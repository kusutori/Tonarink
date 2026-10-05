using System.Globalization;
using LocalSendDotNet;
using Tonarink.Application;

namespace Tonarink.Cli;

static class CliSettings
{
    public static CliSetting[] List(AppSettings s, bool showSecrets) =>
    [
        new("alias", s.Alias), new("download-directory", s.DownloadDirectory),
        new("theme", new[] { "system", "light", "dark" }[s.ThemeIndex]),
        new("language", s.LanguageIndex == 0 ? "system" : AppLanguages.ToStoredCulture(s.LanguageIndex)!),
        new("auto-save", s.AutoSave.ToString().ToLowerInvariant()),
        Flag("minimize-to-tray", s.MinimizeToTray), new("tray-click", s.TrayClickOpensFlyout ? "panel" : "window"),
        Flag("start-with-windows", s.StartWithWindows), Flag("windows-share-favorites", s.ShowFavoriteDevicesInWindowsShare),
        Flag("notifications", s.NotificationsEnabled),
        new("notification-action", s.NotificationDefaultAction == NotificationDefaultAction.OpenFile ? "file" : "folder"),
        Flag("keep-send-items", s.KeepItemsForMultipleReceivers), Flag("send-checksums", s.VerifyChecksumsOnSend),
        Flag("receive-checksums", s.VerifyChecksumsOnReceive), Flag("app-wide-drop", s.ExpandDragDropToEntireApp),
        Flag("file-preview", s.FilePreviewEnabled), new("preview-tool", s.PreviewProvider.ToString()),
        new("preview-overrides", string.Join(',', FilePreviewSettings.Providers(s))),
        new("powertoys-path", s.PowerToysPeekExecutablePath), new("quicklook-path", s.QuickLookExecutablePath),
        Flag("receive-history", s.SaveReceiveHistory), Flag("pin-enabled", s.ReceivePinEnabled),
        new("receive-pin", showSecrets ? s.ReceivePin : "<redacted>"), new("device-type", s.DeviceType.ToString()),
        new("device-model", s.DeviceModel), new("port", s.Port.ToString(CultureInfo.InvariantCulture)),
        new("discovery-timeout", s.DiscoveryTimeoutMs.ToString(CultureInfo.InvariantCulture)), Flag("encryption", s.EnableHttps),
        new("multicast", s.MulticastGroup), new("network-allowlist", string.Join(',', s.NetworkWhitelist ?? [])),
        new("network-blocklist", string.Join(',', s.NetworkBlacklist ?? [])), Flag("explorer-menu", s.ShowExplorerContextMenu),
    ];

    public static AppSettings Set(AppSettings s, string key, string value, string workingDirectory)
    {
        key = key.ToLowerInvariant();
        var next = key switch
        {
            "alias" => s with { Alias = Required(value, key) },
            "download-directory" => s with { DownloadDirectory = FullPath(Required(value, key), workingDirectory) },
            "theme" => s with { ThemeIndex = Choice(value, ["system", "light", "dark"]) },
            "language" => s with { LanguageIndex = Choice(value, ["system", "zh-CN", "en-US"]) },
            "auto-save" => s with { AutoSave = EnumValue<AutoSaveMode>(value), FavoritesOnly = EnumValue<AutoSaveMode>(value) == AutoSaveMode.Favorites },
            "minimize-to-tray" => s with { MinimizeToTray = Boolean(value) },
            "tray-click" => s with { TrayClickOpensFlyout = Choice(value, ["panel", "window"]) == 0 },
            "start-with-windows" => s with { StartWithWindows = Boolean(value) },
            "windows-share-favorites" => s with { ShowFavoriteDevicesInWindowsShare = Boolean(value) },
            "notifications" => s with { NotificationsEnabled = Boolean(value) },
            "notification-action" => s with { NotificationDefaultAction = (NotificationDefaultAction)Choice(value, ["file", "folder"]) },
            "keep-send-items" => s with { KeepItemsForMultipleReceivers = Boolean(value) },
            "send-checksums" => s with { VerifyChecksumsOnSend = Boolean(value) },
            "receive-checksums" => s with { VerifyChecksumsOnReceive = Boolean(value) },
            "app-wide-drop" => s with { ExpandDragDropToEntireApp = Boolean(value) },
            "file-preview" => s with { FilePreviewEnabled = Boolean(value) },
            "preview-tool" => s with { PreviewProvider = EnumValue<FilePreviewProvider>(value) },
            "preview-overrides" => s with { PreviewProviders = Csv(value).Select(EnumValue<FilePreviewProvider>).Distinct().ToArray() },
            "powertoys-path" => Override(s, FilePreviewProvider.PowerToysPeek, value, workingDirectory),
            "quicklook-path" => Override(s, FilePreviewProvider.QuickLook, value, workingDirectory),
            "receive-history" => s with { SaveReceiveHistory = Boolean(value) },
            "pin-enabled" => s with { ReceivePinEnabled = Boolean(value) },
            "receive-pin" => s with { ReceivePin = value.Trim() },
            "device-type" => s with { DeviceType = EnumValue<LocalSendDeviceType>(value) },
            "device-model" => s with { DeviceModel = value.Trim() },
            "port" => s with { Port = Number(value, 1, 65535) },
            "discovery-timeout" => s with { DiscoveryTimeoutMs = Number(value, 1, 60000) },
            "encryption" => s with { EnableHttps = Boolean(value) },
            "multicast" => s with { MulticastGroup = Required(value, key) },
            "network-allowlist" => s with { NetworkWhitelist = string.IsNullOrWhiteSpace(value) ? null : Csv(value), NetworkBlacklist = null },
            "network-blocklist" => s with { NetworkBlacklist = string.IsNullOrWhiteSpace(value) ? null : Csv(value), NetworkWhitelist = null },
            "explorer-menu" => s with { ShowExplorerContextMenu = Boolean(value) },
            _ => throw new CliException(CliText.Get("Unknown settings key: {0}. Run tonarink settings list.", key)),
        };
        if (!SettingsInputDraft.FromSettings(next).TryValidate(next.ReceivePinEnabled, out var error))
            throw new CliException(error switch
            {
                "SettingsMulticastInvalid" => CliText.Get("Multicast must be an IPv4 address between 224.0.0.0 and 239.255.255.255."),
                "SettingsReceivePinInvalid" => CliText.Get("PIN must be at most 32 characters and non-empty when enabled."),
                _ => CliText.Get("Invalid server settings: {0}", error),
            });
        return next;
    }

    private static AppSettings Override(AppSettings s, FilePreviewProvider provider, string value, string directory)
    {
        var path = value.Length == 0 ? "" : FullPath(FilePreviewSettings.NormalizePath(value), directory);
        if (path.Length > 0 && !FilePreviewSettings.PathExists(path)) throw new CliException(CliText.Get("The override file does not exist."));
        s = FilePreviewSettings.Add(s, provider);
        return FilePreviewSettings.SetExecutablePath(s, provider, path);
    }

    public static string FullPath(string value, string directory) => CliPath.FullPath(value, directory);
    private static CliSetting Flag(string key, bool value) => new(key, value ? "true" : "false");
    private static string[] Csv(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static string Required(string value, string key) => !string.IsNullOrWhiteSpace(value)
        ? value.Trim() : throw new CliException(CliText.Get("{0} cannot be empty.", key));
    private static bool Boolean(string value) => value.ToLowerInvariant() switch
    {
        "true" or "on" or "1" => true,
        "false" or "off" or "0" => false,
        _ => throw new CliException(CliText.Get("Expected true or false.")),
    };
    private static int Choice(string value, string[] choices)
    {
        var index = Array.FindIndex(choices, item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : throw new CliException(CliText.Get("Expected one of: {0}", string.Join(", ", choices)));
    }
    private static int Number(string value, int min, int max) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max
            ? number : throw new CliException(CliText.Get("Expected an integer between {0} and {1}.", min, max));
    private static T EnumValue<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var result) && Enum.IsDefined(result)
            ? result : throw new CliException(CliText.Get("Invalid option value: {0}", value));
}
