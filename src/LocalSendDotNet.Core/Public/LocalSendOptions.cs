using System.Net;

namespace LocalSendDotNet;

/// <summary>Configures one LocalSend node.</summary>
public sealed class LocalSendOptions
{
    /// <summary>The default LocalSend TCP and UDP port.</summary>
    public const int DefaultPort = 53317;
    /// <summary>The default LocalSend IPv4 multicast group.</summary>
    public static readonly IPAddress DefaultMulticastAddress = IPAddress.Parse("224.0.0.167");

    /// <summary>Gets the user-visible local alias.</summary>
    public required string Alias { get; init; }
    /// <summary>Gets the optional local device model.</summary>
    public string? DeviceModel { get; init; } = Environment.OSVersion.Platform.ToString();
    /// <summary>Gets the advertised local device type.</summary>
    public LocalSendDeviceType DeviceType { get; init; } = LocalSendDeviceType.Desktop;
    /// <summary>Gets the directory that stores the persistent certificate identity.</summary>
    public required string DataDirectory { get; init; }
    /// <summary>Gets the default root for accepted incoming files.</summary>
    public required string DownloadDirectory { get; init; }
    /// <summary>Gets the TCP server and UDP discovery port.</summary>
    public int Port { get; init; } = DefaultPort;
    /// <summary>Gets the IPv4 multicast group used by protocol v2 discovery.</summary>
    public IPAddress MulticastAddress { get; init; } = DefaultMulticastAddress;
    /// <summary>
    /// Gets optional IPv4 patterns that limit discovery to matching local interfaces.
    /// </summary>
    /// <remarks>
    /// When this list is not <see langword="null"/>, multicast, announcements, and HTTP subnet scans
    /// use only interfaces that have at least one matching address. A <c>*</c> octet is allowed.
    /// Takes precedence over <see cref="NetworkBlacklist"/>. Restart the node after changing this list.
    /// </remarks>
    public IReadOnlyList<string>? NetworkWhitelist { get; init; }
    /// <summary>
    /// Gets optional IPv4 patterns that exclude matching local interfaces from discovery.
    /// </summary>
    /// <remarks>
    /// When this list is not <see langword="null"/> and <see cref="NetworkWhitelist"/> is
    /// <see langword="null"/>, interfaces with any matching address are skipped. A <c>*</c> octet is allowed.
    /// Restart the node after changing this list.
    /// </remarks>
    public IReadOnlyList<string>? NetworkBlacklist { get; init; }
    /// <summary>Gets whether HTTPS and LocalSend mutual certificate identity are enabled.</summary>
    public bool EnableHttps { get; init; } = true;
    /// <summary>Gets the optional PIN required from incoming senders.</summary>
    public string? ReceivePin { get; init; }
    /// <summary>Gets the maximum number of active incoming transfer sessions.</summary>
    public int MaxConcurrentTransfers { get; init; } = 4;
    /// <summary>Gets the maximum number of simultaneously open incoming file bodies.</summary>
    public int MaxConcurrentFileUploads { get; init; } = 8;
    /// <summary>Gets how long one nearby-device HTTP probe may wait.</summary>
    public TimeSpan DiscoveryTimeout { get; init; } = TimeSpan.FromMilliseconds(500);
    /// <summary>Gets the timeout for ordinary HTTP requests.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets the total timeout for one file upload request.</summary>
    public TimeSpan UploadTimeout { get; init; } = TimeSpan.FromHours(12);
    /// <summary>Gets the timeout for best-effort remote cancellation notification.</summary>
    public TimeSpan CancelRequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets how long an incoming offer may wait for an application decision.</summary>
    public TimeSpan IncomingDecisionTimeout { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Gets how long an accepted session may remain unfinished.</summary>
    public TimeSpan IncomingTransferTimeout { get; init; } = TimeSpan.FromMinutes(30);
    /// <summary>Gets how long three consecutive PIN failures lock an IP address.</summary>
    public TimeSpan PinLockoutDuration { get; init; } = TimeSpan.FromMinutes(1);
    /// <summary>Gets the periodic announcement and network-maintenance interval.</summary>
    public TimeSpan AnnouncementInterval { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets how long an automatically discovered peer remains without refresh.</summary>
    public TimeSpan DeviceExpiration { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Gets the maximum number of items in one incoming offer.</summary>
    public int MaxIncomingItemsPerTransfer { get; init; } = 10_000;
    /// <summary>Gets the maximum aggregate declared bytes in one incoming offer.</summary>
    public long MaxIncomingTransferBytes { get; init; } = long.MaxValue;
    /// <summary>Gets the maximum JSON body size accepted by prepare-upload.</summary>
    public long MaxPrepareRequestBytes { get; init; } = 4 * 1024 * 1024;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(DataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(DownloadDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(Port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentTransfers, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentFileUploads, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxIncomingItemsPerTransfer, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxIncomingTransferBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPrepareRequestBytes);
        ValidateTimeout(DiscoveryTimeout, nameof(DiscoveryTimeout));
        ValidateTimeout(RequestTimeout, nameof(RequestTimeout));
        ValidateTimeout(UploadTimeout, nameof(UploadTimeout));
        ValidateTimeout(CancelRequestTimeout, nameof(CancelRequestTimeout));
        ValidateTimeout(IncomingDecisionTimeout, nameof(IncomingDecisionTimeout));
        ValidateTimeout(IncomingTransferTimeout, nameof(IncomingTransferTimeout));
        if (PinLockoutDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PinLockoutDuration));
        if (AnnouncementInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(AnnouncementInterval));
        if (DeviceExpiration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DeviceExpiration));
        if (DeviceExpiration <= AnnouncementInterval)
            throw new ArgumentException(CoreText.ExpirationGreater, nameof(DeviceExpiration));
        if (MulticastAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException(CoreText.MulticastIPv4, nameof(MulticastAddress));
        if (ReceivePin is { Length: 0 })
            throw new ArgumentException(CoreText.PinBlank, nameof(ReceivePin));
    }

    private static void ValidateTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(name, CoreText.TimeoutPositive);
    }
}
