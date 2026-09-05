using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalSendDotNet;

static class AppSettingsStore
{
    private static readonly string FilePath = Path.Combine(AppPlatform.DataDirectory, "settings.json");
    private static volatile AppSettings? _cached;

    public static event Action? Changed;

    public static AppSettings Load()
    {
        if (_cached is { } cached)
            return cached;

        try
        {
            if (!File.Exists(FilePath))
                return _cached = AppSettings.Default;

            var json = File.ReadAllText(FilePath);
            var file = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettingsFile);
            return _cached = file?.ToSettings() ?? AppSettings.Default;
        }
        catch
        {
            return _cached = AppSettings.Default;
        }
    }

    public static void Save(AppSettings settings)
    {
        var previous = _cached;
        _cached = settings;
        Directory.CreateDirectory(AppPlatform.DataDirectory);
        var json = JsonSerializer.Serialize(AppSettingsFile.FromSettings(settings), AppSettingsJsonContext.Default.AppSettingsFile);
        File.WriteAllText(FilePath, json);
        if (previous != settings)
            Changed?.Invoke();
    }
}

sealed class AppSettingsFile
{
    public string? Alias { get; set; }
    public string? AutoSave { get; set; }
    public int? ThemeIndex { get; set; }
    public int? LanguageIndex { get; set; }
    public string? Language { get; set; }
    public bool? MinimizeToTray { get; set; }
    public bool? StartWithWindows { get; set; }
    public bool? NotificationsEnabled { get; set; }
    public string? NotificationDefaultAction { get; set; }
    public bool? FavoritesOnly { get; set; }
    public string? DownloadDirectory { get; set; }
    public string? DeviceType { get; set; }
    public string? DeviceModel { get; set; }
    public int? Port { get; set; }
    public int? DiscoveryTimeoutMs { get; set; }
    public bool? EnableHttps { get; set; }
    public string? MulticastGroup { get; set; }
    public string[]? NetworkWhitelist { get; set; }
    public string[]? NetworkBlacklist { get; set; }
    public bool? ShowExplorerContextMenu { get; set; }

    public static AppSettingsFile FromSettings(AppSettings settings) => new()
    {
        Alias = settings.Alias,
        AutoSave = settings.AutoSave.ToString(),
        ThemeIndex = settings.ThemeIndex,
        LanguageIndex = settings.LanguageIndex,
        Language = settings.LanguageIndex switch
        {
            1 => "zh-CN",
            2 => "en-US",
            _ => null,
        },
        MinimizeToTray = settings.MinimizeToTray,
        StartWithWindows = settings.StartWithWindows,
        NotificationsEnabled = settings.NotificationsEnabled,
        NotificationDefaultAction = settings.NotificationDefaultAction.ToString(),
        FavoritesOnly = settings.FavoritesOnly,
        DownloadDirectory = settings.DownloadDirectory,
        DeviceType = settings.DeviceType.ToString(),
        DeviceModel = settings.DeviceModel,
        Port = settings.Port,
        DiscoveryTimeoutMs = settings.DiscoveryTimeoutMs,
        EnableHttps = settings.EnableHttps,
        MulticastGroup = settings.MulticastGroup,
        NetworkWhitelist = Copy(settings.NetworkWhitelist),
        NetworkBlacklist = Copy(settings.NetworkBlacklist),
        ShowExplorerContextMenu = settings.ShowExplorerContextMenu,
    };

    public AppSettings ToSettings()
    {
        var defaults = AppSettings.Default;
        var autoSave = Enum.TryParse<AutoSaveMode>(AutoSave, ignoreCase: true, out var parsedAutoSave)
            ? parsedAutoSave
            : defaults.AutoSave;
        if (autoSave == AutoSaveMode.On && FavoritesOnly == true)
            autoSave = AutoSaveMode.Favorites;

        return defaults with
        {
            Alias = string.IsNullOrWhiteSpace(Alias) ? defaults.Alias : Alias.Trim(),
            AutoSave = autoSave,
            ThemeIndex = ThemeIndex is >= 0 and <= 2 ? ThemeIndex.Value : defaults.ThemeIndex,
            LanguageIndex = LanguageIndex is >= 0 and <= 2 ? LanguageIndex.Value : defaults.LanguageIndex,
            MinimizeToTray = MinimizeToTray ?? defaults.MinimizeToTray,
            StartWithWindows = StartWithWindows ?? defaults.StartWithWindows,
            NotificationsEnabled = NotificationsEnabled ?? defaults.NotificationsEnabled,
            NotificationDefaultAction = Enum.TryParse<NotificationDefaultAction>(
                NotificationDefaultAction,
                ignoreCase: true,
                out var notificationDefaultAction)
                    ? notificationDefaultAction
                    : defaults.NotificationDefaultAction,
            FavoritesOnly = autoSave == AutoSaveMode.Favorites,
            DownloadDirectory = string.IsNullOrWhiteSpace(DownloadDirectory)
                ? defaults.DownloadDirectory
                : DownloadDirectory,
            DeviceType = Enum.TryParse<LocalSendDeviceType>(DeviceType, ignoreCase: true, out var deviceType)
                ? deviceType
                : defaults.DeviceType,
            DeviceModel = DeviceModel ?? defaults.DeviceModel,
            Port = Port is >= 1 and <= ushort.MaxValue ? Port.Value : defaults.Port,
            DiscoveryTimeoutMs = DiscoveryTimeoutMs is > 0 ? DiscoveryTimeoutMs.Value : defaults.DiscoveryTimeoutMs,
            EnableHttps = EnableHttps ?? defaults.EnableHttps,
            MulticastGroup = IsMulticastGroup(MulticastGroup) ? MulticastGroup! : defaults.MulticastGroup,
            NetworkWhitelist = Copy(NetworkWhitelist),
            NetworkBlacklist = NetworkWhitelist is null ? Copy(NetworkBlacklist) : null,
            ShowExplorerContextMenu = ShowExplorerContextMenu ?? defaults.ShowExplorerContextMenu,
        };
    }

    private static string[]? Copy(IReadOnlyList<string>? values) =>
        values is null ? null : [.. values];

    private static bool IsMulticastGroup(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && IPAddress.TryParse(value, out var address)
        && address.AddressFamily == AddressFamily.InterNetwork;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettingsFile))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
