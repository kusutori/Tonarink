using System.Net;
using System.Net.Sockets;
using LocalSendDotNet;
using Microsoft.UI.Reactor.Navigation;

namespace Tonarink.Models;

enum AppRoute
{
    Receive,
    History,
    Send,
    Settings,
    NetworkInterfaces,
    WebShare,
    WebReceive,
    DeviceDetails,
}

static class AppNavigation
{
    public static readonly NavigateOptions DrillIn = new()
    {
        Transition = NavigationTransition.DrillIn(),
    };

    public static bool IsDetail(AppRoute route) => route is
        AppRoute.History or AppRoute.NetworkInterfaces or AppRoute.WebShare or AppRoute.WebReceive
        or AppRoute.DeviceDetails;
}

enum AutoSaveMode
{
    Off,
    Favorites,
    On,
}

enum NotificationDefaultAction
{
    OpenFile,
    ShowInFolder,
}

sealed record AppSettings(
    string Alias,
    AutoSaveMode AutoSave,
    int ThemeIndex,
    int LanguageIndex,
    bool MinimizeToTray,
    bool TrayClickOpensFlyout,
    bool StartWithWindows,
    bool ShowFavoriteDevicesInWindowsShare,
    bool NotificationsEnabled,
    NotificationDefaultAction NotificationDefaultAction,
    bool KeepItemsForMultipleReceivers,
    bool VerifyChecksumsOnSend,
    bool SaveReceiveHistory,
    bool VerifyChecksumsOnReceive,
    bool ReceivePinEnabled,
    string ReceivePin,
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
        TrayClickOpensFlyout: true,
        StartWithWindows: false,
        ShowFavoriteDevicesInWindowsShare: false,
        NotificationsEnabled: true,
        NotificationDefaultAction: NotificationDefaultAction.OpenFile,
        KeepItemsForMultipleReceivers: false,
        VerifyChecksumsOnSend: true,
        SaveReceiveHistory: true,
        VerifyChecksumsOnReceive: true,
        ReceivePinEnabled: false,
        ReceivePin: "1234",
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

    public string? ResolvedReceivePin =>
        ReceivePinEnabled && !string.IsNullOrWhiteSpace(ReceivePin) ? ReceivePin.Trim() : null;

    public IPAddress ResolvedMulticastAddress =>
        IPAddress.TryParse(MulticastGroup, out var address)
        && address.AddressFamily == AddressFamily.InterNetwork
            ? address
            : LocalSendOptions.DefaultMulticastAddress;
}

sealed record OutgoingTransferSnapshot(
    LocalSendIdentity? Sender,
    LocalSendDevice Receiver,
    string ContentSummary,
    TransferState State,
    long BytesTransferred,
    long TotalBytes,
    string Status,
    Action Cancel);

union OutgoingTransferViewState(
    OutgoingTransferViewState.Pending,
    OutgoingTransferViewState.AwaitingPin,
    OutgoingTransferViewState.Finished)
{
    public sealed record Pending(OutgoingTransferSnapshot Transfer);

    public sealed record AwaitingPin(OutgoingTransferSnapshot Transfer, OutgoingPinPrompt Prompt);

    public sealed record Finished(OutgoingTransferSnapshot Transfer, bool IsError);

    public OutgoingTransferSnapshot Transfer => this switch
    {
        Pending(var transfer) => transfer,
        AwaitingPin(var transfer, _) => transfer,
        Finished(var transfer, _) => transfer,
    };

    public LocalSendIdentity? Sender => Transfer.Sender;

    public LocalSendDevice Receiver => Transfer.Receiver;

    public string ContentSummary => Transfer.ContentSummary;

    public TransferState State => Transfer.State;

    public long BytesTransferred => Transfer.BytesTransferred;

    public long TotalBytes => Transfer.TotalBytes;

    public string Status => Transfer.Status;

    public Action Cancel => Transfer.Cancel;

    public bool IsPending => this is Pending;

    public bool IsError => this is Finished { IsError: true };

    public OutgoingPinPrompt? PinPrompt => this is AwaitingPin(_, var prompt) ? prompt : null;
}

sealed record OutgoingPinPrompt(
    string? Error,
    Action<string> Submit,
    Action Cancel);

sealed record ShareTargetPayload(
    Guid Id,
    IReadOnlyList<ShareTargetItem> Items,
    string? SuggestedContactFingerprint = null);

union ShareTargetItem(ShareTargetItem.FileSystem, ShareTargetItem.Text)
{
    public sealed record FileSystem(string Path, bool IsDirectory);

    public sealed record Text(string Value, string FileName);
}

sealed record ReceiveHistoryEntry(
    Guid Id,
    string FileName,
    string Path,
    long Size,
    string SenderAlias,
    DateTimeOffset ReceivedAt);
