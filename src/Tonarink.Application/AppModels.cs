namespace Tonarink.Application;

public enum TonarinkTheme
{
    System,
    Light,
    Dark,
}

public enum TonarinkLanguage
{
    System,
    SimplifiedChinese,
    English,
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
    bool AutoAccept)
{
    public static TonarinkSettings CreateDefault(string downloadDirectory, string? alias = null) => new(
        string.IsNullOrWhiteSpace(alias) ? (string.IsNullOrWhiteSpace(Environment.UserName) ? "Tonarink" : Environment.UserName) : alias.Trim(),
        downloadDirectory,
        TonarinkTheme.System,
        TonarinkLanguage.System,
        AutoAccept: false);
}

public sealed record PlatformCapabilities(
    string PlatformName,
    bool IsNative,
    bool CanRunLocalSendNode,
    bool CanReceiveInBackground,
    bool CanPickFiles,
    bool CanUseClipboard,
    bool CanUseSystemShare,
    IReadOnlyList<string> Limitations)
{
    public static PlatformCapabilities Browser { get; } = new(
        "Web",
        IsNative: false,
        CanRunLocalSendNode: false,
        CanReceiveInBackground: false,
        CanPickFiles: true,
        CanUseClipboard: true,
        CanUseSystemShare: true,
        ["浏览器沙箱不能监听 LocalSend 的 UDP 多播和入站 TCP 端口。", "Web 版将连接到一个正在运行的 Tonarink 节点，或使用浏览器分享模式。"]);
}

public sealed record NearbyDevice(
    string Id,
    string Alias,
    string Model,
    string DeviceType,
    string Address,
    int Port,
    DateTimeOffset LastSeen);

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
    DateTimeOffset ReceivedAt);

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
    public double Progress => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)BytesTransferred / TotalBytes, 0, 1);
}

public sealed record IosShareFile(string Name, string Path, long Size, string ContentType);

public sealed record IosSharePayload(string? Text, IReadOnlyList<IosShareFile> Files);
