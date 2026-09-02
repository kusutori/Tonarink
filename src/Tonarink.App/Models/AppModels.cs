using System.Net;
using System.Net.Sockets;
using LocalSendDotNet;

enum AppRoute
{
    Receive,
    History,
    Send,
    Settings,
    NetworkInterfaces,
    WebShare,
    WebReceive,
}

enum AutoSaveMode
{
    Off,
    Favorites,
    On,
}

sealed record AppSettings(
    string Alias,
    AutoSaveMode AutoSave,
    int ThemeIndex,
    int LanguageIndex,
    bool MinimizeToTray,
    bool StartWithWindows,
    bool FavoritesOnly,
    string DownloadDirectory,
    LocalSendDeviceType DeviceType,
    string DeviceModel,
    int Port,
    int DiscoveryTimeoutMs,
    bool EnableHttps,
    string MulticastGroup,
    IReadOnlyList<string>? NetworkWhitelist,
    IReadOnlyList<string>? NetworkBlacklist,
    bool ShowExplorerContextMenu)
{
    public static readonly AppSettings Default = new(
        Alias: string.IsNullOrWhiteSpace(Environment.UserName) ? Environment.MachineName : Environment.UserName,
        AutoSave: AutoSaveMode.Off,
        ThemeIndex: 0,
        LanguageIndex: 0,
        MinimizeToTray: false,
        StartWithWindows: false,
        FavoritesOnly: false,
        DownloadDirectory: AppPlatform.DefaultDownloadDirectory,
        DeviceType: LocalSendDeviceType.Desktop,
        DeviceModel: "",
        Port: LocalSendOptions.DefaultPort,
        DiscoveryTimeoutMs: 500,
        EnableHttps: true,
        MulticastGroup: LocalSendOptions.DefaultMulticastAddress.ToString(),
        NetworkWhitelist: null,
        NetworkBlacklist: null,
        ShowExplorerContextMenu: true);

    public string ResolvedAlias =>
        string.IsNullOrWhiteSpace(Alias) ? Default.Alias : Alias.Trim();

    public string ResolvedDeviceModel =>
        string.IsNullOrWhiteSpace(DeviceModel) ? Environment.MachineName : DeviceModel.Trim();

    public IPAddress ResolvedMulticastAddress =>
        IPAddress.TryParse(MulticastGroup, out var address)
            && address.AddressFamily == AddressFamily.InterNetwork
                ? address
                : LocalSendOptions.DefaultMulticastAddress;
}

sealed record AppRuntimeState(
    LocalSendNodeState NodeState,
    LocalSendIdentity? Identity,
    IReadOnlyList<LocalSendDevice> Devices,
    IReadOnlyList<IncomingTransferRequest> IncomingTransfers,
    string? Error,
    string? AppliedMulticastGroup,
    string? DiscoveryWarning,
    IReadOnlyList<string>? AppliedNetworkWhitelist,
    IReadOnlyList<string>? AppliedNetworkBlacklist)
{
    public static readonly AppRuntimeState Initial = new(
        LocalSendNodeState.Created,
        Identity: null,
        Devices: Array.Empty<LocalSendDevice>(),
        IncomingTransfers: Array.Empty<IncomingTransferRequest>(),
        Error: null,
        AppliedMulticastGroup: null,
        DiscoveryWarning: null,
        AppliedNetworkWhitelist: null,
        AppliedNetworkBlacklist: null);
}

sealed record OutgoingTransferViewState(
    LocalSendIdentity? Sender,
    LocalSendDevice Receiver,
    string ContentSummary,
    TransferState State,
    long BytesTransferred,
    long TotalBytes,
    string Status,
    bool IsPending,
    bool IsError,
    Action Cancel);

sealed record ShareTargetPayload(
    Guid Id,
    IReadOnlyList<ShareTargetItem> Items);

abstract record ShareTargetItem
{
    public sealed record FileSystem(string Path, bool IsDirectory) : ShareTargetItem;

    public sealed record Text(string Value, string FileName) : ShareTargetItem;
}

sealed record ReceiveHistoryEntry(
    Guid Id,
    string FileName,
    string Path,
    long Size,
    string SenderAlias,
    DateTimeOffset ReceivedAt);
