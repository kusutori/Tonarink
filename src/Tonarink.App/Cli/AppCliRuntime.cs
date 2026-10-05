using System.Diagnostics;
using LocalSendDotNet;
using Microsoft.UI.Reactor;

namespace Tonarink.Cli;

sealed record CliBindings(LocalSendNodeSession Session, AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings, Action RestoreWindow, string Locale);

static class CliBridge
{
    // UI-only snapshot, replaced on every render. The host belongs to the outer
    // shell, so locale changes cannot replace the listener or outstanding calls.
    public static CliBindings? Current { get; set; }
    public static CliBindings Get() => Current ?? throw new CliException("The app is still initializing.", 3);
}

sealed class AppCliRuntime(Func<CliBindings> bindings) : ICliRuntime
{
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private static int _receiveClients;
    public static bool HasReceiveClient => Volatile.Read(ref _receiveClients) > 0;
    public bool Integrated => true;
    private Task<CliBindings> StateAsync(CancellationToken token) => CliUi.InvokeAsync(bindings, token);
    public async Task<CliState> ReadAsync(CancellationToken token)
    {
        var current = await StateAsync(token).ConfigureAwait(false);
        var state = current.Session.Runtime;
        var s = current.Settings;
        return new(current.Session.Node, current.Session.IsServerDesired, state.NodeState, state.Identity,
            state.Error, state.DiscoveryWarning, state.IncomingTransfers,
            new(s.DownloadDirectory, s.VerifyChecksumsOnSend, s.VerifyChecksumsOnReceive, s.SaveReceiveHistory, current.Locale));
    }

    public Task<CliResponse> ServerAsync(string action, CancellationToken token) => ServerAsync(action, token, null);
    private async Task<CliResponse> ServerAsync(string action, CancellationToken token, AppSettings? settings)
    {
        var previous = await StateAsync(token).ConfigureAwait(false);
        if (action == "start" && previous.Session.Node?.State == LocalSendNodeState.Running)
            return CliProtocol.Success("Server is already running");
        await CliUi.InvokeAsync(() =>
        {
            var current = bindings();
            if (action == "stop") current.Session.Stop();
            else current.Session.StartOrRestartWithSettings(settings ?? current.Settings);
            return true;
        }, token).ConfigureAwait(false);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(45))
        {
            var current = await StateAsync(token).ConfigureAwait(false);
            if (action == "stop" && current.Session.Runtime.NodeState == LocalSendNodeState.Stopped)
                return CliProtocol.Success("Server stopped");
            if (action != "stop" && current.Session.Node?.State == LocalSendNodeState.Running
                && !ReferenceEquals(current.Session.Node, previous.Session.Node))
                return CliProtocol.Success(action == "restart" ? "Server restarted" : "Server started");
            if (action != "stop" && current.Session.Runtime.NodeState == LocalSendNodeState.Faulted
                && (previous.Session.Runtime.NodeState != LocalSendNodeState.Faulted || clock.Elapsed > TimeSpan.FromSeconds(1)))
                throw new CliException(current.Session.Runtime.Error ?? "Server startup failed", 1);
            await Task.Delay(100, token).ConfigureAwait(false);
        }
        throw new CliException("Timed out waiting for the server state change.", 3);
    }

    public async Task<CliResponse> SettingsAsync(CliRequest request, CliArguments args, CancellationToken token)
    {
        if (args.Action == "set")
        {
            await _settingsGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var saved = await (await CliUi.InvokeAsync(async () =>
                {
                    var current = bindings();
                    var next = CliSettings.Set(current.Settings, args.Positionals[0], args.Positionals[1], request.WorkingDirectory);
                    if (next.StartWithWindows != current.Settings.StartWithWindows)
                        await WindowsStartup.SetEnabledAsync(next.StartWithWindows, next.MinimizeToTray);
                    AppSettingsStore.Save(next); current.UpdateSettings(_ => next);
                    AppNotificationService.SetEnabled(next.NotificationsEnabled);
                    return next;
                }, token).ConfigureAwait(false)).ConfigureAwait(false);
                if (args.Has("restart")) return await ServerAsync("restart", token, saved).ConfigureAwait(false);
            }
            finally { _settingsGate.Release(); }
            return CliProtocol.Success("Setting saved; server-related changes apply after server restart");
        }
        var entries = CliSettings.List((await StateAsync(token).ConfigureAwait(false)).Settings, args.Has("show-secrets"));
        if (args.Action == "get")
        {
            entries = entries.Where(e => e.Key.Equals(args.Positionals[0], StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length == 0) throw new CliException("Unknown settings key: " + args.Positionals[0]);
        }
        return CliProtocol.Result(string.Join('\n', entries.Select(e => $"{e.Key}\t{e.Value}")), entries, CliJsonContext.Default.CliSettingArray);
    }
    public Task<CliFavorite[]> FavoritesAsync(CancellationToken token) => CliUi.InvokeAsync(() =>
        FavoriteDeviceStore.Entries.Values.Select(f => new CliFavorite(f.Fingerprint, f.Name, f.Address, f.Port, f.DeviceType.ToString())).ToArray(), token);
    public Task SaveFavoriteAsync(LocalSendDevice device, CancellationToken token) => CliUi.InvokeAsync(() =>
    {
        var endpoint = device.PreferredEndpoint ?? throw new CliException("Device has no usable address.", 3);
        FavoriteDeviceStore.Upsert(new(device.Fingerprint, device.Alias, endpoint.Address.ToString(), endpoint.Port, device.DeviceType));
        return true;
    }, token);
    public Task RemoveFavoriteAsync(string fingerprint, CancellationToken token) => CliUi.InvokeAsync(() =>
    {
        var key = FavoriteDeviceStore.Entries.Keys.FirstOrDefault(k => k.Equals(fingerprint, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException("Favorite fingerprint not found.", 3);
        FavoriteDeviceStore.Remove(key); return true;
    }, token);
    public Task<CliHistory[]> HistoryAsync(CancellationToken token) => CliUi.InvokeAsync(() => ReceiveHistoryStore.Entries
        .Select(e => new CliHistory(e.Id, e.FileName, e.Path, e.Size, e.SenderAlias, e.ReceivedAt)).ToArray(), token);
    public Task RemoveHistoryAsync(Guid? id, CancellationToken token) => CliUi.InvokeAsync(() =>
    {
        if (id is null) ReceiveHistoryStore.Clear();
        else
        {
            if (!ReceiveHistoryStore.Entries.Any(e => e.Id == id)) throw new CliException("History entry not found.", 3);
            ReceiveHistoryStore.Remove(id.Value);
        }
        return true;
    }, token);
    public Task RecordReceiveAsync(IncomingTransferRequest request, ReceiveOutcome.Completed result, CancellationToken token) => CliUi.InvokeAsync(() =>
    {
        if (CoreTransferOutcomeMapper.Map(result) is IncomingTransferResult.Completed saved) ReceiveHistoryStore.Record(request.Sender.Alias, saved);
        return true;
    }, token);
    public Task DismissIncomingAsync(Guid id, CancellationToken token) => CliUi.InvokeAsync(() => { bindings().Session.DismissIncoming(id); return true; }, token);
    public IDisposable BeginReceiveWatch()
    { Interlocked.Increment(ref _receiveClients); return new CliScope(() => Interlocked.Decrement(ref _receiveClients)); }
    public Task OpenAppAsync(CancellationToken token) => CliUi.InvokeAsync(() => { bindings().RestoreWindow(); return true; }, token);
    public Task QuitAsync(CancellationToken token) => CliUi.InvokeAsync(() => { ReactorApp.Exit(); return true; }, token);
}

static class CliUi
{
    public static Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = ReactorApp.UIDispatcher;
        if (dispatcher is null || !dispatcher.TryEnqueue(() =>
        {
            if (token.IsCancellationRequested) { completion.TrySetCanceled(token); return; }
            try { completion.TrySetResult(action()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        })) completion.TrySetException(new CliException("The app dispatcher is unavailable.", 3));
        return completion.Task.WaitAsync(token);
    }
}
