using LocalSendDotNet;

namespace Tonarink.Services;

sealed record TrayFlyoutSnapshot(
    AppRuntimeState Runtime,
    OutgoingTransferViewState? Outgoing,
    WidgetTransferInfo? IncomingProgress,
    AppSettings Settings,
    bool ServerDesired)
{
    public static readonly TrayFlyoutSnapshot Empty = new(
        AppRuntimeState.Initial,
        Outgoing: null,
        IncomingProgress: null,
        AppSettings.Default,
        ServerDesired: true);
}

sealed record TraySendRequest(
    LocalSendDevice Device,
    IReadOnlyList<SendItem> Items,
    long TotalBytes,
    string? Pin);

static class TrayFlyoutStore
{
    private static readonly Lock Gate = new();
    private static TrayFlyoutSnapshot _snapshot = TrayFlyoutSnapshot.Empty;

    public static event Action? Changed;

    public static Action Restore { get; set; } = static () => { };

    public static Action StartServer { get; set; } = static () => { };

    public static Action StopServer { get; set; } = static () => { };

    public static Func<TraySendRequest, Task> SendAsync { get; set; } =
        static _ => Task.FromException(new InvalidOperationException("The send service is unavailable."));

    public static TrayFlyoutSnapshot Snapshot
    {
        get
        {
            lock (Gate)
                return _snapshot;
        }
    }

    public static void Publish(
        AppRuntimeState runtime,
        OutgoingTransferViewState? outgoing,
        AppSettings settings,
        bool serverDesired)
    {
        lock (Gate)
        {
            _snapshot = new(
                runtime,
                outgoing,
                WidgetAppHost.Incoming,
                settings,
                serverDesired);
        }

        Changed?.Invoke();
    }

    public static void NotifyIncoming()
    {
        lock (Gate)
            _snapshot = _snapshot with { IncomingProgress = WidgetAppHost.Incoming };
        Changed?.Invoke();
    }
}
