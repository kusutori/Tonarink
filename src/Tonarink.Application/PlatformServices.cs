namespace Tonarink.Application;

public interface IPlatformServices
{
    PlatformCapabilities Capabilities { get; }

    string DataDirectory { get; }

    string DownloadDirectory { get; }

    string DefaultAlias { get; }

    string DeviceModel { get; }

    TonarinkDeviceKind DeviceKind { get; }

    Task<TonarinkSettings?> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(TonarinkSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShareItem>> PickFilesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShareItem>> PickFoldersAsync(CancellationToken cancellationToken = default);

    Task<ShareItem> ImportSharedFileAsync(string fileName, Stream source, string contentType, CancellationToken cancellationToken = default);

    void ReleaseShareItem(ShareItem item);

    void ReleaseUnreferencedShareItems(IReadOnlyList<ShareItem> stillHeld);

    ValueTask<Stream> OpenReadAsync(ShareItem item, CancellationToken cancellationToken = default);

    Task<string?> ReadClipboardTextAsync(CancellationToken cancellationToken = default);

    Task WriteClipboardTextAsync(string text, CancellationToken cancellationToken = default);

    Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default);

    Task PublishReceivedFileAsync(string path, string contentType, CancellationToken cancellationToken = default);
}

public interface ITonarinkRuntime : IAsyncDisposable
{
    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<RuntimeSendResult> SendAsync(NearbyDevice device, IReadOnlyList<ShareItem> items, string? pin = null, IProgress<TransferActivity>? progress = null, CancellationToken cancellationToken = default);

    Task<RuntimeSendResult> SendToAddressAsync(string address, IReadOnlyList<ShareItem> items, string? pin = null, IProgress<TransferActivity>? progress = null, CancellationToken cancellationToken = default);

    Task<RuntimeReceiveResult> AcceptAsync(IncomingOffer offer, IReadOnlySet<Guid> acceptedItems, CancellationToken cancellationToken = default);

    Task DeclineAsync(IncomingOffer offer, CancellationToken cancellationToken = default);
}

public sealed class CapabilityOnlyRuntime : ITonarinkRuntime
{
    private readonly PlatformCapabilities _capabilities;

    public CapabilityOnlyRuntime(PlatformCapabilities capabilities) => _capabilities = capabilities;

    public bool IsRunning => false;

    public Task StartAsync(CancellationToken cancellationToken = default) => Unsupported();

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Unsupported();

    public Task<RuntimeSendResult> SendAsync(NearbyDevice device, IReadOnlyList<ShareItem> items, string? pin = null, IProgress<TransferActivity>? progress = null, CancellationToken cancellationToken = default) => Unsupported<RuntimeSendResult>();

    public Task<RuntimeSendResult> SendToAddressAsync(string address, IReadOnlyList<ShareItem> items, string? pin = null, IProgress<TransferActivity>? progress = null, CancellationToken cancellationToken = default) => Unsupported<RuntimeSendResult>();

    public Task<RuntimeReceiveResult> AcceptAsync(IncomingOffer offer, IReadOnlySet<Guid> acceptedItems, CancellationToken cancellationToken = default) => Unsupported<RuntimeReceiveResult>();

    public Task DeclineAsync(IncomingOffer offer, CancellationToken cancellationToken = default) => Unsupported();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task Unsupported() => Task.FromException(new PlatformNotSupportedException(
        $"{_capabilities.PlatformName} 当前不能直接运行完整 LocalSend 节点。"));

    private Task<T> Unsupported<T>() => Task.FromException<T>(new PlatformNotSupportedException(
        $"{_capabilities.PlatformName} 当前不能直接运行完整 LocalSend 节点。"));
}
