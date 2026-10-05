using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LocalSendDotNet;

namespace Tonarink.Cli;

sealed class StandaloneCliRuntime : ICliRuntime, IAsyncDisposable
{
    private readonly string _profile;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _serverGate = new(1, 1);
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, IncomingTransferRequest> _pending = new();
    private readonly TaskCompletionSource _quit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private StandaloneSettings _settings;
    private CliFavorite[] _favorites;
    private CliHistory[] _history;
    private LocalSendNode? _node;
    private LocalSendNodeState _state = LocalSendNodeState.Stopped;
    private bool _desired;
    private string? _error;
    private CancellationTokenSource? _watchCancellation;
    private Task? _watching;
    public bool Integrated => false;

    private StandaloneCliRuntime(string profile)
    {
        _profile = profile;
        _settings = ReadFile("settings.json", StandaloneJsonContext.Default.StandaloneSettings) ?? new();
        _settings.Validate();
        _favorites = ReadFile("favorites.json", CliJsonContext.Default.CliFavoriteArray) ?? [];
        _history = ReadFile("history.json", CliJsonContext.Default.CliHistoryArray) ?? [];
    }
    public static async Task<int> RunHostAsync(string profile)
    {
        Directory.CreateDirectory(profile);
        FileStream ownership;
        try { ownership = new(Path.Combine(profile, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return 0; } // another startup won this profile
        using (ownership)
        {
            try
            {
                await using var runtime = new StandaloneCliRuntime(profile);
                using var host = new CliHost(runtime, profile, runtime.Report);
                var listening = host.RunAsync();
                var completed = await Task.WhenAny(listening, runtime._quit.Task).ConfigureAwait(false);
                if (completed == listening) await listening.ConfigureAwait(false);
                host.Dispose();
                await listening.ConfigureAwait(false);
                return 0;
            }
            catch (Exception exception)
            {
                File.AppendAllText(Path.Combine(profile, "cli.log"), $"{DateTimeOffset.Now:O} {exception}\n");
                return 1;
            }
        }
    }
    public Task<CliState> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(new CliState(_node, _desired, _node?.State ?? _state, _node?.Identity,
                _error, _node?.DiscoveryError, _pending.Values.ToArray(),
                new(_settings.DownloadDirectory, _settings.SendChecksums, _settings.ReceiveChecksums, _settings.ReceiveHistory, _settings.Language)));
    }
    public async Task<CliResponse> ServerAsync(CliServerAction action, CancellationToken token)
    {
        await _serverGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (_gate)
                if (action == CliServerAction.Start && _node?.State == LocalSendNodeState.Running) return CliProtocol.Success(CliText.Get("Server is already running"));
            await StopNodeAsync().ConfigureAwait(false);
            if (action == CliServerAction.Stop) return CliProtocol.Success(CliText.Get("Server stopped"));
            StandaloneSettings settings;
            lock (_gate) { settings = _settings; _state = LocalSendNodeState.Starting; _desired = true; _error = null; }
            var node = new LocalSendNode(settings.NodeOptions(_profile));
            lock (_gate) _node = node;
            try
            {
                await node.StartAsync(token).ConfigureAwait(false);
                _watchCancellation = new();
                _watching = WatchAsync(node, _watchCancellation.Token);
            }
            catch (Exception exception)
            { lock (_gate) { _state = LocalSendNodeState.Faulted; _error = exception.Message; } throw; }
            return CliProtocol.Success(action == CliServerAction.Restart ? CliText.Get("Server restarted") : CliText.Get("Server started"));
        }
        finally { _serverGate.Release(); }
    }
    private async Task StopNodeAsync()
    {
        LocalSendNode? node;
        lock (_gate) { node = _node; _node = null; _desired = false; _state = LocalSendNodeState.Stopping; }
        if (_watchCancellation is not null) await _watchCancellation.CancelAsync().ConfigureAwait(false);
        if (node is not null) await node.DisposeAsync().ConfigureAwait(false);
        if (_watching is not null) await _watching.ConfigureAwait(false);
        _watchCancellation?.Dispose(); _watchCancellation = null; _watching = null; _pending.Clear();
        lock (_gate) { _state = LocalSendNodeState.Stopped; _error = null; }
    }
    private async Task WatchAsync(LocalSendNode node, CancellationToken token)
    {
        try
        {
            await foreach (var request in node.WatchIncomingTransfersAsync(token).ConfigureAwait(false))
            {
                bool accept;
                lock (_gate) accept = _settings.AutoSave == "on" || (_settings.AutoSave == "favorites"
                    && _favorites.Any(f => f.Fingerprint.Equals(request.Sender.Fingerprint, StringComparison.OrdinalIgnoreCase)));
                if (accept) _ = AutoAcceptAsync(node, request, token);
                else _pending[request.RequestId] = request;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { Report("Incoming watcher failed", exception); }
    }
    private async Task AutoAcceptAsync(LocalSendNode node, IncomingTransferRequest request, CancellationToken token)
    {
        try
        {
            var s = (await ReadAsync(token).ConfigureAwait(false)).Settings;
            var result = await node.AcceptAsync(request.RequestId, new() { DestinationDirectory = s.DownloadDirectory, VerifySha256 = s.VerifyChecksumsOnReceive }, cancellationToken: token).ConfigureAwait(false);
            if (result is ReceiveOutcome.Completed completed && s.SaveReceiveHistory) await RecordReceiveAsync(request, completed, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { Report("Automatic receive failed", exception); }
    }
    public async Task<CliResponse> SaveSettingAsync(string key, string value, string workingDirectory, bool restart, CancellationToken token)
    {
        await _settingsGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                var next = _settings.Set(key, value, workingDirectory);
                Save("settings.json", next, StandaloneJsonContext.Default.StandaloneSettings); _settings = next;
            }
            if (restart) return await ServerAsync(CliServerAction.Restart, token).ConfigureAwait(false);
            return CliProtocol.Success(CliText.Get("Setting saved; server-related changes apply after server restart"));
        }
        finally { _settingsGate.Release(); }
    }
    public Task<CliSetting[]> ReadSettingsAsync(bool showSecrets, CancellationToken token)
    { token.ThrowIfCancellationRequested(); lock (_gate) return Task.FromResult(_settings.List(showSecrets)); }
    public Task<CliFavorite[]> FavoritesAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); lock (_gate) return Task.FromResult(_favorites); }
    public Task SaveFavoriteAsync(LocalSendDevice device, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var e = device.PreferredEndpoint ?? throw new CliException(CliText.Get("Device has no usable endpoint."), 3);
        lock (_gate)
        {
            var next = _favorites.Where(f => f.Fingerprint != device.Fingerprint).Append(new(device.Fingerprint, device.Alias, e.Address.ToString(), e.Port, device.DeviceType.ToString())).ToArray();
            Save("favorites.json", next, CliJsonContext.Default.CliFavoriteArray); _favorites = next;
        }
        return Task.CompletedTask;
    }
    public Task RemoveFavoriteAsync(string fingerprint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var next = _favorites.Where(f => !f.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (next.Length == _favorites.Length) throw new CliException(CliText.Get("Favorite fingerprint not found."), 3);
            Save("favorites.json", next, CliJsonContext.Default.CliFavoriteArray); _favorites = next;
        }
        return Task.CompletedTask;
    }
    public Task<CliHistory[]> HistoryAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); lock (_gate) return Task.FromResult(_history); }
    public Task RemoveHistoryAsync(Guid? id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (id is not null && !_history.Any(e => e.Id == id)) throw new CliException(CliText.Get("History entry not found."), 3);
            var next = id is null ? [] : _history.Where(e => e.Id != id).ToArray();
            Save("history.json", next, CliJsonContext.Default.CliHistoryArray); _history = next;
        }
        return Task.CompletedTask;
    }
    public Task RecordReceiveAsync(IncomingTransferRequest request, ReceiveOutcome.Completed result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var entries = result.Items.Where(i => i.SavedPath is not null).Select(i =>
            new CliHistory(Guid.NewGuid(), i.FileName, i.SavedPath!, i.BytesTransferred, request.Sender.Alias, DateTimeOffset.UtcNow));
        lock (_gate)
        {
            var next = entries.Concat(_history).Take(500).ToArray();
            Save("history.json", next, CliJsonContext.Default.CliHistoryArray); _history = next;
        }
        return Task.CompletedTask;
    }
    public Task DismissIncomingAsync(Guid id, CancellationToken token) { token.ThrowIfCancellationRequested(); _pending.TryRemove(id, out _); return Task.CompletedTask; }
    public IDisposable BeginReceiveWatch() => new CliScope(() => { });
    public Task OpenAppAsync(string? favorite, Guid? history, CancellationToken token) => throw new CliException(CliText.Get("Standalone CLI has no graphical window."), 3);
    public Task QuitAsync(CancellationToken token) { _quit.TrySetResult(); return Task.CompletedTask; }
    public async ValueTask DisposeAsync() { await _serverGate.WaitAsync().ConfigureAwait(false); try { await StopNodeAsync().ConfigureAwait(false); } finally { _serverGate.Release(); } }
    private T? ReadFile<T>(string file, JsonTypeInfo<T> type) => File.Exists(Path.Combine(_profile, file))
        ? JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(_profile, file)), type) : default;
    private void Save<T>(string file, T value, JsonTypeInfo<T> type)
    {
        var target = Path.Combine(_profile, file); var temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, type)); File.Move(temporary, target, overwrite: true);
    }
    private void Report(string operation, Exception exception)
    { lock (_gate) File.AppendAllText(Path.Combine(_profile, "cli.log"), $"{DateTimeOffset.Now:O} {operation}: {exception}\n"); }
}
