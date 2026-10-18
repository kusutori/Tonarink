namespace Tonarink.Application;

public enum TonarinkTheme
{
    System,
    Light,
    Dark,
}

public enum TonarinkLanguage
{
    // Persisted as an index by the Windows app; never reorder these values.
    System = 0,
    SimplifiedChinese = 1,
    English = 2,
    Japanese = 3,
    Korean = 4,
    French = 5,
    Spanish = 6,
}

public enum TransferDirection
{
    Send,
    Receive,
}

public enum TonarinkDeviceKind
{
    Desktop,
    Mobile,
    Web,
}

public enum TransferStatus
{
    Waiting,
    Transferring,
    Completed,
    Cancelled,
    Failed,
}

public sealed record TonarinkSettings(
    string Alias,
    string DownloadDirectory,
    TonarinkTheme Theme,
    TonarinkLanguage Language,
    bool AutoAccept,
    string? ReceivePin = null,
    int Port = DeviceAddress.DefaultPort)
{
    public static TonarinkSettings CreateDefault(string downloadDirectory, string? alias = null) => new(
        string.IsNullOrWhiteSpace(alias) ? (string.IsNullOrWhiteSpace(Environment.UserName) ? "Tonarink" : Environment.UserName) : alias.Trim(),
        downloadDirectory,
        TonarinkTheme.System,
        TonarinkLanguage.System,
        AutoAccept: false);

    public string? ResolvedReceivePin => string.IsNullOrWhiteSpace(ReceivePin) ? null : ReceivePin.Trim();
}

public sealed record PlatformCapabilities(
    string PlatformName,
    bool IsNative,
    bool CanRunLocalSendNode,
    bool CanReceiveInBackground,
    bool CanPickFiles,
    bool CanPickFolders,
    bool CanUseClipboard,
    bool CanUseSystemShare,
    IReadOnlyList<string> Limitations)
{
    public static PlatformCapabilities Browser => new(
        "Web",
        IsNative: false,
        CanRunLocalSendNode: false,
        CanReceiveInBackground: false,
        CanPickFiles: true,
        CanPickFolders: false,
        CanUseClipboard: true,
        CanUseSystemShare: true,
        [AppText.Get("BrowserSocketLimit"), AppText.Get("BrowserNodeLimit")]);
}

public sealed record NearbyDevice(
    string Id,
    string Alias,
    string Model,
    string DeviceType,
    string Address,
    int Port,
    DateTimeOffset LastSeen)
{
    public bool IsModelFallback { get; init; }
}

public sealed record ShareItem(
    Guid Id,
    string Name,
    long Size,
    string ContentType,
    string? NativePath = null,
    string? TextContent = null,
    Func<CancellationToken, ValueTask<Stream>>? OpenRead = null);

public sealed record IncomingOffer(
    string Id,
    string SenderAlias,
    string SenderModel,
    IReadOnlyList<ShareItem> Items,
    DateTimeOffset ReceivedAt)
{
    public string? SenderDeviceType { get; init; }
    public bool IsSenderModelFallback { get; init; }
}

public sealed record TransferActivity(
    string Id,
    TransferDirection Direction,
    string PeerAlias,
    string Summary,
    TransferStatus Status,
    long BytesTransferred,
    long TotalBytes,
    DateTimeOffset UpdatedAt)
{
    // Keep count as data so a language change can update existing UI rows.
    // Old persisted entries may only contain Summary and remain readable.
    public int? ItemCount { get; init; }

    public double Progress => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)BytesTransferred / TotalBytes, 0, 1);
}

public union RuntimeSendResult(
    RuntimeSendResult.Completed,
    RuntimeSendResult.Cancelled,
    RuntimeSendResult.PinRequired,
    RuntimeSendResult.RateLimited,
    RuntimeSendResult.PeerBusy,
    RuntimeSendResult.Declined,
    RuntimeSendResult.Failed)
{
    public sealed record Completed;
    public sealed record Cancelled;
    public sealed record PinRequired(bool InvalidPin);
    public sealed record RateLimited;
    public sealed record PeerBusy;
    public sealed record Declined;
    public sealed record Failed(string Message);
}

public union RuntimeReceiveResult(
    RuntimeReceiveResult.Completed,
    RuntimeReceiveResult.Cancelled,
    RuntimeReceiveResult.Failed)
{
    public sealed record Completed;
    public sealed record Cancelled;
    public sealed record Failed(string Message);
}

public sealed record IosShareFile(string Name, string Path, long Size, string ContentType);

public sealed record IosSharePayload(string? Text, IReadOnlyList<IosShareFile> Files);
