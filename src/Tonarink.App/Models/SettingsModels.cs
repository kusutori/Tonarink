using System.Net;
using System.Net.Sockets;
using LocalSendDotNet;

namespace Tonarink.Models;

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
    bool ExpandDragDropToEntireApp,
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
        ExpandDragDropToEntireApp: true,
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
