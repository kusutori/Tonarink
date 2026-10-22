using System.Diagnostics;
using LocalSendDotNet;
using Microsoft.UI.Reactor;

namespace Tonarink.Cli;

sealed record CliBindings(LocalSendNodeSession Session,
    Action<Func<AppSettings, AppSettings>> UpdateSettings, string Locale);

static class CliBridge
{
    // UI-only snapshot, replaced on every render. The host belongs to the outer
    // shell, so locale changes cannot replace the listener or outstanding calls.
    public static CliBindings? Current { get; set; }
    public static CliBindings Get() => Current ?? throw new CliException(CliText.Get("The app is still initializing."), 3);
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
        var s = AppSettingsStore.Load();
        return new(current.Session.Node, current.Session.IsServerDesired, state.NodeState, state.Identity,
            state.Error, state.DiscoveryWarning, state.IncomingTransfers,
            new(s.DownloadDirectory, s.VerifyChecksumsOnSend, s.VerifyChecksumsOnReceive, s.SaveReceiveHistory, current.Locale));
    }

    public Task<CliResponse> ServerAsync(CliServerAction action, CancellationToken token) => ServerAsync(action, token, null);
    private async Task<CliResponse> ServerAsync(CliServerAction action, CancellationToken token, AppSettings? settings)
    {
        var previous = await StateAsync(token).ConfigureAwait(false);
        if (action == CliServerAction.Start && previous.Session.Node?.State == LocalSendNodeState.Running)
            return CliProtocol.Success(CliText.Get("Server is already running"));
        await CliUi.InvokeAsync(() =>
        {
            var current = bindings();
            if (action == CliServerAction.Stop) current.Session.Stop();
            else current.Session.StartOrRestartWithSettings(settings ?? AppSettingsStore.Load());
            return true;
        }, token).ConfigureAwait(false);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(45))
        {
            var current = await StateAsync(token).ConfigureAwait(false);
            if (action == CliServerAction.Stop && current.Session.Runtime.NodeState == LocalSendNodeState.Stopped)
                return CliProtocol.Success(CliText.Get("Server stopped"));
            if (action != CliServerAction.Stop && current.Session.Node?.State == LocalSendNodeState.Running
                && !ReferenceEquals(current.Session.Node, previous.Session.Node))
                return CliProtocol.Success(action == CliServerAction.Restart ? CliText.Get("Server restarted") : CliText.Get("Server started"));
            if (action != CliServerAction.Stop && current.Session.Runtime.NodeState == LocalSendNodeState.Faulted
                && (previous.Session.Runtime.NodeState != LocalSendNodeState.Faulted || clock.Elapsed > TimeSpan.FromSeconds(1)))
                throw new CliException(current.Session.Runtime.Error ?? CliText.Get("Server startup failed"), 1);
            await Task.Delay(100, token).ConfigureAwait(false);
        }
        throw new CliException(CliText.Get("Timed out waiting for the server state change."), 3);
    }

    public async Task<CliResponse> SaveSettingAsync(string key, string value, string workingDirectory, bool restart, CancellationToken token)
    {
        await _settingsGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var saved = await (await CliUi.InvokeAsync(async () =>
            {
                var current = bindings();
                // Read the committed snapshot: the renderer may not have caught
                // up with a preceding CLI write yet, even on the UI dispatcher.
                var previous = AppSettingsStore.Load();
                var next = CliSettings.Set(previous, key, value, workingDirectory);
                if (next.StartWithWindows != previous.StartWithWindows)
                    await WindowsStartup.SetEnabledAsync(next.StartWithWindows, next.MinimizeToTray);
                if (next.ShowInSendToMenu != previous.ShowInSendToMenu)
                {
                    if (AppPlatform.HasPackageIdentity())
                        throw new CliException(CliText.Get("Send To integration is only available in unpackaged builds."), 3);
                    WindowsSendTo.SetEnabled(next.ShowInSendToMenu);
                }
                AppSettingsStore.Save(next); current.UpdateSettings(_ => next);
                AppNotificationService.SetEnabled(next.NotificationsEnabled);
                return next;
            }, token).ConfigureAwait(false)).ConfigureAwait(false);
            if (restart) return await ServerAsync(CliServerAction.Restart, token, saved).ConfigureAwait(false);
        }
        finally { _settingsGate.Release(); }
        return CliProtocol.Success(CliText.Get("Setting saved; server-related changes apply after server restart"));
    }
    public Task<CliSetting[]> ReadSettingsAsync(bool showSecrets, CancellationToken token) =>
        CliUi.InvokeAsync(() => CliSettings.List(AppSettingsStore.Load(), showSecrets), token);
    public Task<CliFavorite[]> FavoritesAsync(CancellationToken token) => CliUi.InvokeAsync(() =>
        FavoriteDeviceStore.Entries.Values.Select(f => new CliFavorite(f.Fingerprint, f.Name, f.Address, f.Port, f.DeviceType.ToString())).ToArray(), token);
    public Task SaveFavoriteAsync(LocalSendDevice device, CancellationToken token) => CliUi.InvokeAsync(() =>
    {
        var endpoint = device.PreferredEndpoint ?? throw new CliException(CliText.Get("Device has no usable address."), 3);
        FavoriteDeviceStore.Upsert(new(device.Fingerprint, device.Alias, endpoint.Address.ToString(), endpoint.Port, device.DeviceType));
        return true;
    }, token);
    public Task RemoveFavoriteAsync(string fingerprint, CancellationToken token) => CliUi.InvokeAsync(() =>
    {
        var key = FavoriteDeviceStore.Entries.Keys.FirstOrDefault(k => k.Equals(fingerprint, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException(CliText.Get("Favorite fingerprint not found."), 3);
        FavoriteDeviceStore.Remove(key); return true;
    }, token);
    public Task<CliHistory[]> HistoryAsync(CancellationToken token) => CliUi.InvokeAsync(() => ReceiveHistoryStore.Entries
        .Select(e => new CliHistory(e.Id, e.FileName, e.Path, e.Size, e.SenderAlias, e.ReceivedAt)).ToArray(), token);
    public Task RemoveHistoryAsync(Guid? id, CancellationToken token) => CliUi.InvokeAsync(() =>
    {
        if (id is null) ReceiveHistoryStore.Clear();
        else
        {
            if (!ReceiveHistoryStore.Entries.Any(e => e.Id == id)) throw new CliException(CliText.Get("History entry not found."), 3);
            ReceiveHistoryStore.Remove(id.Value);
        }
        return true;
    }, token);
    public Task RecordReceiveAsync(IncomingTransferRequest request, ReceiveOutcome.Completed result, CancellationToken token) => CliUi.InvokeAsync(() =>
    {
        if (CoreTransferOutcomeMapper.Map(result) is IncomingTransferResult.Completed saved) ReceiveHistoryStore.TryRecord(request.Sender.Alias, saved);
        return true;
    }, token);
    public Task DismissIncomingAsync(Guid id, CancellationToken token) => CliUi.InvokeAsync(() => { bindings().Session.DismissIncoming(id); return true; }, token);
    public IDisposable BeginReceiveWatch()
    { Interlocked.Increment(ref _receiveClients); return new CliScope(() => Interlocked.Decrement(ref _receiveClients)); }
    public Task OpenAppAsync(string? favorite, Guid? history, CancellationToken token) =>
        AppNavigationService.OpenAsync(favorite is not null ? AppNavigationRequest.ForFavorite(favorite)
            : history is { } id ? AppNavigationRequest.ForHistory(id) : new(), token);
    public Task OpenAppAsync(string? favorite, Guid? history, IReadOnlyList<string> paths,
        string workingDirectory, CancellationToken token)
    {
        if (paths.Count == 0) return OpenAppAsync(favorite, history, token);
        token.ThrowIfCancellationRequested();
        if (history is not null) throw new CliException(CliText.Get("Do not combine paths with --history."), 2);
        var items = paths.Select(path => CliPath.FullPath(path, workingDirectory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                    throw new CliException(CliText.Get("File or folder does not exist: {0}", path), 2);
                return (ShareTargetItem)new ShareTargetItem.FileSystem(path, Directory.Exists(path));
            }).ToArray();
        return AppNavigationService.OpenAsync(new(AppRoute.Send, Favorite: favorite,
            Payload: new(Guid.NewGuid(), items)), token);
    }
    public Task QuitAsync(CancellationToken token) => CliUi.InvokeAsync(() => { ReactorApp.Exit(); return true; }, token);
}

static class CliUi
{
    public static Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = ReactorApp.UIDispatcher;
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        if (dispatcher is null || !dispatcher.TryEnqueue(() =>
        {
            if (token.IsCancellationRequested) { completion.TrySetCanceled(token); return; }
            try { using var language = CliText.UseCulture(culture); completion.TrySetResult(action()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        })) completion.TrySetException(new CliException(CliText.Get("The app dispatcher is unavailable."), 3));
        return completion.Task.WaitAsync(token);
    }
}
