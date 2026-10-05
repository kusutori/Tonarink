using LocalSendDotNet;

namespace Tonarink.Cli;

sealed record CliPreferences(string DownloadDirectory, bool VerifyChecksumsOnSend,
    bool VerifyChecksumsOnReceive, bool SaveReceiveHistory, string Locale);

sealed record CliState(LocalSendNode? Node, bool IsServerDesired, LocalSendNodeState NodeState,
    LocalSendIdentity? Identity, string? Error, string? DiscoveryWarning,
    IReadOnlyList<IncomingTransferRequest> IncomingTransfers, CliPreferences Settings);

// The command engine has no dependency on WinUI, GUI stores or a dispatcher.
// Each host owns its node and persistence; commands share exactly the same flow.
interface ICliRuntime
{
    bool Integrated { get; }
    Task<CliState> ReadAsync(CancellationToken token);
    Task<CliResponse> ServerAsync(string action, CancellationToken token);
    Task<CliResponse> SettingsAsync(CliRequest request, CliArguments args, CancellationToken token);
    Task<CliFavorite[]> FavoritesAsync(CancellationToken token);
    Task SaveFavoriteAsync(LocalSendDevice device, CancellationToken token);
    Task RemoveFavoriteAsync(string fingerprint, CancellationToken token);
    Task<CliHistory[]> HistoryAsync(CancellationToken token);
    Task RemoveHistoryAsync(Guid? id, CancellationToken token);
    Task RecordReceiveAsync(IncomingTransferRequest request, ReceiveOutcome.Completed result, CancellationToken token);
    Task DismissIncomingAsync(Guid id, CancellationToken token);
    IDisposable BeginReceiveWatch();
    Task OpenAppAsync(CancellationToken token);
    Task QuitAsync(CancellationToken token);
}

static class CliPath
{
    public static string FullPath(string value, string directory)
    {
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value), directory); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        { throw new CliException("Invalid path: " + value); }
    }
}

sealed class CliScope(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
