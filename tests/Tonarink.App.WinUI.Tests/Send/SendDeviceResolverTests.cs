using Tonarink.Services.Devices;
using System.Net;
using System.Net.Sockets;
using LocalSendDotNet;
using Tonarink.TestSupport;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
public sealed class SendDeviceResolverTests
{
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(" 192.168.1.2 ", "192.168.1.2", 53317, "192.168.1.2")]
    [DataRow("192.168.1.2:1", "192.168.1.2", 1, "192.168.1.2:1")]
    [DataRow("192.168.1.2:53317", "192.168.1.2", 53317, "192.168.1.2")]
    [DataRow("::1", "::1", 53317, "[::1]")]
    [DataRow("[::1]:65535", "::1", 65535, "[::1]:65535")]
    [DataRow("[fe80::1%7]:1234", "fe80::1%7", 1234, "[fe80::1%7]:1234")]
    public void LiteralAddressesPreservePortScopeAndRoundTripWithoutDns(string input, string expectedAddress, int expectedPort, string formatted)
    {
        Assert.IsTrue(SendDeviceResolver.TryParseAddress(input, out var address, out var port));
        Assert.AreEqual(IPAddress.Parse(expectedAddress), address);
        Assert.AreEqual(expectedPort, port);
        Assert.AreEqual(formatted, SendDeviceResolver.FormatAddress(address, port));
        Assert.IsTrue(SendDeviceResolver.TryParseAddress(formatted, out var roundTrip, out var roundTripPort));
        Assert.AreEqual(address, roundTrip);
        Assert.AreEqual(port, roundTripPort);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("localhost:53317")]
    [DataRow("http://127.0.0.1:53317")]
    [DataRow("127.0.0.1:0")]
    [DataRow("127.0.0.1:65536")]
    [DataRow("127.0.0.1:")]
    [DataRow("[::1]:0")]
    [DataRow("[::1]:65536")]
    [DataRow("[::1]:")]
    [DataRow("127.0.0.1:53317/path")]
    [DataRow("127.0.0.1:53317?query")]
    [DataRow("127.0.0.1:53317#fragment")]
    [DataRow("user@127.0.0.1:53317")]
    [DataRow("[::1]:53317/path")]
    public void NonLiteralOrDecoratedEndpointsAreRejectedRatherThanSilentlyDiscarded(string input)
    {
        Assert.IsFalse(SendDeviceResolver.TryParseAddress(input, out var address, out var port), input);
        Assert.AreEqual(IPAddress.None, address);
        Assert.AreEqual(LocalSendOptions.DefaultPort, port);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public async Task ResolvingRealPeerUsesWorkingProtocolAndRegistersVerifiedFingerprint(bool peerHttps, bool preferHttps)
    {
        await using var peers = await Peers.CreateAsync(peerHttps);
        Assert.IsEmpty(peers.Sender.GetDevices());
        var resolution = await SendDeviceResolver.ResolveAsync(peers.Sender, IPAddress.Loopback, peers.Port,
            preferHttps ? LocalSendProtocol.Https : LocalSendProtocol.Http, peers.Receiver.Identity!.Fingerprint)
            .WaitAsync(TimeSpan.FromSeconds(10));
        var device = resolution switch
        {
            ResolvedDevice resolved => resolved.Device,
            DeviceResolutionFailure failed => throw new AssertFailedException(failed.Cause.ToString())
        };
        Assert.AreEqual(peers.Receiver.Identity.Fingerprint, device.Fingerprint);
        Assert.AreEqual("Resolver peer", device.Alias);
        var endpoint = Assert.ContainsSingle(device.Endpoints);
        Assert.AreEqual(IPAddress.Loopback, endpoint.Address);
        Assert.AreEqual(peers.Port, endpoint.Port);
        Assert.AreEqual(peerHttps ? LocalSendProtocol.Https : LocalSendProtocol.Http, endpoint.Protocol);
        Assert.AreEqual(device.Fingerprint, Assert.ContainsSingle(peers.Sender.GetDevices()).Fingerprint);
        Assert.IsTrue(peers.Sender.RemoveDevice(device.Fingerprint));
        Assert.IsEmpty(peers.Sender.GetDevices());
    }

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow(true)]
    [DataRow(false)]
    public async Task WrongSavedIdentityNeverRegistersPeerAndCorrectIdentityCanRecover(bool peerHttps)
    {
        await using var peers = await Peers.CreateAsync(peerHttps);
        var protocol = peerHttps ? LocalSendProtocol.Https : LocalSendProtocol.Http;
        var rejected = await SendDeviceResolver.ResolveAsync(peers.Sender, IPAddress.Loopback, peers.Port,
            protocol, peers.Sender.Identity!.Fingerprint).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(rejected is DeviceResolutionFailure);
        Assert.IsEmpty(peers.Sender.GetDevices(), "A probe must not turn an untrusted identity into a known device.");
        Assert.IsEmpty(peers.Receiver.GetDevices(), "Identity rejection must happen before registering with the peer.");
        var recovered = await SendDeviceResolver.ResolveAsync(peers.Sender, IPAddress.Loopback, peers.Port,
            protocol, peers.Receiver.Identity!.Fingerprint).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(recovered is ResolvedDevice);
        Assert.AreEqual(peers.Receiver.Identity.Fingerprint, Assert.ContainsSingle(peers.Sender.GetDevices()).Fingerprint);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task UnreachableEndpointReturnsFailureWithoutChangingExistingTrustedPeer()
    {
        await using var peers = await Peers.CreateAsync(true);
        var trusted = await peers.Sender.AddKnownDeviceAsync(new(IPAddress.Loopback, peers.Port, LocalSendProtocol.Https),
            peers.Receiver.Identity!.Fingerprint);
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var unreachable = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var result = await SendDeviceResolver.ResolveAsync(peers.Sender, IPAddress.Loopback, unreachable,
            LocalSendProtocol.Https, null).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(result is DeviceResolutionFailure);
        var retained = Assert.ContainsSingle(peers.Sender.GetDevices());
        Assert.AreEqual(trusted.Fingerprint, retained.Fingerprint);
        Assert.AreSequenceEqual(trusted.Endpoints, retained.Endpoints);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task RequestDeadlineTriesBothProtocolsAndReturnsFailureWithoutRegisteringPeer()
    {
        await using var peers = await Peers.CreateAsync(true);
        await using var stalled = new StalledPeer();
        var result = await SendDeviceResolver.ResolveAsync(peers.Sender, IPAddress.Loopback, stalled.Port,
            LocalSendProtocol.Https, null).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(result is DeviceResolutionFailure, "A peer that never replies must not resolve.");
        Assert.AreEqual(2, stalled.Connections, "A request deadline must allow the alternate protocol to be attempted.");
        Assert.IsEmpty(peers.Sender.GetDevices());
        Assert.IsTrue(await SendDeviceResolver.ResolveAsync(peers.Sender, IPAddress.Loopback, peers.Port,
            LocalSendProtocol.Https, peers.Receiver.Identity!.Fingerprint).WaitAsync(TimeSpan.FromSeconds(10)) is ResolvedDevice,
            "The same sender must remain usable after both request deadlines expire.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task AlreadyCancelledCallerDoesNotOpenAConnection()
    {
        await using var peers = await Peers.CreateAsync(true);
        await using var stalled = new StalledPeer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => SendDeviceResolver.ResolveAsync(
            peers.Sender, IPAddress.Loopback, stalled.Port, LocalSendProtocol.Https, null, cancellation.Token));
        Assert.AreEqual(0, stalled.Connections);
        Assert.IsEmpty(peers.Sender.GetDevices());
    }

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CancellingConnectedProbeDoesNotFallBackOrRegisterPeer(bool preferHttps)
    {
        await using var peers = await Peers.CreateAsync(true);
        await using var stalled = new StalledPeer();
        using var cancellation = new CancellationTokenSource();
        var resolve = SendDeviceResolver.ResolveAsync(peers.Sender, IPAddress.Loopback, stalled.Port,
            preferHttps ? LocalSendProtocol.Https : LocalSendProtocol.Http, null, cancellation.Token);
        await stalled.Connected.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => resolve.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, stalled.Connections, "Caller cancellation must not open an alternate protocol connection.");
        Assert.IsEmpty(peers.Sender.GetDevices());
        Assert.IsTrue(await SendDeviceResolver.ResolveAsync(peers.Sender, IPAddress.Loopback, peers.Port,
            LocalSendProtocol.Https, peers.Receiver.Identity!.Fingerprint).WaitAsync(TimeSpan.FromSeconds(10)) is ResolvedDevice);
    }

    // Accept real TCP connections but deliberately never send HTTP/TLS data.
    // This makes request timeout and caller cancellation independently observable.
    private sealed class StalledPeer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly List<TcpClient> _clients = [];
        private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _accept;
        private int _connections;
        public int Port { get; }
        public int Connections => Volatile.Read(ref _connections);
        public Task Connected => _connected.Task;

        public StalledPeer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accept = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                    _clients.Add(client);
                    Interlocked.Increment(ref _connections);
                    _connected.TrySetResult();
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (SocketException) when (_lifetime.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            _listener.Stop();
            try { await _accept.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally
            {
                foreach (var client in _clients) client.Dispose();
                _lifetime.Dispose();
            }
        }
    }

    private sealed class Peers : IAsyncDisposable
    {
        private readonly TemporaryDirectory _directory = new();
        public LocalSendNode Sender { get; }
        public LocalSendNode Receiver { get; }
        public int Port { get; }

        private Peers(bool peerHttps)
        {
            // Keep the first port reserved while choosing the second so the
            // fixture cannot accidentally assign both nodes the same port.
            using var senderPort = new TcpListener(IPAddress.Loopback, 0);
            senderPort.Start();
            using var receiverPort = new TcpListener(IPAddress.Loopback, 0);
            receiverPort.Start();
            Port = ((IPEndPoint)receiverPort.LocalEndpoint).Port;
            Sender = Create("sender", "Resolver sender", ((IPEndPoint)senderPort.LocalEndpoint).Port, true);
            Receiver = Create("receiver", "Resolver peer", Port, peerHttps);
        }

        private LocalSendNode Create(string name, string alias, int port, bool https) => new(new LocalSendOptions
        {
            Alias = alias,
            Port = port,
            EnableHttps = https,
            NetworkWhitelist = [],
            DataDirectory = Path.Combine(_directory.Path, name, "identity"),
            DownloadDirectory = Path.Combine(_directory.Path, name, "downloads"),
            RequestTimeout = TimeSpan.FromSeconds(2)
        });

        public static async Task<Peers> CreateAsync(bool peerHttps)
        {
            var peers = new Peers(peerHttps);
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await Task.WhenAll(peers.Sender.StartAsync(deadline.Token), peers.Receiver.StartAsync(deadline.Token));
                return peers;
            }
            catch
            {
                await peers.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { await Sender.DisposeAsync(); }
            finally
            {
                try { await Receiver.DisposeAsync(); }
                finally { await _directory.DisposeAsync(); }
            }
        }
    }
}
