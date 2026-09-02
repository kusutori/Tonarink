using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using LocalSendDotNet.Protocol;
using LocalSendDotNet.Protocol.V2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalSendDotNet;

/// <summary>A UI-independent LocalSend v2.2 peer.</summary>
public sealed class LocalSendNode : IAsyncDisposable
{
    private readonly LocalSendOptions _options;
    private readonly ILogger _logger;
    private readonly ILocalSendProtocolAdapter _protocol = new V2ProtocolAdapter();
    private readonly BroadcastHub<DeviceChange> _deviceChanges = new(128);
    private readonly BroadcastHub<IncomingTransferRequest> _incomingTransfers = new(64, dropOldest: false);
    private readonly BroadcastHub<LocalSendNodeStateChange> _stateChanges = new(32);
    private readonly ConcurrentDictionary<Guid, IncomingSession> _pending = new();
    private readonly ConcurrentDictionary<string, IncomingSession> _incomingSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (IPAddress Address, CancellationTokenSource Cancellation)> _outgoingSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeOutgoing = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _transferSlots;
    private readonly SemaphoreSlim _uploadSlots;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DeviceStore _devices;
    private DeviceIdentity? _identity;
    private V2HttpClient? _client;
    private V2Server? _server;
    private WebShareService? _webShare;
    private readonly BroadcastHub<WebShareState> _webShareChanges = new(32);
    private V2MulticastDiscovery? _discovery;
    private Task? _maintenance;
    private LocalSendNodeState _state = LocalSendNodeState.Created;
    private string? _discoveryError;
    private bool _started;
    private bool _stopped;
    private bool _disposed;

    /// <summary>Creates a LocalSend node. Call <see cref="StartAsync"/> before using network operations.</summary>
    /// <param name="options">Node identity, storage, transport, timeout, and limit settings.</param>
    /// <param name="loggerFactory">An optional structured logger factory.</param>
    public LocalSendNode(LocalSendOptions options, ILoggerFactory? loggerFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LocalSendNode>();
        _devices = new DeviceStore(_deviceChanges);
        _transferSlots = new SemaphoreSlim(options.MaxConcurrentTransfers, options.MaxConcurrentTransfers);
        _uploadSlots = new SemaphoreSlim(options.MaxConcurrentFileUploads, options.MaxConcurrentFileUploads);
    }

    /// <summary>Gets the current lifecycle state.</summary>
    public LocalSendNodeState State { get { lock (_stateGate) return _state; } }

    /// <summary>Gets why multicast discovery is unavailable after a successful HTTP start, or <see langword="null"/> when it is running.</summary>
    public string? DiscoveryError { get { lock (_stateGate) return _discoveryError; } }

    /// <summary>Gets the persistent local identity after startup, or <see langword="null"/> before identity loading.</summary>
    public LocalSendIdentity? Identity => _identity is null ? null : new(
        _options.Alias, V2Constants.Version, _options.DeviceModel, _options.DeviceType, _identity.Fingerprint,
        _options.Port, _options.EnableHttps ? LocalSendProtocol.Https : LocalSendProtocol.Http);

    /// <summary>Loads identity, starts the HTTP server, and starts multicast discovery when the UDP port is available.</summary>
    /// <param name="cancellationToken">Cancels startup. A cancelled startup may be retried.</param>
    /// <remarks>A multicast bind failure leaves the node running so transfers still work. Nearby devices are then found with an HTTP subnet scan.</remarks>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
                return;
            if (_stopped)
                throw new InvalidOperationException("A stopped LocalSendNode cannot be restarted; create a new node instance.");
            SetState(LocalSendNodeState.Starting);
            try
            {
                Directory.CreateDirectory(_options.DownloadDirectory);
                _identity = await DeviceIdentityStore.LoadOrCreateAsync(_options.DataDirectory, cancellationToken).ConfigureAwait(false);
                _client = new V2HttpClient(_identity, _options);
                _webShare = new WebShareService(_options, _logger);
                _webShare.Changed += PublishWebShare;
                _server = new V2Server(_options, _identity, () => CreateLocalInfo(), OnRegisterAsync, OnPrepareAsync, OnUploadAsync, OnCancelAsync, _logger, _webShare);
                await _server.StartAsync(cancellationToken).ConfigureAwait(false);
                await StartDiscoveryAsync(cancellationToken).ConfigureAwait(false);
                _started = true;
                SetState(LocalSendNodeState.Running);
                _maintenance = MaintainAsync(_lifetime.Token);
                if (_discovery is null)
                    _ = ScanSubnetsAsync(_lifetime.Token);
            }
            catch (Exception exception)
            {
                if (_discovery is not null)
                {
                    try { await _discovery.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception cleanupException) { _logger.LogDebug(cleanupException, "Could not clean up discovery after startup failure"); }
                }
                if (_server is not null)
                {
                    try { await _server.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception cleanupException) { _logger.LogDebug(cleanupException, "Could not clean up server after startup failure"); }
                }
                _discovery = null;
                if (_webShare is not null)
                {
                    _webShare.Changed -= PublishWebShare;
                    _webShare.Stop();
                }
                _webShare = null;
                _server = null;
                _client = null;
                _identity?.Dispose();
                _identity = null;
                _discoveryError = null;
                _started = false;
                SetState(exception is OperationCanceledException ? LocalSendNodeState.Created : LocalSendNodeState.Faulted,
                    exception is OperationCanceledException ? null : exception);
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Stops discovery, rejects pending offers, cancels transfers, and releases listening ports.</summary>
    /// <param name="cancellationToken">Cancels the stop wait.</param>
    /// <remarks>A successfully stopped instance cannot be restarted.</remarks>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started && _server is null)
                return;
            SetState(LocalSendNodeState.Stopping);
            await _lifetime.CancelAsync().ConfigureAwait(false);
            foreach (var session in _pending.Values)
                session.Decision.TrySetResult(new(false, null));
            foreach (var session in _incomingSessions.Values)
            {
                await session.Cancellation.CancelAsync().ConfigureAwait(false);
                session.Cancel();
            }
            foreach (var outgoing in _outgoingSessions.Values)
                await outgoing.Cancellation.CancelAsync().ConfigureAwait(false);
            if (_maintenance is not null)
            {
                try { await _maintenance.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            if (_discovery is not null)
                await _discovery.DisposeAsync().ConfigureAwait(false);
            if (_server is not null)
                await _server.DisposeAsync().ConfigureAwait(false);
            if (_webShare is not null)
            {
                _webShare.Changed -= PublishWebShare;
                _webShare.Stop();
            }
            _discovery = null;
            _webShare = null;
            _server = null;
            _started = false;
            _stopped = true;
            SetState(LocalSendNodeState.Stopped);
            _deviceChanges.Complete();
            _incomingTransfers.Complete();
            _webShareChanges.Complete();
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Rebinds multicast when possible, announces if it is running, and scans local /24 subnets over HTTP.</summary>
    /// <param name="cancellationToken">Cancels refresh.</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        await StartDiscoveryAsync(cancellationToken).ConfigureAwait(false);
        if (_discovery is not null)
        {
            try
            {
                await _discovery.RefreshInterfacesAsync(force: true, cancellationToken).ConfigureAwait(false);
                await _discovery.AnnounceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "LocalSend multicast refresh failed; continuing with HTTP discovery");
                SetDiscoveryError(exception.Message);
            }
        }

        if (_discovery is null)
            await ScanSubnetsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets an ordered snapshot of currently known devices.</summary>
    public IReadOnlyList<LocalSendDevice> GetDevices() => _devices.Snapshot();

    /// <summary>Watches node lifecycle transitions occurring after subscription.</summary>
    /// <param name="cancellationToken">Stops enumeration.</param>
    public IAsyncEnumerable<LocalSendNodeStateChange> WatchStateChangesAsync(CancellationToken cancellationToken = default) => _stateChanges.Subscribe(cancellationToken);

    /// <summary>Watches device additions, updates, and removals occurring after subscription.</summary>
    /// <param name="cancellationToken">Stops enumeration.</param>
    public IAsyncEnumerable<DeviceChange> WatchDeviceChangesAsync(CancellationToken cancellationToken = default) => _deviceChanges.Subscribe(cancellationToken);

    /// <summary>Watches incoming offers that require an accept or decline decision.</summary>
    /// <param name="cancellationToken">Stops enumeration.</param>
    public IAsyncEnumerable<IncomingTransferRequest> WatchIncomingTransfersAsync(CancellationToken cancellationToken = default) => _incomingTransfers.Subscribe(cancellationToken);

    /// <summary>Gets a snapshot of the current browser share session, or an inactive state.</summary>
    public WebShareState GetWebShare() => _webShare?.Snapshot() ?? WebShareState.Inactive;

    /// <summary>Watches browser share session changes occurring after subscription.</summary>
    /// <param name="cancellationToken">Stops enumeration.</param>
    public IAsyncEnumerable<WebShareState> WatchWebShareAsync(CancellationToken cancellationToken = default) => _webShareChanges.Subscribe(cancellationToken);

    /// <summary>Serves the given items to browsers at this node's HTTP root.</summary>
    /// <param name="items">Files and text offered for download.</param>
    /// <param name="options">PIN and auto-accept behavior.</param>
    /// <param name="cancellationToken">Cancels the start wait.</param>
    public Task StartWebShareAsync(IReadOnlyList<SendItem> items, WebShareOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();
        _webShare!.Start(items, options);
        return Task.CompletedTask;
    }

    /// <summary>Serves a browser page that uploads files to this node.</summary>
    /// <param name="options">PIN and auto-accept behavior.</param>
    /// <param name="cancellationToken">Cancels the start wait.</param>
    public Task StartWebReceiveAsync(WebShareOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();
        _webShare!.StartReceive(options);
        return Task.CompletedTask;
    }

    /// <summary>Stops serving browser downloads.</summary>
    public void StopWebShare() => _webShare?.Stop();

    /// <summary>Sets whether new browser requests are accepted without confirmation.</summary>
    public void SetWebShareAutoAccept(bool autoAccept) => _webShare?.SetAutoAccept(autoAccept);

    /// <summary>Sets or clears the PIN browsers must enter.</summary>
    public void SetWebSharePin(string? pin) => _webShare?.SetPin(pin);

    /// <summary>Accepts a pending browser download request.</summary>
    public bool AcceptWebShareRequest(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _webShare?.Accept(sessionId) == true;
    }

    /// <summary>Declines a pending browser download request.</summary>
    public bool DeclineWebShareRequest(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _webShare?.Decline(sessionId) == true;
    }

    /// <summary>Registers with and retains a manually trusted peer until explicit removal or disposal.</summary>
    /// <param name="endpoint">The peer endpoint.</param>
    /// <param name="fingerprint">The trusted peer certificate fingerprint.</param>
    /// <param name="cancellationToken">Cancels registration.</param>
    /// <returns>The registered peer snapshot.</returns>
    public async Task<LocalSendDevice> AddKnownDeviceAsync(DeviceEndpoint endpoint, string fingerprint, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        RegisterResponseDto response;
        try { response = await _client!.RegisterAsync(endpoint, fingerprint, CreateLocalInfo(), cancellationToken).ConfigureAwait(false); }
        catch (HttpRequestException exception) when (ClassifyFailure(exception, string.Empty) == TransferFailureCodes.PeerIdentity)
        {
            throw new PeerIdentityException("The peer failed TLS identity validation.", exception);
        }
        var device = new LocalSendDevice(response.Alias, response.Version, response.DeviceModel,
            V2ProtocolAdapter.ParseDeviceType(response.DeviceType), fingerprint, response.Download, [endpoint], DateTimeOffset.UtcNow);
        return _devices.Upsert(device, persistent: true);
    }

    /// <summary>Inspects a manually entered endpoint without adding it to the device list.</summary>
    /// <param name="endpoint">The endpoint to inspect.</param>
    /// <param name="cancellationToken">Cancels probing.</param>
    /// <returns>Peer metadata and whether HTTPS verified its identity binding.</returns>
    public async Task<DeviceProbeResult> ProbeDeviceAsync(DeviceEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        ArgumentNullException.ThrowIfNull(endpoint);
        var result = await _client!.ProbeAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var device = new LocalSendDevice(result.Info.Alias, result.Info.Version, result.Info.DeviceModel,
            V2ProtocolAdapter.ParseDeviceType(result.Info.DeviceType), result.Fingerprint, result.Info.Download, [endpoint], DateTimeOffset.UtcNow);
        return new(device, result.Verified);
    }

    /// <summary>Removes a known or manually trusted device.</summary>
    /// <param name="fingerprint">The device fingerprint.</param>
    /// <returns><see langword="true"/> when a device was removed.</returns>
    public bool RemoveDevice(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        return _devices.Remove(fingerprint);
    }

    /// <summary>Cancels an active outgoing, pending incoming, or accepted incoming transfer.</summary>
    /// <param name="transferId">The transfer identifier reported by progress or an incoming request.</param>
    /// <param name="cancellationToken">Cancels the cancellation request.</param>
    /// <returns><see langword="true"/> when the transfer was active.</returns>
    public async Task<bool> CancelTransferAsync(Guid transferId, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();
        if (_activeOutgoing.TryGetValue(transferId, out var outgoing))
        {
            await outgoing.CancelAsync().ConfigureAwait(false);
            return true;
        }
        var incoming = _pending.Values.Concat(_incomingSessions.Values).FirstOrDefault(session => session.TransferId == transferId);
        if (incoming is null)
            return false;
        incoming.Decision.TrySetResult(new(false, null));
        await incoming.Cancellation.CancelAsync().ConfigureAwait(false);
        incoming.Cancel();
        return true;
    }

    /// <summary>Offers items to one peer and streams the receiver-selected content.</summary>
    /// <param name="device">The target peer.</param>
    /// <param name="items">Items to offer.</param>
    /// <param name="options">Optional PIN and checksum behavior.</param>
    /// <param name="progress">Optional aggregate progress callback.</param>
    /// <param name="cancellationToken">Cancels negotiation or upload.</param>
    /// <returns>The final transfer outcome.</returns>
    /// <exception cref="PinRequiredException">The peer requires a PIN or rejected the supplied PIN.</exception>
    /// <exception cref="PinRateLimitedException">The peer temporarily rate-limited PIN attempts.</exception>
    public async Task<TransferResult> SendAsync(
        LocalSendDevice device,
        IReadOnlyCollection<SendItem> items,
        SendOptions? options = null,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("At least one item is required.", nameof(items));
        options ??= new SendOptions();
        var endpoint = device.Endpoints.OrderByDescending(static x => x.Protocol == LocalSendProtocol.Https).FirstOrDefault()
            ?? throw new LocalSendException("The device has no usable v2 endpoint.");
        var transferId = Guid.NewGuid();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        _activeOutgoing[transferId] = linked;
        PrepareUploadResponseDto? prepared = null;
        var results = new List<TransferredItemResult>();
        try
        {
            var itemMap = items.Select(item => (Id: Guid.NewGuid().ToString("N"), Item: item, Length: item.Length)).ToArray();
            var totalBytes = itemMap.Sum(static x => x.Length);
            progress?.Report(new(transferId, null, TransferDirection.Send, TransferState.Preparing, 0, totalBytes));
            var files = new Dictionary<string, FileDto>(StringComparer.Ordinal);
            foreach (var item in itemMap)
            {
                var sha256 = options.ComputeSha256 ? await ComputeSha256Async(item.Item, linked.Token).ConfigureAwait(false) : null;
                files[item.Id] = ToFileDto(item.Id, item.Item, item.Length, sha256);
            }
            var dto = new PrepareUploadRequestDto { Info = CreateLocalInfo(), Files = files };
            progress?.Report(new(transferId, null, TransferDirection.Send, TransferState.WaitingForAcceptance, 0, totalBytes));
            prepared = await _client!.PrepareUploadAsync(endpoint, device.Fingerprint, dto, options.Pin, linked.Token).ConfigureAwait(false);
            _outgoingSessions[prepared.SessionId] = (endpoint.Address, linked);
            long completedBytes = 0;
            foreach (var (id, item, length) in itemMap)
            {
                if (!prepared.Files.TryGetValue(id, out var token))
                    continue;
                await using var source = await item.OpenReadAsync(linked.Token).ConfigureAwait(false);
                if (source.CanSeek && source.Length != length)
                    throw new IOException($"The source length changed before upload: {item.FileName}.");
                await using var tracked = new ProgressReadStream(source, current => progress?.Report(new(
                    transferId, id, TransferDirection.Send, TransferState.Transferring, completedBytes + current, totalBytes)));
                await _client.UploadAsync(endpoint, device.Fingerprint, prepared.SessionId, id, token, tracked, length, item.ContentType, linked.Token).ConfigureAwait(false);
                completedBytes += length;
                results.Add(new(id, item.FileName, length, null));
            }
            progress?.Report(new(transferId, null, TransferDirection.Send, TransferState.Completed, completedBytes, totalBytes));
            return new(transferId, TransferDirection.Send, TransferState.Completed, results);
        }
        catch (PinRequiredException) { throw; }
        catch (PinRateLimitedException) { throw; }
        catch (OperationCanceledException)
        {
            if (prepared is not null)
                await TryCancelRemoteAsync(endpoint, device.Fingerprint, prepared.SessionId).ConfigureAwait(false);
            return new(transferId, TransferDirection.Send, TransferState.Cancelled, results);
        }
        catch (Exception exception)
        {
            if (prepared is not null)
                await TryCancelRemoteAsync(endpoint, device.Fingerprint, prepared.SessionId).ConfigureAwait(false);
            _logger.LogWarning(exception, "Outgoing transfer {TransferId} failed", transferId);
            return new(transferId, TransferDirection.Send, TransferState.Failed, results,
                new(ClassifyFailure(exception, prepared is null ? TransferFailureCodes.PrepareFailed : TransferFailureCodes.UploadFailed), exception.GetBaseException().Message));
        }
        finally
        {
            _activeOutgoing.TryRemove(transferId, out _);
            if (prepared is not null)
                _outgoingSessions.TryRemove(prepared.SessionId, out _);
        }
    }

    /// <summary>Accepts all or selected items from a pending incoming offer and waits for completion.</summary>
    /// <param name="requestId">The incoming request identifier.</param>
    /// <param name="options">Selection, destination, and rename options.</param>
    /// <param name="progress">Optional aggregate receive progress callback.</param>
    /// <param name="cancellationToken">Cancels receiving and notifies the sender.</param>
    /// <returns>The final receive outcome.</returns>
    public async Task<TransferResult> AcceptAsync(Guid requestId, AcceptTransferOptions? options = null, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        if (!_pending.TryGetValue(requestId, out var session))
            throw new LocalSendException("The incoming request no longer exists.");
        options ??= new AcceptTransferOptions();
        var knownIds = session.PublicRequest.Items.Select(static item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (options.AcceptedItemIds?.Any(id => !knownIds.Contains(id)) == true)
            throw new ArgumentException("AcceptedItemIds contains an item that is not part of this request.", nameof(options));
        if (options.TargetFileNames?.Keys.Any(id => !knownIds.Contains(id)) == true)
            throw new ArgumentException("TargetFileNames contains an item that is not part of this request.", nameof(options));
        session.Progress = progress;
        session.Decision.TrySetResult(new(true, options));
        try
        {
            return await session.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await session.Cancellation.CancelAsync().ConfigureAwait(false);
            session.Cancel();
            var endpoint = session.PublicRequest.Sender.Endpoints.FirstOrDefault();
            if (endpoint is not null)
            {
                await TryCancelRemoteAsync(endpoint, session.PublicRequest.Sender.Fingerprint, session.SessionId).ConfigureAwait(false);
            }
            return new(session.TransferId, TransferDirection.Receive, TransferState.Cancelled, []);
        }
    }

    /// <summary>Declines a pending incoming offer.</summary>
    /// <param name="requestId">The incoming request identifier.</param>
    /// <param name="cancellationToken">Cancels the local operation before the decision is applied.</param>
    public Task DeclineAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();
        if (!_pending.TryGetValue(requestId, out var session))
            throw new LocalSendException("The incoming request no longer exists.");
        session.Decision.TrySetResult(new(false, null));
        return Task.CompletedTask;
    }

    private DeviceInfoDto CreateLocalInfo(bool announce = false) => _protocol.CreateDeviceInfo(_identity ?? throw new InvalidOperationException("Identity is not loaded."), _options, announce);

    private async Task StartDiscoveryAsync(CancellationToken cancellationToken)
    {
        if (_discovery is not null)
            return;

        var discovery = new V2MulticastDiscovery(_options, () => CreateLocalInfo(announce: true), OnAnnouncementAsync, _logger);
        try
        {
            await discovery.StartAsync(cancellationToken).ConfigureAwait(false);
            _discovery = discovery;
            SetDiscoveryError(null);
            try { await discovery.AnnounceAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "LocalSend multicast announce failed");
            }
        }
        catch (OperationCanceledException)
        {
            await discovery.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "LocalSend multicast discovery is unavailable; HTTP subnet scan will be used");
            SetDiscoveryError(exception.Message);
            try { await discovery.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupException) { _logger.LogDebug(cleanupException, "Could not clean up failed LocalSend discovery"); }
        }
    }

    private void SetDiscoveryError(string? message)
    {
        lock (_stateGate)
            _discoveryError = message;
    }

    private async Task ScanSubnetsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> localAddresses;
        try { localAddresses = LocalNetworkAddresses.GetUnicastIPv4(_options.NetworkWhitelist, _options.NetworkBlacklist); }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not list interfaces for HTTP discovery");
            return;
        }

        var localSet = localAddresses.ToHashSet();
        var prefixes = localAddresses
            .Where(static address => !LocalNetworkAddresses.IsAutomaticPrivate(address))
            .Select(static address => address.GetAddressBytes())
            .Select(static octets => (octets[0], octets[1], octets[2]))
            .Distinct()
            .ToArray();
        if (prefixes.Length == 0)
            return;

        using var gate = new SemaphoreSlim(50, 50);
        var probes = new List<Task>();
        foreach (var (a, b, c) in prefixes)
        {
            for (var host = 1; host <= 254; host++)
            {
                var address = new IPAddress([(byte)a, (byte)b, (byte)c, (byte)host]);
                if (localSet.Contains(address))
                    continue;
                probes.Add(ProbeScannedHostAsync(address, gate, cancellationToken));
            }
        }

        try { await Task.WhenAll(probes).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ProbeScannedHostAsync(IPAddress address, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.DiscoveryTimeout);
            var endpoint = new DeviceEndpoint(address, _options.Port, _options.EnableHttps ? LocalSendProtocol.Https : LocalSendProtocol.Http);
            var result = await _client!.ProbeAsync(endpoint, timeout.Token, _options.DiscoveryTimeout).ConfigureAwait(false);
            if (StringComparer.OrdinalIgnoreCase.Equals(result.Fingerprint, _identity!.Fingerprint))
                return;
            try { await _client.RegisterAsync(endpoint, result.Fingerprint, CreateLocalInfo(), timeout.Token, _options.DiscoveryTimeout).ConfigureAwait(false); }
            catch (Exception exception) { _logger.LogDebug(exception, "Could not register with scanned peer {Address}", address); }
            _devices.Upsert(new LocalSendDevice(result.Info.Alias, result.Info.Version, result.Info.DeviceModel,
                V2ProtocolAdapter.ParseDeviceType(result.Info.DeviceType), result.Fingerprint, result.Info.Download,
                [endpoint], DateTimeOffset.UtcNow));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
        }
        finally { gate.Release(); }
    }

    private async Task OnAnnouncementAsync(DeviceInfoDto announcement, IPAddress source, CancellationToken cancellationToken)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(announcement.Fingerprint, _identity!.Fingerprint) || announcement.Port is < 1 or > 65535)
            return;
        var endpoint = new DeviceEndpoint(source, announcement.Port, StringComparer.OrdinalIgnoreCase.Equals(announcement.Protocol, "https") ? LocalSendProtocol.Https : LocalSendProtocol.Http);
        var response = await _client!.RegisterAsync(endpoint, announcement.Fingerprint, CreateLocalInfo(), cancellationToken).ConfigureAwait(false);
        var candidate = new LocalSendDevice(response.Alias, response.Version, response.DeviceModel,
            V2ProtocolAdapter.ParseDeviceType(response.DeviceType), announcement.Fingerprint, response.Download, [endpoint], DateTimeOffset.UtcNow);
        _devices.Upsert(candidate);
    }

    private Task OnRegisterAsync(DeviceInfoDto info, IPAddress source, string? certificateFingerprint)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(info.Fingerprint, _identity!.Fingerprint) || info.Port is < 1 or > 65535)
            return Task.CompletedTask;
        var fingerprint = certificateFingerprint ?? info.Fingerprint;
        var endpoint = new DeviceEndpoint(source, info.Port, StringComparer.OrdinalIgnoreCase.Equals(info.Protocol, "https") ? LocalSendProtocol.Https : LocalSendProtocol.Http);
        _devices.Upsert(new(info.Alias, info.Version, info.DeviceModel, V2ProtocolAdapter.ParseDeviceType(info.DeviceType), fingerprint, info.Download, [endpoint], DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    private async Task<PrepareOutcome> OnPrepareAsync(
        PrepareUploadRequestDto request,
        IPAddress remote,
        string? certificateFingerprint,
        bool autoAccept,
        CancellationToken requestCancellation)
    {
        if (!await _transferSlots.WaitAsync(0, requestCancellation).ConfigureAwait(false))
            return new(HttpStatusCode.TooManyRequests, Message: "The receiver is handling the maximum number of transfers");
        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString("N");
        var transferId = Guid.NewGuid();
        var endpoint = new DeviceEndpoint(remote, request.Info.Port, StringComparer.OrdinalIgnoreCase.Equals(request.Info.Protocol, "https") ? LocalSendProtocol.Https : LocalSendProtocol.Http);
        var sender = new LocalSendDevice(request.Info.Alias, request.Info.Version, request.Info.DeviceModel,
            V2ProtocolAdapter.ParseDeviceType(request.Info.DeviceType), certificateFingerprint ?? request.Info.Fingerprint, request.Info.Download, [endpoint], DateTimeOffset.UtcNow);
        var items = request.Files.Values.Select(ToIncomingItem).ToArray();
        var publicRequest = new IncomingTransferRequest(requestId, transferId, sessionId, sender, items, DateTimeOffset.UtcNow);
        var session = new IncomingSession
        {
            RequestId = requestId,
            TransferId = transferId,
            SessionId = sessionId,
            RemoteAddress = remote,
            Request = request,
            PublicRequest = publicRequest,
            Decision = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Completion = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        IncomingDecision decision;
        if (autoAccept)
        {
            decision = new(true, new AcceptTransferOptions());
        }
        else
        {
            _pending[requestId] = session;
            await _incomingTransfers.PublishAsync(publicRequest, requestCancellation).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(_options.IncomingDecisionTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation, timeout.Token, _lifetime.Token);
            try { decision = await session.Decision.Task.WaitAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                _pending.TryRemove(requestId, out _);
                _transferSlots.Release();
                return new(HttpStatusCode.RequestTimeout, Message: "Incoming transfer decision timed out");
            }
            _pending.TryRemove(requestId, out _);
        }
        if (!decision.Accepted)
        {
            _transferSlots.Release();
            session.Completion.TrySetResult(new(transferId, TransferDirection.Receive, TransferState.Cancelled, []));
            return new(HttpStatusCode.Forbidden, Message: "Transfer declined");
        }

        var selected = decision.Options!.AcceptedItemIds is null
            ? request.Files.Keys.ToHashSet(StringComparer.Ordinal)
            : decision.Options.AcceptedItemIds.Where(request.Files.ContainsKey).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            _transferSlots.Release();
            session.Completion.TrySetResult(new(transferId, TransferDirection.Receive, TransferState.Completed, []));
            return new(HttpStatusCode.NoContent);
        }

        try
        {
            var destinationRoot = decision.Options.DestinationDirectory ?? _options.DownloadDirectory;
            var reserved = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var id in selected)
            {
                var file = request.Files[id];
                var targetName = decision.Options.TargetFileNames?.GetValueOrDefault(id) ?? file.FileName;
                session.Destinations[id] = SafeFileTarget.ResolveUnique(destinationRoot, targetName, reserved);
                session.Tokens[id] = Guid.NewGuid().ToString("N");
            }
        }
        catch (Exception exception)
        {
            _transferSlots.Release();
            session.Fail(TransferFailureCodes.InvalidDestination, exception.Message);
            return new(HttpStatusCode.BadRequest, Message: exception.Message);
        }
        session.InitializeAccepted(selected);
        _incomingSessions[sessionId] = session;
        _ = ExpireIncomingSessionAsync(session);
        _ = ReleaseIncomingSlotWhenDoneAsync(session);
        return new(HttpStatusCode.OK, new PrepareUploadResponseDto { SessionId = sessionId, Files = session.TokenSnapshot() });
    }

    private async Task<HttpStatusCode> OnUploadAsync(string sessionId, string fileId, string token, IPAddress remote, Stream body, long? contentLength, CancellationToken requestCancellation)
    {
        if (!_incomingSessions.TryGetValue(sessionId, out var session) || !session.RemoteAddress.Equals(remote))
            return HttpStatusCode.NotFound;
        if (!await _uploadSlots.WaitAsync(0, requestCancellation).ConfigureAwait(false))
            return HttpStatusCode.TooManyRequests;
        try
        {
            if (!session.TryConsumeToken(fileId, token) || !session.Request.Files.TryGetValue(fileId, out var file))
                return HttpStatusCode.Forbidden;
            if (contentLength is not null && contentLength != file.Size)
            {
                session.Fail(TransferFailureCodes.LengthMismatch, $"Expected {file.Size} bytes but the request declared {contentLength}.", fileId);
                return HttpStatusCode.BadRequest;
            }

            var destination = session.Destinations[fileId];
            var temporary = destination + $".part-{Guid.NewGuid():N}";
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation, session.Cancellation.Token, _lifetime.Token);
            long written = 0;
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 512 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[512 * 1024];
                    while (true)
                    {
                        var read = await body.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                        if (read == 0) break;
                        written += read;
                        if (written > file.Size)
                            throw new LocalSendException("Uploaded content exceeded the declared size.");
                        sha256.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), linked.Token).ConfigureAwait(false);
                        session.ReportProgress(fileId, written);
                    }
                    await output.FlushAsync(linked.Token).ConfigureAwait(false);
                }
                if (written != file.Size)
                    throw new LocalSendException($"Uploaded content length mismatch: expected {file.Size}, received {written}.");
                if (file.Sha256 is not null && !MatchesSha256(sha256.GetHashAndReset(), file.Sha256))
                    throw new LocalSendException("Uploaded content failed SHA-256 verification.");
                File.Move(temporary, destination);
                RestoreTimestamps(destination, file.Metadata);
                session.FileCompleted(fileId, file.FileName, written, destination);
                return HttpStatusCode.OK;
            }
            catch (OperationCanceledException)
            {
                TryDelete(temporary);
                session.Cancel();
                return HttpStatusCode.BadRequest;
            }
            catch (Exception exception)
            {
                TryDelete(temporary);
                session.Fail(exception.Message.Contains("SHA-256", StringComparison.Ordinal) ? TransferFailureCodes.ChecksumMismatch : TransferFailureCodes.ReceiveFailed, exception.Message, fileId);
                return HttpStatusCode.BadRequest;
            }
        }
        finally { _uploadSlots.Release(); }
    }

    private async Task<bool> OnCancelAsync(string sessionId, IPAddress remote, CancellationToken cancellationToken)
    {
        if (_incomingSessions.TryGetValue(sessionId, out var incoming) && incoming.RemoteAddress.Equals(remote))
        {
            await incoming.Cancellation.CancelAsync().ConfigureAwait(false);
            incoming.Cancel();
            return true;
        }
        if (_outgoingSessions.TryGetValue(sessionId, out var outgoing) && outgoing.Address.Equals(remote))
        {
            await outgoing.Cancellation.CancelAsync().ConfigureAwait(false);
            return true;
        }
        return false;
    }

    private async Task ReleaseIncomingSlotWhenDoneAsync(IncomingSession session)
    {
        await session.Completion.Task.ConfigureAwait(false);
        _incomingSessions.TryRemove(session.SessionId, out _);
        session.Cancellation.Dispose();
        _transferSlots.Release();
    }

    private async Task ExpireIncomingSessionAsync(IncomingSession session)
    {
        try
        {
            var timeout = Task.Delay(_options.IncomingTransferTimeout, _lifetime.Token);
            if (await Task.WhenAny(session.Completion.Task, timeout).ConfigureAwait(false) == timeout && !_lifetime.IsCancellationRequested)
            {
                await session.Cancellation.CancelAsync().ConfigureAwait(false);
                session.Fail(TransferFailureCodes.TransferTimeout, "The sender did not finish the accepted transfer before its timeout.");
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private static FileDto ToFileDto(string id, SendItem item, long length, string? sha256)
    {
        FileMetadataDto? metadata = null;
        if (item is SendFileItem file)
        {
            var info = new FileInfo(file.Path);
            metadata = new() { Modified = info.LastWriteTimeUtc.ToString("O"), Accessed = info.LastAccessTimeUtc.ToString("O") };
        }
        return new() { Id = id, FileName = item.FileName, Size = length, FileType = item.ContentType, Sha256 = sha256, Metadata = metadata };
    }

    private static async Task<string> ComputeSha256Async(SendItem item, CancellationToken cancellationToken)
    {
        await using var stream = await item.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool MatchesSha256(byte[] actual, string expected)
    {
        try
        {
            byte[] parsed;
            if (expected.Length == 64)
                parsed = Convert.FromHexString(expected);
            else
                parsed = Convert.FromBase64String(expected);
            return parsed.Length == actual.Length && CryptographicOperations.FixedTimeEquals(actual, parsed);
        }
        catch (FormatException) { return false; }
    }

    private static string ClassifyFailure(Exception exception, string fallback)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is PeerIdentityException) return TransferFailureCodes.PeerIdentity;
            if (current is PeerBusyException) return TransferFailureCodes.PeerBusy;
            if (current is TransferDeclinedException) return TransferFailureCodes.Declined;
            if (current is IOException) return TransferFailureCodes.SourceIo;
        }
        return fallback;
    }

    private async Task TryCancelRemoteAsync(DeviceEndpoint endpoint, string fingerprint, string sessionId)
    {
        using var timeout = new CancellationTokenSource(_options.CancelRequestTimeout);
        try { await _client!.CancelAsync(endpoint, fingerprint, sessionId, timeout.Token).ConfigureAwait(false); }
        catch (Exception exception) { _logger.LogDebug(exception, "Could not cancel remote session {SessionId}", sessionId); }
    }

    private async Task MaintainAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.AnnouncementInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    if (_discovery is not null)
                    {
                        await _discovery.RefreshInterfacesAsync(force: false, cancellationToken).ConfigureAwait(false);
                        await _discovery.AnnounceAsync(cancellationToken).ConfigureAwait(false);
                    }
                    _devices.RemoveExpired(DateTimeOffset.UtcNow - _options.DeviceExpiration);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) { _logger.LogWarning(exception, "LocalSend maintenance iteration failed"); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void SetState(LocalSendNodeState state, Exception? error = null)
    {
        LocalSendNodeState previous;
        lock (_stateGate)
        {
            previous = _state;
            if (previous == state)
                return;
            _state = state;
        }
        _stateChanges.Publish(new(previous, state, error));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static IncomingItem ToIncomingItem(FileDto file) => new(file.Id, file.FileName, file.Size, file.FileType, file.Sha256, file.Preview,
        ParseTimestamp(file.Metadata?.Modified), ParseTimestamp(file.Metadata?.Accessed));

    private static DateTimeOffset? ParseTimestamp(string? value) => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static void RestoreTimestamps(string path, FileMetadataDto? metadata)
    {
        if (metadata is null) return;
        if (ParseTimestamp(metadata.Modified) is { } modified) File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
        if (ParseTimestamp(metadata.Accessed) is { } accessed) File.SetLastAccessTimeUtc(path, accessed.UtcDateTime);
    }

    private void PublishWebShare() => _webShareChanges.Publish(GetWebShare());

    private void EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started || State != LocalSendNodeState.Running) throw new InvalidOperationException("The LocalSend node is not running.");
    }

    /// <summary>Stops the node and releases identities, sockets, channels, and synchronization resources.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        SetState(LocalSendNodeState.Disposed);
        _stateChanges.Complete();
        _identity?.Dispose();
        _lifetime.Dispose();
        _transferSlots.Dispose();
        _uploadSlots.Dispose();
        _lifecycle.Dispose();
    }
}
