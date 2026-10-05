using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using LocalSendDotNet;

namespace Tonarink.Cli;

sealed record StandaloneSettings
{
    public string Alias { get; init; } = Environment.MachineName;
    public string DownloadDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public int Port { get; init; } = LocalSendOptions.DefaultPort;
    public bool Encryption { get; init; } = true;
    public string Multicast { get; init; } = "224.0.0.167";
    public int DiscoveryTimeout { get; init; } = 500;
    public string DeviceModel { get; init; } = Environment.OSVersion.Platform.ToString();
    public LocalSendDeviceType DeviceType { get; init; } = LocalSendDeviceType.Headless;
    public string[]? NetworkAllowlist { get; init; }
    public string[]? NetworkBlocklist { get; init; }
    public bool SendChecksums { get; init; } = true;
    public bool ReceiveChecksums { get; init; } = true;
    public bool ReceiveHistory { get; init; } = true;
    public string AutoSave { get; init; } = "off";
    public bool PinEnabled { get; init; }
    public string ReceivePin { get; init; } = "";
    public string Language { get; init; } = "en-US";

    public LocalSendOptions NodeOptions(string profile) => new()
    {
        Alias = Alias, DownloadDirectory = DownloadDirectory, DataDirectory = profile,
        Port = Port, EnableHttps = Encryption, MulticastAddress = IPAddress.Parse(Multicast),
        DiscoveryTimeout = TimeSpan.FromMilliseconds(DiscoveryTimeout), DeviceModel = DeviceModel, DeviceType = DeviceType,
        ReceivePin = PinEnabled ? ReceivePin : null,
        NetworkWhitelist = NetworkAllowlist,
        NetworkBlacklist = NetworkBlocklist,
    };

    public CliSetting[] List(bool secrets) =>
    [
        new("alias", Alias), new("download-directory", DownloadDirectory), new("port", Port.ToString(CultureInfo.InvariantCulture)),
        Flag("encryption", Encryption), new("multicast", Multicast), new("discovery-timeout", DiscoveryTimeout.ToString(CultureInfo.InvariantCulture)),
        new("device-model", DeviceModel), new("device-type", DeviceType.ToString()),
        new("network-allowlist", string.Join(',', NetworkAllowlist ?? [])), new("network-blocklist", string.Join(',', NetworkBlocklist ?? [])),
        Flag("send-checksums", SendChecksums), Flag("receive-checksums", ReceiveChecksums), Flag("receive-history", ReceiveHistory),
        new("auto-save", AutoSave), Flag("pin-enabled", PinEnabled), new("receive-pin", secrets ? ReceivePin : "<redacted>"), new("language", Language),
    ];

    public StandaloneSettings Set(string key, string value, string directory)
    {
        var next = key.ToLowerInvariant() switch
        {
            "alias" => this with { Alias = string.IsNullOrWhiteSpace(value) ? throw new CliException("Alias cannot be empty.") : value.Trim() },
            "download-directory" => this with { DownloadDirectory = CliPath.FullPath(value, directory) },
            "port" => this with { Port = Number(value, 1, 65535) },
            "discovery-timeout" => this with { DiscoveryTimeout = Number(value, 1, 60000) },
            "encryption" => this with { Encryption = Boolean(value) },
            "multicast" => this with { Multicast = value.Trim() },
            "device-model" => this with { DeviceModel = value.Trim() },
            "device-type" => this with { DeviceType = Enum.TryParse<LocalSendDeviceType>(value, true, out var type) && Enum.IsDefined(type) ? type : throw new CliException("Invalid device type.") },
            "network-allowlist" => this with { NetworkAllowlist = Csv(value), NetworkBlocklist = null },
            "network-blocklist" => this with { NetworkBlocklist = Csv(value), NetworkAllowlist = null },
            "send-checksums" => this with { SendChecksums = Boolean(value) },
            "receive-checksums" => this with { ReceiveChecksums = Boolean(value) },
            "receive-history" => this with { ReceiveHistory = Boolean(value) },
            "pin-enabled" => this with { PinEnabled = Boolean(value) },
            "receive-pin" => this with { ReceivePin = value.Trim() },
            "auto-save" => this with { AutoSave = Choice(value, ["off", "favorites", "on"]) },
            "language" => this with { Language = Choice(value, ["en-US", "zh-CN"]) },
            _ => throw new CliException("Unsupported standalone setting: " + key + ". Run settings list for supported keys."),
        };
        next.Validate();
        return next;
    }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Alias)) throw new CliException("Alias cannot be empty.");
        if (Port is < 1 or > 65535 || DiscoveryTimeout is < 1 or > 60000) throw new CliException("Invalid port/discovery timeout.");
        if (!IPAddress.TryParse(Multicast, out var multicast) || multicast.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || multicast.GetAddressBytes()[0] is < 224 or > 239) throw new CliException("An IPv4 multicast address is required.");
        if (ReceivePin.Length > 32 || (PinEnabled && ReceivePin.Length == 0)) throw new CliException("PIN must be at most 32 characters and non-empty when enabled.");
        _ = Choice(AutoSave, ["off", "favorites", "on"]); _ = Choice(Language, ["en-US", "zh-CN"]);
        if (!Enum.IsDefined(DeviceType)) throw new CliException("Invalid device type.");
        if (!Path.IsPathFullyQualified(DownloadDirectory)) throw new CliException("Download directory must be absolute.");
    }
    private static CliSetting Flag(string key, bool value) => new(key, value ? "true" : "false");
    private static string[]? Csv(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static bool Boolean(string value) => value.ToLowerInvariant() switch
    { "true" or "on" or "1" => true, "false" or "off" or "0" => false, _ => throw new CliException("Expected true or false.") };
    private static int Number(string value, int min, int max) => int.TryParse(value, out var number) && number >= min && number <= max
        ? number : throw new CliException($"Expected an integer between {min} and {max}.");
    private static string Choice(string value, string[] choices) => choices.FirstOrDefault(v => v.Equals(value, StringComparison.OrdinalIgnoreCase))
        ?? throw new CliException("Expected one of: " + string.Join(", ", choices));
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(StandaloneSettings))]
sealed partial class StandaloneJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
