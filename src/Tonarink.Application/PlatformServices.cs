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

    Task SendAsync(NearbyDevice device, IReadOnlyList<ShareItem> items, string? pin = null, IProgress<TransferActivity>? progress = null, CancellationToken cancellationToken = default);

    Task AcceptAsync(IncomingOffer offer, IReadOnlySet<Guid> acceptedItems, CancellationToken cancellationToken = default);

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

    public Task SendAsync(NearbyDevice device, IReadOnlyList<ShareItem> items, string? pin = null, IProgress<TransferActivity>? progress = null, CancellationToken cancellationToken = default) => Unsupported();

    public Task AcceptAsync(IncomingOffer offer, IReadOnlySet<Guid> acceptedItems, CancellationToken cancellationToken = default) => Unsupported();

    public Task DeclineAsync(IncomingOffer offer, CancellationToken cancellationToken = default) => Unsupported();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task Unsupported() => Task.FromException(new PlatformNotSupportedException(
        $"{_capabilities.PlatformName} 当前不能直接运行完整 LocalSend 节点。"));
}

public sealed class TransferPinRequiredException : Exception
{
    public TransferPinRequiredException(bool invalidPin, Exception? innerException = null)
        : base(invalidPin ? "The supplied PIN is incorrect." : "The receiving device requires a PIN.", innerException) => InvalidPin = invalidPin;

    public bool InvalidPin { get; }
}

public sealed class TransferPinRateLimitedException : Exception
{
    public TransferPinRateLimitedException(Exception? innerException = null)
        : base("Too many incorrect PIN attempts. Try again later.", innerException) { }
}
