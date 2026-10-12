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
    private readonly object _devicePublication = new();
    private IReadOnlyDictionary<string, LocalSendDevice> _devices = new Dictionary<string, LocalSendDevice>(StringComparer.Ordinal);
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
                    TonarinkDeviceKind.Desktop => LocalSendDeviceType.Desktop,
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
            ReplaceDevices([]);
            _offers.Clear();
            _state.ReplaceIncomingOffers([]);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => RequiredNode().RefreshAsync(cancellationToken);

    public async Task<RuntimeSendResult> SendAsync(NearbyDevice device, IReadOnlyList<ShareItem> items, string? pin = null, IProgress<TransferActivity>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Volatile.Read(ref _devices).TryGetValue(device.Id, out var target))
            throw new InvalidOperationException(Text("DeviceOffline"));
        return await SendToDeviceAsync(target, items, pin, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RuntimeSendResult> SendToDeviceAsync(LocalSendDevice target, IReadOnlyList<ShareItem> items, string? pin, IProgress<TransferActivity>? progress, CancellationToken cancellationToken)
    {
        var coreItems = items.Select(ToCoreItem).ToArray();
        var reporter = new Progress<TransferProgress>(value =>
        {
            var activity = MapProgress(value, target.Alias, Text("ItemSummary", items.Count), items.Count);
            _state.UpsertTransfer(activity);
            progress?.Report(activity);
        });
        var result = await RequiredNode().SendAsync(
                target,
                coreItems,
                new SendOptions { Pin = pin, ComputeSha256 = true },
                reporter,
                cancellationToken)
            .ConfigureAwait(false);
        if (result is SendOutcome.PinRequired required)
            return new RuntimeSendResult.PinRequired(required.InvalidPin);
        if (result is SendOutcome.PinRateLimited)
            return new RuntimeSendResult.PinRateLimited();

        var (transferId, status, bytesTransferred, runtimeResult) = result switch
        {
            SendOutcome.Completed completed => (
                completed.TransferId,
                AppTransferStatus.Completed,
                completed.Items.Sum(static item => item.BytesTransferred),
                (RuntimeSendResult)new RuntimeSendResult.Completed()),
            SendOutcome.Cancelled cancelled => (
                cancelled.TransferId,
                AppTransferStatus.Cancelled,
                cancelled.Items.Sum(static item => item.BytesTransferred),
                new RuntimeSendResult.Cancelled()),
            SendOutcome.PeerBusy busy => (
                busy.TransferId,
                AppTransferStatus.Failed,
                0,
                new RuntimeSendResult.PeerBusy()),
            SendOutcome.Declined declined => (
                declined.TransferId,
                AppTransferStatus.Failed,
                0,
                new RuntimeSendResult.Declined()),
            SendOutcome.Failed failed => (
                failed.TransferId,
                AppTransferStatus.Failed,
                failed.Items.Sum(static item => item.BytesTransferred),
                new RuntimeSendResult.Failed(failed.Failure.Message)),
            SendOutcome.PinRequired or SendOutcome.PinRateLimited =>
                throw new InvalidOperationException("PIN outcomes must be returned before recording the final transfer."),
        };
        _state.UpsertTransfer(new TransferActivity(
            transferId.ToString("N"),
            AppTransferDirection.Send,
            target.Alias,
            Text("ItemSummary", items.Count),
            status,
            bytesTransferred,
            items.Sum(static item => item.Size),
            DateTimeOffset.UtcNow) { ItemCount = items.Count });
        if (runtimeResult is RuntimeSendResult.Completed)
            await _platform.NotifyAsync(Text("SendCompleteTitle"), Text("SendCompleteBody", target.Alias), cancellationToken).ConfigureAwait(false);
        return runtimeResult;
    }

    public async Task<RuntimeSendResult> SendToAddressAsync(string address, IReadOnlyList<ShareItem> items, string? pin = null, IProgress<TransferActivity>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!DeviceAddress.TryParse(address, out var ip, out var port))
            throw new ArgumentException(Text("InvalidAddress"), nameof(address));

        Exception? lastError = null;
        foreach (var protocol in new[] { LocalSendProtocol.Https, LocalSendProtocol.Http })
        {
            try
            {
                var endpoint = new DeviceEndpoint(ip, port, protocol);
                var probe = await RequiredNode().ProbeDeviceAsync(endpoint, cancellationToken).ConfigureAwait(false);
                var device = await RequiredNode().AddKnownDeviceAsync(endpoint, probe.Device.Fingerprint, cancellationToken).ConfigureAwait(false);
                ReplaceDevices(RequiredNode().GetDevices());
                // The probe already resolved this target. A concurrent device
                // refresh must not make an address send re-resolve its cache.
                return await SendToDeviceAsync(device, items, pin, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Address send attempt using {Protocol} failed", protocol);
                lastError = exception;
            }
        }

        throw lastError ?? new InvalidOperationException(Text("CannotConnect"));
    }

    public async Task<RuntimeReceiveResult> AcceptAsync(IncomingOffer offer, IReadOnlySet<Guid> acceptedItems, CancellationToken cancellationToken = default)
    {
        if (!_offers.TryGetValue(offer.Id, out var request))
            throw new InvalidOperationException(Text("RequestExpired"));
        var ids = request.Items.Where(item => acceptedItems.Contains(StableGuid(item.Id))).Select(static item => item.Id).ToArray();
        var reporter = new Progress<TransferProgress>(value => _state.UpsertTransfer(MapProgress(value, request.Sender.Alias, Text("ItemSummary", ids.Length), ids.Length)));
        var result = await RequiredNode().AcceptAsync(request.RequestId, new AcceptTransferOptions { AcceptedItemIds = ids }, reporter, cancellationToken).ConfigureAwait(false);
        var (transferId, status, bytesTransferred, runtimeResult, receivedItems) = result switch
        {
            ReceiveOutcome.Completed completed => (
                completed.TransferId,
                AppTransferStatus.Completed,
                completed.Items.Sum(static item => item.BytesTransferred),
                (RuntimeReceiveResult)new RuntimeReceiveResult.Completed(),
                completed.Items),
            ReceiveOutcome.Cancelled cancelled => (
                cancelled.TransferId,
                AppTransferStatus.Cancelled,
                cancelled.Items.Sum(static item => item.BytesTransferred),
                new RuntimeReceiveResult.Cancelled(),
                cancelled.Items),
            ReceiveOutcome.Failed failed => (
                failed.TransferId,
                AppTransferStatus.Failed,
                failed.Items.Sum(static item => item.BytesTransferred),
                new RuntimeReceiveResult.Failed(failed.Failure.Message),
                failed.Items),
        };
        _state.UpsertTransfer(new TransferActivity(
            transferId.ToString("N"),
            AppTransferDirection.Receive,
            request.Sender.Alias,
            Text("ItemSummary", ids.Length),
            status,
            bytesTransferred,
            request.Items.Where(item => ids.Contains(item.Id, StringComparer.Ordinal)).Sum(static item => item.Size),
            DateTimeOffset.UtcNow) { ItemCount = ids.Length });
        if (runtimeResult is RuntimeReceiveResult.Completed)
        {
            foreach (var item in receivedItems.Where(static item => item.SavedPath is not null))
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
            await _platform.NotifyAsync(Text("ReceiveCompleteTitle"), Text("ReceiveCompleteBody", request.Sender.Alias), cancellationToken).ConfigureAwait(false);
        }
        _offers.TryRemove(offer.Id, out _);
        PublishOffers();
        return runtimeResult;
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
                    await _platform.NotifyAsync(Text("IncomingTitle"), Text("IncomingBody", request.Sender.Alias, request.Items.Count), cancellationToken).ConfigureAwait(false);
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

    internal void ReplaceDevices(IReadOnlyList<LocalSendDevice> devices)
    {
        var snapshot = new Dictionary<string, LocalSendDevice>(StringComparer.Ordinal);
        var nearby = new List<NearbyDevice>(devices.Count);
        foreach (var device in devices)
        {
            snapshot[device.Fingerprint] = device;
            var endpoint = device.PreferredEndpoint;
            nearby.Add(new NearbyDevice(device.Fingerprint, device.Alias, device.DeviceModel ?? device.DeviceType.ToString(),
                device.DeviceType.ToString(), endpoint?.Address.ToString() ?? "?", endpoint?.Port ?? 0, device.LastSeen)
            { IsModelFallback = device.DeviceModel is null });
        }

        // Never mutate a published dictionary. Readers retain the previous
        // complete snapshot while its replacement is being materialized.
        lock (_devicePublication)
        {
            Volatile.Write(ref _devices, snapshot);
            _state.ReplaceDevices(nearby);
        }
    }

    private void PublishOffers() => _state.ReplaceIncomingOffers(_offers.Values.Select(ToOffer));

    private static IncomingOffer ToOffer(IncomingTransferRequest request) => new(
        request.RequestId.ToString("N"), request.Sender.Alias, request.Sender.DeviceModel ?? request.Sender.DeviceType.ToString(),
        request.Items.Select(static item => new ShareItem(StableGuid(item.Id), item.FileName, item.Size, item.ContentType,
            TextContent: item.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? item.Preview : null)).ToArray(), request.ReceivedAt)
    { SenderDeviceType = request.Sender.DeviceType.ToString(), IsSenderModelFallback = request.Sender.DeviceModel is null };

    private async Task AutoAcceptAsync(IncomingOffer offer, CancellationToken cancellationToken)
    {
        try
        {
            var result = await AcceptAsync(offer, offer.Items.Select(static item => item.Id).ToHashSet(), cancellationToken).ConfigureAwait(false);
            var failure = result switch
            {
                RuntimeReceiveResult.Completed or RuntimeReceiveResult.Cancelled => null,
                RuntimeReceiveResult.Failed failed => failed.Message,
            };
            if (failure is not null)
            {
                _logger.LogError("Automatically accepting transfer {TransferId} failed: {Reason}", offer.Id, failure);
                await _platform.NotifyAsync(Text("AutoReceiveFailed"), failure, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Automatic acceptance was cancelled with the runtime session.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Automatically accepting transfer {TransferId} failed", offer.Id);
            await _platform.NotifyAsync(Text("AutoReceiveFailed"), exception.GetBaseException().Message, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static Guid StableGuid(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static TransferActivity MapProgress(TransferProgress value, string peer, string summary, int itemCount) => new(
        value.TransferId.ToString("N"), value.Direction == CoreTransferDirection.Send ? AppTransferDirection.Send : AppTransferDirection.Receive,
        peer, summary, MapStatus(value.State), value.BytesTransferred, value.TotalBytes, DateTimeOffset.UtcNow)
    { ItemCount = itemCount };

    private static AppTransferStatus MapStatus(CoreTransferState state) => state switch
    {
        CoreTransferState.Completed => AppTransferStatus.Completed,
        CoreTransferState.Cancelled => AppTransferStatus.Cancelled,
        CoreTransferState.Failed => AppTransferStatus.Failed,
        CoreTransferState.Transferring => AppTransferStatus.Transferring,
        CoreTransferState.Preparing or CoreTransferState.WaitingForAcceptance => AppTransferStatus.Waiting,
    };

    private string Text(string key, params object?[] arguments) => AppText.Get(key, _state.Settings.Language, arguments);

    private LocalSendNode RequiredNode() => _node ?? throw new InvalidOperationException(Text("NodeNotStarted"));
}
