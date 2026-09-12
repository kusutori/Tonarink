using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Collections.Concurrent;
using LocalSendDotNet.Protocol.V2;
using Microsoft.Extensions.Logging;

namespace LocalSendDotNet;

internal sealed class V2MulticastDiscovery(
    LocalSendOptions options,
    Func<DeviceInfoDto> createAnnouncement,
    Func<DeviceInfoDto, IPAddress, CancellationToken, Task> onAnnouncement,
    ILogger logger,
    Func<IReadOnlyList<IPAddress>>? addressProvider = null) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _announceGate = new(1, 1);
    private readonly SemaphoreSlim _receiverGate = new(1, 1);
    private readonly ConcurrentDictionary<IPAddress, byte> _registrations = new();
    private readonly Func<IReadOnlyList<IPAddress>> _addressProvider = addressProvider
        ?? (() => LocalNetworkAddresses.GetUnicastIPv4(options.NetworkWhitelist, options.NetworkBlacklist));
    private Socket? _receiver;
    private CancellationTokenSource? _receiverStop;
    private Task? _receiveLoop;
    private IReadOnlyList<IPAddress> _joinedAddresses = [];
    private int _refreshScheduled;
    private bool _started;

    internal IReadOnlyList<IPAddress> JoinedAddresses => _joinedAddresses;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try { await RefreshInterfacesAsync(force: true, requireInterface: true, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is SocketException or NetworkInformationException)
        {
            throw new DiscoveryUnavailableException($"UDP port {options.Port} could not start LocalSend discovery.", exception);
        }
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        _started = true;
    }

    internal Task<bool> RefreshInterfacesAsync(bool force, CancellationToken cancellationToken = default) =>
        RefreshInterfacesAsync(force, requireInterface: false, cancellationToken);

    private async Task<bool> RefreshInterfacesAsync(bool force, bool requireInterface, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var addresses = _addressProvider().Distinct().OrderBy(static address => address.ToString(), StringComparer.Ordinal).ToArray();
        await _receiverGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!force && addresses.SequenceEqual(_joinedAddresses))
                return false;

            Socket receiver;
            IReadOnlyList<IPAddress> joinedAddresses;
            try { (receiver, joinedAddresses) = CreateReceiver(addresses); }
            catch (Exception exception) when (!requireInterface && exception is SocketException or DiscoveryUnavailableException)
            {
                logger.LogWarning(exception, "LocalSend discovery could not bind to the current network interfaces");
                return false;
            }

            var receiverStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            var receiveLoop = ReceiveLoopAsync(receiver, receiverStop.Token);
            var oldReceiver = _receiver;
            var oldStop = _receiverStop;
            var oldLoop = _receiveLoop;
            _receiver = receiver;
            _receiverStop = receiverStop;
            _receiveLoop = receiveLoop;
            _joinedAddresses = joinedAddresses;

            if (oldStop is not null)
                await oldStop.CancelAsync().ConfigureAwait(false);
            oldReceiver?.Dispose();
            if (oldLoop is not null)
            {
                try { await oldLoop.ConfigureAwait(false); }
                catch (ObjectDisposedException)
                {
                    // Replacing the receiver disposes the socket observed by the old loop.
                }
            }
            oldStop?.Dispose();
            logger.LogInformation("LocalSend discovery is listening on {Count} IPv4 interface(s)", joinedAddresses.Count);
            return true;
        }
        finally { _receiverGate.Release(); }
    }

    private (Socket Receiver, IReadOnlyList<IPAddress> JoinedAddresses) CreateReceiver(IReadOnlyCollection<IPAddress> addresses)
    {
        if (addresses.Count == 0)
            throw new DiscoveryUnavailableException("No usable IPv4 interface is currently available.");
        var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            receiver.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            receiver.ExclusiveAddressUse = false;
            receiver.Bind(new IPEndPoint(IPAddress.Any, options.Port));
            var joined = new List<IPAddress>();
            foreach (var address in addresses)
            {
                try
                {
                    receiver.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(options.MulticastAddress, address));
                    joined.Add(address);
                }
                catch (SocketException exception)
                {
                    logger.LogDebug(exception, "Could not join LocalSend multicast on {Address}", address);
                }
            }
            if (joined.Count == 0)
                throw new DiscoveryUnavailableException("No IPv4 interface could join the LocalSend multicast group.");
            return (receiver, joined);
        }
        catch
        {
            receiver.Dispose();
            throw;
        }
    }

    public async Task AnnounceAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _announceGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(createAnnouncement(), V2JsonContext.Default.DeviceInfoDto);
            var delays = new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2) };
            foreach (var delay in delays)
            {
                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                await SendOnAllInterfacesAsync(payload, linked.Token).ConfigureAwait(false);
            }
        }
        finally { _announceGate.Release(); }
    }

    private async Task ReceiveLoopAsync(Socket receiver, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await receiver.ReceiveFromAsync(buffer, SocketFlags.None, remote, cancellationToken).ConfigureAwait(false);
                var message = JsonSerializer.Deserialize(buffer.AsSpan(0, result.ReceivedBytes), V2JsonContext.Default.DeviceInfoDto);
                if (message is null || result.RemoteEndPoint is not IPEndPoint endpoint)
                    continue;
                if (_registrations.TryAdd(endpoint.Address, 0))
                    _ = ObserveAnnouncementAsync(message, endpoint.Address, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (JsonException exception)
            {
                logger.LogDebug(exception, "Ignoring malformed LocalSend multicast announcement");
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "LocalSend multicast receive failed");
                ScheduleRefresh();
                break;
            }
        }
    }

    private async Task ObserveAnnouncementAsync(DeviceInfoDto message, IPAddress source, CancellationToken cancellationToken)
    {
        try { await onAnnouncement(message, source, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Announcement observation ended with discovery shutdown.
        }
        catch (Exception exception) { logger.LogDebug(exception, "Could not register with announced LocalSend peer {Address}", source); }
        finally { _registrations.TryRemove(source, out _); }
    }

    private async Task SendOnAllInterfacesAsync(byte[] payload, CancellationToken cancellationToken)
    {
        foreach (var address in _addressProvider())
        {
            try
            {
                using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                sender.Bind(new IPEndPoint(address, 0));
                await sender.SendToAsync(payload, SocketFlags.None, new IPEndPoint(options.MulticastAddress, options.Port), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or NetworkInformationException)
            {
                logger.LogDebug(exception, "Could not announce LocalSend on {Address}", address);
            }
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs eventArgs) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (_stop.IsCancellationRequested || Interlocked.Exchange(ref _refreshScheduled, 1) != 0)
            return;
        _ = RefreshAfterNetworkChangeAsync();
    }

    private async Task RefreshAfterNetworkChangeAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), _stop.Token).ConfigureAwait(false);
            await RefreshInterfacesAsync(force: true, _stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // A pending recovery was cancelled during shutdown.
        }
        catch (Exception exception) { logger.LogWarning(exception, "LocalSend discovery network recovery failed"); }
        finally { Interlocked.Exchange(ref _refreshScheduled, 0); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        await _stop.CancelAsync().ConfigureAwait(false);
        await _receiverGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_receiverStop is not null)
                await _receiverStop.CancelAsync().ConfigureAwait(false);
            _receiver?.Dispose();
            if (_receiveLoop is not null)
            {
                try { await _receiveLoop.ConfigureAwait(false); }
                catch (ObjectDisposedException)
                {
                    // Disposing discovery closes the receiver before its loop completes.
                }
            }
            _receiverStop?.Dispose();
        }
        finally { _receiverGate.Release(); }
        await _announceGate.WaitAsync().ConfigureAwait(false);
        _announceGate.Release();
        _stop.Dispose();
        _announceGate.Dispose();
        _receiverGate.Dispose();
    }
}
