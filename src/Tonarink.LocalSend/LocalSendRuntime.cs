using System.Collections.Concurrent;
using LocalSendDotNet;
using Microsoft.Extensions.Logging;
using Tonarink.Application;
using AppTransferDirection = Tonarink.Application.TransferDirection;
using AppTransferStatus = Tonarink.Application.TransferStatus;
using CoreTransferDirection = LocalSendDotNet.TransferDirection;
using CoreTransferState = LocalSendDotNet.TransferState;

namespace Tonarink.LocalSend;

public sealed class LocalSendRuntime : ITonarinkRuntime
{
    private readonly TonarinkAppState _state;
    private readonly IPlatformServices _platform;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LocalSendRuntime> _logger;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly ConcurrentDictionary<string, LocalSendDevice> _devices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IncomingTransferRequest> _offers = new(StringComparer.Ordinal);
    private CancellationTokenSource? _lifetime;
    private LocalSendNode? _node;
    private Task[] _watchers = [];
    private bool _disposed;

    public LocalSendRuntime(TonarinkAppState state, IPlatformServices platform, ILoggerFactory loggerFactory)
    {
        _state = state;
        _platform = platform;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<LocalSendRuntime>();
    }

    public bool IsRunning => _node?.State == LocalSendNodeState.Running;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_node is not null)
                return;

            Directory.CreateDirectory(_platform.DataDirectory);
            Directory.CreateDirectory(_platform.DownloadDirectory);
            var settings = _state.Settings;
            var node = new LocalSendNode(new LocalSendOptions
            {
                Alias = settings.Alias,
                DeviceModel = _platform.DeviceModel,
                DeviceType = _platform.DeviceKind switch
                {
                    TonarinkDeviceKind.Mobile => LocalSendDeviceType.Mobile,
                    TonarinkDeviceKind.Web => LocalSendDeviceType.Web,
                    _ => LocalSendDeviceType.Desktop,
                },
                DataDirectory = Path.Combine(_platform.DataDirectory, "identity"),
                DownloadDirectory = settings.DownloadDirectory,
                Port = settings.Port,
                EnableHttps = true,
                ReceivePin = settings.ResolvedReceivePin,
            }, _loggerFactory);
            var lifetime = new CancellationTokenSource();
            Task[] watchers = [];
            try
            {
                await node.StartAsync(cancellationToken).ConfigureAwait(false);
                ReplaceDevices(node.GetDevices());
                var incomingReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                watchers = [WatchDevicesAsync(node, lifetime.Token), WatchIncomingAsync(node, lifetime.Token, incomingReady)];
                await incomingReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await lifetime.CancelAsync().ConfigureAwait(false);
                try { await Task.WhenAll(watchers).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                await node.DisposeAsync().ConfigureAwait(false);
                lifetime.Dispose();
                throw;
            }

            _node = node;
            _lifetime = lifetime;
            _watchers = watchers;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var node = _node;
            var lifetime = _lifetime;
            _node = null;
            _lifetime = null;
            if (node is null)
                return;

            if (lifetime is not null)
                await lifetime.CancelAsync().ConfigureAwait(false);
            await node.StopAsync(cancellationToken).ConfigureAwait(false);
            try { await Task.WhenAll(_watchers).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                // Watchers are cancelled before the node is disposed.
            }
            await node.DisposeAsync().ConfigureAwait(false);
            lifetime?.Dispose();
            _watchers = [];
            _devices.Clear();
            _offers.Clear();
            _state.ReplaceDevices([]);
            _state.ReplaceIncomingOffers([]);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => RequiredNode().RefreshAsync(cancellationToken);

    public async Task SendAsync(NearbyDevice device, IReadOnlyList<ShareItem> items, string? pin = null, IProgress<TransferActivity>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!_devices.TryGetValue(device.Id, out var target))
            throw new InvalidOperationException("目标设备已离线，请刷新后重试。");
        var coreItems = items.Select(ToCoreItem).ToArray();
        var reporter = new Progress<TransferProgress>(value =>
        {
            var activity = MapProgress(value, target.Alias, $"{items.Count} 项");
            _state.UpsertTransfer(activity);
            progress?.Report(activity);
        });
        TransferResult result;
        try
        {
            result = await RequiredNode().SendAsync(target, coreItems, new SendOptions { Pin = pin, ComputeSha256 = true }, reporter, cancellationToken).ConfigureAwait(false);
        }
        catch (PinRequiredException exception)
        {
            throw new TransferPinRequiredException(exception.InvalidPin, exception);
        }
        catch (PinRateLimitedException exception)
        {
            throw new TransferPinRateLimitedException(exception);
        }
        _state.UpsertTransfer(new TransferActivity(result.TransferId.ToString("N"), AppTransferDirection.Send, target.Alias,
            $"{items.Count} 项", MapStatus(result.State), result.Items.Sum(static item => item.BytesTransferred), items.Sum(static item => item.Size), DateTimeOffset.UtcNow));
        if (result.IsSuccess)
            await _platform.NotifyAsync("发送完成", $"已成功发送给 {target.Alias}", cancellationToken).ConfigureAwait(false);
    }

    public async Task SendToAddressAsync(string address, IReadOnlyList<ShareItem> items, string? pin = null, IProgress<TransferActivity>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!DeviceAddress.TryParse(address, out var ip, out var port))
            throw new ArgumentException("目标地址无效，请使用 IP 或 IP:端口。", nameof(address));

        Exception? lastError = null;
        foreach (var protocol in new[] { LocalSendProtocol.Https, LocalSendProtocol.Http })
        {
            try
            {
                var endpoint = new DeviceEndpoint(ip, port, protocol);
                var probe = await RequiredNode().ProbeDeviceAsync(endpoint, cancellationToken).ConfigureAwait(false);
                var device = await RequiredNode().AddKnownDeviceAsync(endpoint, probe.Device.Fingerprint, cancellationToken).ConfigureAwait(false);
                _devices[device.Fingerprint] = device;
                ReplaceDevices(RequiredNode().GetDevices());
                var nearby = new NearbyDevice(device.Fingerprint, device.Alias, device.DeviceModel ?? device.DeviceType.ToString(),
                    device.DeviceType.ToString(), endpoint.Address.ToString(), endpoint.Port, device.LastSeen);
                await SendAsync(nearby, items, pin, progress, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (TransferPinRequiredException)
            {
                throw;
            }
            catch (TransferPinRateLimitedException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        throw lastError ?? new InvalidOperationException("无法连接到该地址。");
    }

    public async Task AcceptAsync(IncomingOffer offer, IReadOnlySet<Guid> acceptedItems, CancellationToken cancellationToken = default)
    {
        if (!_offers.TryGetValue(offer.Id, out var request))
            throw new InvalidOperationException("接收请求已失效。");
        var ids = request.Items.Where(item => acceptedItems.Contains(StableGuid(item.Id))).Select(static item => item.Id).ToArray();
        var reporter = new Progress<TransferProgress>(value => _state.UpsertTransfer(MapProgress(value, request.Sender.Alias, $"{ids.Length} 项")));
        var result = await RequiredNode().AcceptAsync(request.RequestId, new AcceptTransferOptions { AcceptedItemIds = ids }, reporter, cancellationToken).ConfigureAwait(false);
        _state.UpsertTransfer(new TransferActivity(result.TransferId.ToString("N"), AppTransferDirection.Receive, request.Sender.Alias,
            $"{ids.Length} 项", MapStatus(result.State), result.Items.Sum(static item => item.BytesTransferred),
            request.Items.Where(item => ids.Contains(item.Id, StringComparer.Ordinal)).Sum(static item => item.Size), DateTimeOffset.UtcNow));
        if (result.IsSuccess)
        {
            foreach (var item in result.Items.Where(static item => item.SavedPath is not null))
            {
                var contentType = request.Items.FirstOrDefault(candidate => candidate.Id == item.ItemId)?.ContentType ?? "application/octet-stream";
                try
                {
                    await _platform.PublishReceivedFileAsync(item.SavedPath!, contentType, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Publishing received file {FileName} to platform storage failed", item.FileName);
                }
            }
            await _platform.NotifyAsync("接收完成", $"已接收来自 {request.Sender.Alias} 的内容", cancellationToken).ConfigureAwait(false);
        }
        _offers.TryRemove(offer.Id, out _);
        PublishOffers();
    }

    public async Task DeclineAsync(IncomingOffer offer, CancellationToken cancellationToken = default)
    {
        if (_offers.TryRemove(offer.Id, out var request))
            await RequiredNode().DeclineAsync(request.RequestId, cancellationToken).ConfigureAwait(false);
        PublishOffers();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private SendItem ToCoreItem(ShareItem item) => item.TextContent is { } text
        ? new SendTextItem(text, item.Name)
        : new SendStreamItem(item.Name, item.Size,
            item.OpenRead ?? (token => _platform.OpenReadAsync(item, token)), item.ContentType);

    private async Task WatchDevicesAsync(LocalSendNode node, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in node.WatchDeviceChangesAsync(cancellationToken).ConfigureAwait(false))
                ReplaceDevices(node.GetDevices());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Device watching ended with the runtime session.
        }
    }

    private async Task WatchIncomingAsync(LocalSendNode node, CancellationToken cancellationToken, TaskCompletionSource ready)
    {
        try
        {
            var enumerator = node.WatchIncomingTransfersAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            await using (enumerator.ConfigureAwait(false))
            {
                var pending = enumerator.MoveNextAsync();
                ready.TrySetResult();
                while (await pending.ConfigureAwait(false))
                {
                    var request = enumerator.Current;
                    _offers[request.RequestId.ToString("N")] = request;
                    PublishOffers();
                    await _platform.NotifyAsync("收到发送请求", $"{request.Sender.Alias} 想要发送 {request.Items.Count} 项内容", cancellationToken).ConfigureAwait(false);
                    if (_state.Settings.AutoAccept)
                        _ = AutoAcceptAsync(ToOffer(request), cancellationToken);
                    pending = enumerator.MoveNextAsync();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Incoming transfer watching ended with the runtime session.
        }
        catch (Exception exception)
        {
            ready.TrySetException(exception);
            throw;
        }
        finally
        {
            ready.TrySetCanceled(cancellationToken);
        }
    }

    private void ReplaceDevices(IReadOnlyList<LocalSendDevice> devices)
    {
        _devices.Clear();
        foreach (var device in devices)
            _devices[device.Fingerprint] = device;
        _state.ReplaceDevices(devices.Select(static device =>
        {
            var endpoint = device.PreferredEndpoint;
            return new NearbyDevice(device.Fingerprint, device.Alias, device.DeviceModel ?? device.DeviceType.ToString(),
                device.DeviceType.ToString(), endpoint?.Address.ToString() ?? "?", endpoint?.Port ?? 0, device.LastSeen);
        }));
    }

    private void PublishOffers() => _state.ReplaceIncomingOffers(_offers.Values.Select(ToOffer));

    private static IncomingOffer ToOffer(IncomingTransferRequest request) => new(
        request.RequestId.ToString("N"), request.Sender.Alias, request.Sender.DeviceModel ?? request.Sender.DeviceType.ToString(),
        request.Items.Select(static item => new ShareItem(StableGuid(item.Id), item.FileName, item.Size, item.ContentType,
            TextContent: item.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? item.Preview : null)).ToArray(), request.ReceivedAt);

    private async Task AutoAcceptAsync(IncomingOffer offer, CancellationToken cancellationToken)
    {
        try
        {
            await AcceptAsync(offer, offer.Items.Select(static item => item.Id).ToHashSet(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Automatic acceptance was cancelled with the runtime session.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Automatically accepting transfer {TransferId} failed", offer.Id);
            await _platform.NotifyAsync("自动接收失败", exception.GetBaseException().Message, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static Guid StableGuid(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static TransferActivity MapProgress(TransferProgress value, string peer, string summary) => new(
        value.TransferId.ToString("N"), value.Direction == CoreTransferDirection.Send ? AppTransferDirection.Send : AppTransferDirection.Receive,
        peer, summary, MapStatus(value.State), value.BytesTransferred, value.TotalBytes, DateTimeOffset.UtcNow);

    private static AppTransferStatus MapStatus(CoreTransferState state) => state switch
    {
        CoreTransferState.Completed => AppTransferStatus.Completed,
        CoreTransferState.Cancelled => AppTransferStatus.Cancelled,
        CoreTransferState.Failed => AppTransferStatus.Failed,
        CoreTransferState.Transferring => AppTransferStatus.Transferring,
        _ => AppTransferStatus.Waiting,
    };

    private LocalSendNode RequiredNode() => _node ?? throw new InvalidOperationException("Tonarink 节点尚未启动。");
}
