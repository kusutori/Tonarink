using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using LocalSendDotNet.Protocol.V2;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
public sealed class ReliabilityTests
{
    [TestMethod]
    public async Task ReliableBroadcastWaitsInsteadOfDroppingDecisionEvents()
    {
        var hub = new BroadcastHub<int>(1, dropOldest: false);
        await using var reader = hub.Subscribe(default).GetAsyncEnumerator();
        var firstRead = reader.MoveNextAsync().AsTask();
        hub.Publish(1);
        Assert.IsTrue(await firstRead);
        Assert.AreEqual(1, reader.Current);

        await hub.PublishAsync(2, default);
        var blocked = hub.PublishAsync(3, default);
        await Task.Delay(50);
        Assert.IsFalse(blocked.IsCompleted);
        Assert.IsTrue(await reader.MoveNextAsync());
        Assert.AreEqual(2, reader.Current);
        await blocked;
        Assert.IsTrue(await reader.MoveNextAsync());
        Assert.AreEqual(3, reader.Current);
    }

    [TestMethod]
    public async Task DeviceExpirationNotifiesSubscribersAndKeepsFreshPeers()
    {
        var hub = new BroadcastHub<DeviceChange>(8);
        var store = new DeviceStore(hub);
        var cutoff = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = hub.Subscribe(deadline.Token).GetAsyncEnumerator();
        var added = reader.MoveNextAsync().AsTask();
        store.Upsert(new("old", "2.2", null, LocalSendDeviceType.Desktop, "expired", false, [], cutoff.AddMinutes(-1)));
        Assert.IsTrue(await added);
        Assert.AreEqual(DeviceChangeKind.Added, reader.Current.Kind);
        store.Upsert(new("fresh", "2.2", null, LocalSendDeviceType.Desktop, "current", false, [], cutoff.AddMinutes(1)));
        Assert.IsTrue(await reader.MoveNextAsync());
        Assert.AreEqual("current", reader.Current.Device.Fingerprint);

        var removed = reader.MoveNextAsync().AsTask();
        Assert.AreEqual(1, store.RemoveExpired(cutoff));
        Assert.IsTrue(await removed);
        Assert.AreEqual(DeviceChangeKind.Removed, reader.Current.Kind);
        Assert.AreEqual("expired", reader.Current.Device.Fingerprint);
        Assert.AreEqual("current", Assert.ContainsSingle(store.Snapshot()).Fingerprint);
    }

    [TestMethod]
    public void ManuallyTrustedDeviceDoesNotExpire()
    {
        var store = new DeviceStore(new BroadcastHub<DeviceChange>(8));
        store.Upsert(new("manual", "2.2", null, LocalSendDeviceType.Desktop, "fingerprint", false, [],
            DateTimeOffset.UtcNow.AddDays(-1)), persistent: true);
        Assert.AreEqual(0, store.RemoveExpired(DateTimeOffset.UtcNow));
        Assert.ContainsSingle(store.Snapshot());
    }

    [TestMethod]
    public void DirectoryItemsPreserveRelativePathsAndInferContentTypes()
    {
        var root = TestDirectory.Create();
        try
        {
            var child = Path.Combine(root, "nested");
            Directory.CreateDirectory(child);
            File.WriteAllText(Path.Combine(child, "note.txt"), "hello");
            var item = Assert.ContainsSingle(LocalSendItems.FromDirectory(root));
            Assert.AreEqual("nested/note.txt", item.FileName);
            Assert.AreEqual("text/plain", item.ContentType);
            Assert.AreEqual("image/png", LocalSendContentTypes.GetForFileName("photo.PNG"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void OptionsRejectUnsafeLimits()
    {
        var root = TestDirectory.Create();
        try
        {
            var options = new LocalSendOptions
            {
                Alias = "test",
                DataDirectory = root,
                DownloadDirectory = root,
                MaxIncomingItemsPerTransfer = 0
            };
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(options.Validate);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void EndpointNormalizesIpv4MappedAddress()
    {
        var endpoint = new DeviceEndpoint(System.Net.IPAddress.Parse("::ffff:192.168.1.10"), 53317, LocalSendProtocol.Https);
        Assert.AreEqual(System.Net.IPAddress.Parse("192.168.1.10"), endpoint.Address);
    }

    [TestMethod]
    public async Task StreamItemSupportsUiOwnedStreams()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("virtual file");
        var item = new SendStreamItem("virtual.txt", bytes.Length,
            _ => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
        Assert.AreEqual("text/plain", item.ContentType);
        await using var stream = await item.OpenReadAsync(default);
        using var reader = new StreamReader(stream);
        Assert.AreEqual("virtual file", await reader.ReadToEndAsync());
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task DiscoveryMembershipTracksAvailableInterfacesAndRetainsTheLastUsableSet()
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(static nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(static nic => nic.GetIPProperties().UnicastAddresses)
            .Select(static item => item.Address)
            .Where(static address => address.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToArray();
        if (addresses.Length == 0)
            Assert.Inconclusive("No active IPv4 network interface is available for multicast rebinding.");
        var root = TestDirectory.Create();
        try
        {
            IReadOnlyList<IPAddress> supplied = [addresses[0]];
            var options = TestOptions(GetFreeUdpPort(), root);
            await using var discovery = new V2MulticastDiscovery(options, () => Announcement(options), (_, _, _) => Task.CompletedTask,
                NullLogger.Instance, () => supplied);
            await discovery.StartAsync(default);
            Assert.AreSequenceEqual(supplied, discovery.JoinedAddresses);
            supplied = [];
            Assert.IsFalse(await discovery.RefreshInterfacesAsync(force: true));
            Assert.IsNotEmpty(discovery.JoinedAddresses);
            supplied = addresses;
            Assert.IsTrue(await discovery.RefreshInterfacesAsync(force: true));
            Assert.AreSequenceEqual(addresses.OrderBy(static address => address.ToString(), StringComparer.Ordinal), discovery.JoinedAddresses);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task OccupiedUdpPortLeavesHttpServerRunning()
    {
        var root = TestDirectory.Create();
        using var blocker = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        blocker.ExclusiveAddressUse = true;
        blocker.Bind(new IPEndPoint(IPAddress.Any, 0));
        var port = ((IPEndPoint)blocker.LocalEndPoint!).Port;
        await using var node = new LocalSendNode(TestOptions(port, root));
        try
        {
            await node.StartAsync();
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            Assert.IsNotNull(node.Identity);
            Assert.IsNotNull(node.DiscoveryError);
            await AssertHttpInfoAsync(node, port);
            await node.StopAsync();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task OccupiedTcpPortProducesActionableException()
    {
        var root = TestDirectory.Create();
        var listener = new TcpListener(IPAddress.IPv6Any, 0);
        listener.Server.DualMode = true;
        listener.Server.ExclusiveAddressUse = true;
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var node = new LocalSendNode(TestOptions(port, root));
        try
        {
            var exception = await Assert.ThrowsExactlyAsync<PortUnavailableException>(() => node.StartAsync());
            Assert.AreEqual(port, exception.Port);
            Assert.AreEqual(LocalSendNodeState.Faulted, node.State);
            listener.Stop();
            await node.StartAsync();
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            await AssertHttpInfoAsync(node, port);
            await node.StopAsync();
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task CancelledStartRequestCanBeRetried()
    {
        var root = TestDirectory.Create();
        await using var node = new LocalSendNode(TestOptions(GetFreeTcpPort(), root));
        try
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => node.StartAsync(cancellation.Token));
            Assert.AreEqual(LocalSendNodeState.Created, node.State);
            await node.StartAsync();
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            await node.StopAsync();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task AssertHttpInfoAsync(LocalSendNode node, int port)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && DeviceIdentityStore.ValidatePeerCertificate(certificate, node.Identity!.Fingerprint)
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync($"https://127.0.0.1:{port}/api/localsend/v2/info");
        response.EnsureSuccessStatusCode();
        using var info = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("test", info.RootElement.GetProperty("alias").GetString());
        Assert.AreEqual(node.Identity!.Fingerprint, info.RootElement.GetProperty("fingerprint").GetString());
    }

    private static int GetFreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static LocalSendOptions TestOptions(int port, string? root = null)
    {
        root ??= TestDirectory.Create();
        return new LocalSendOptions { Alias = "test", DataDirectory = Path.Combine(root, "data"), DownloadDirectory = Path.Combine(root, "downloads"), Port = port };
    }

    private static DeviceInfoDto Announcement(LocalSendOptions options) => new()
    {
        Alias = options.Alias,
        Version = "2.2",
        Fingerprint = new string('A', 64),
        Port = options.Port,
        Protocol = "https"
    };
}
