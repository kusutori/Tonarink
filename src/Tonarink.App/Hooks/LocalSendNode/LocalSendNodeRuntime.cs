using LocalSendDotNet;

namespace Tonarink.Hooks.LocalSendNode;

sealed record AppRuntimeState(
    LocalSendNodeState NodeState,
    LocalSendIdentity? Identity,
    IReadOnlyList<LocalSendDevice> Devices,
    IReadOnlyList<IncomingTransferRequest> IncomingTransfers,
    IReadOnlyDictionary<string, IReadOnlyList<DeviceActivityEntry>> DeviceActivity,
    string? Error,
    string? AppliedMulticastGroup,
    string? DiscoveryWarning,
    string? AppliedReceivePin,
    IReadOnlyList<string>? AppliedNetworkWhitelist,
    IReadOnlyList<string>? AppliedNetworkBlacklist)
{
    public static readonly AppRuntimeState Initial = new(
        LocalSendNodeState.Created,
        Identity: null,
        Devices: [],
        IncomingTransfers: [],
        DeviceActivity: new Dictionary<string, IReadOnlyList<DeviceActivityEntry>>(StringComparer.Ordinal),
        Error: null,
        AppliedMulticastGroup: null,
        DiscoveryWarning: null,
        AppliedReceivePin: null,
        AppliedNetworkWhitelist: null,
        AppliedNetworkBlacklist: null);
}

union AppRuntimeAction(
    AppRuntimeAction.Starting,
    AppRuntimeAction.Stopping,
    AppRuntimeAction.Stopped,
    AppRuntimeAction.Started,
    AppRuntimeAction.DevicesChanged,
    AppRuntimeAction.Refreshed,
    AppRuntimeAction.IncomingAdded,
    AppRuntimeAction.IncomingDismissed,
    AppRuntimeAction.ErrorReported,
    AppRuntimeAction.Failed)
{
    public sealed record Starting;

    public sealed record Stopping;

    public sealed record Stopped;

    public sealed record Started(
        LocalSendNodeState NodeState,
        LocalSendIdentity? Identity,
        IReadOnlyList<LocalSendDevice> Devices,
        string AppliedMulticastGroup,
        string? AppliedReceivePin,
        string? DiscoveryWarning,
        IReadOnlyList<string>? AppliedNetworkWhitelist,
        IReadOnlyList<string>? AppliedNetworkBlacklist);

    public sealed record DevicesChanged(
        IReadOnlyList<LocalSendDevice> Devices,
        DeviceChange Change);

    public sealed record Refreshed(
        IReadOnlyList<LocalSendDevice> Devices,
        string? DiscoveryWarning);

    public sealed record IncomingAdded(IncomingTransferRequest Request);

    public sealed record IncomingDismissed(Guid RequestId);

    public sealed record ErrorReported(string Message);

    public sealed record Failed(LocalSendNodeState NodeState, string Message);
}

sealed record DeviceActivityEntry(
    DeviceChangeKind Kind,
    DateTimeOffset Timestamp,
    IReadOnlyList<DeviceEndpoint> Endpoints);

static class LocalSendNodeRuntimeReducer
{
    public static AppRuntimeState Reduce(AppRuntimeState state, AppRuntimeAction action) => action switch
    {
        AppRuntimeAction.Starting => state with
        {
            NodeState = LocalSendNodeState.Starting,
            Devices = [],
            IncomingTransfers = [],
            Error = null,
            DiscoveryWarning = null,
        },
        AppRuntimeAction.Stopping => state with
        {
            NodeState = LocalSendNodeState.Stopping,
            Devices = [],
            IncomingTransfers = [],
            Error = null,
            DiscoveryWarning = null,
        },
        AppRuntimeAction.Stopped => state with
        {
            NodeState = LocalSendNodeState.Stopped,
            Devices = [],
            IncomingTransfers = [],
            Error = null,
            DiscoveryWarning = null,
        },
        AppRuntimeAction.Started started => state with
        {
            NodeState = started.NodeState,
            Identity = started.Identity,
            Devices = started.Devices,
            Error = null,
            AppliedMulticastGroup = started.AppliedMulticastGroup,
            AppliedReceivePin = started.AppliedReceivePin,
            DiscoveryWarning = started.DiscoveryWarning,
            AppliedNetworkWhitelist = started.AppliedNetworkWhitelist,
            AppliedNetworkBlacklist = started.AppliedNetworkBlacklist,
        },
        AppRuntimeAction.DevicesChanged changed => state with
        {
            Devices = changed.Devices,
            DeviceActivity = AppendDeviceActivity(state.DeviceActivity, changed.Change),
        },
        AppRuntimeAction.Refreshed refreshed => state with
        {
            Devices = refreshed.Devices,
            Error = null,
            DiscoveryWarning = refreshed.DiscoveryWarning,
        },
        AppRuntimeAction.IncomingAdded added => state with
        {
            IncomingTransfers = (IncomingTransferRequest[])[.. state.IncomingTransfers, added.Request],
        },
        AppRuntimeAction.IncomingDismissed dismissed => state with
        {
            IncomingTransfers = (IncomingTransferRequest[])
            [
                .. state.IncomingTransfers.Where(request => request.RequestId != dismissed.RequestId)
            ],
        },
        AppRuntimeAction.ErrorReported reported => state with { Error = reported.Message },
        AppRuntimeAction.Failed failed => state with
        {
            NodeState = failed.NodeState,
            Error = failed.Message,
            DiscoveryWarning = null,
        },
    };

    private static IReadOnlyDictionary<string, IReadOnlyList<DeviceActivityEntry>> AppendDeviceActivity(
        IReadOnlyDictionary<string, IReadOnlyList<DeviceActivityEntry>> activity,
        DeviceChange change)
    {
        var updated = activity.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal);
        var existing = updated.GetValueOrDefault(change.Device.Fingerprint) ?? [];
        updated[change.Device.Fingerprint] = (DeviceActivityEntry[])
        [
            .. existing
                .Append(new DeviceActivityEntry(change.Kind, DateTimeOffset.Now, change.Device.Endpoints))
                .TakeLast(100)
        ];
        return updated;
    }
}
