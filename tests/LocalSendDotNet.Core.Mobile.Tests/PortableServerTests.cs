using System.Net;
using System.Net.Sockets;
using LocalSendDotNet;

namespace LocalSendDotNet.Core.Mobile.Tests;

public sealed class PortableServerTests
{
    [Fact(Timeout = 30_000)]
    public async Task TwoPortableNodesTransferTextOverMutualTls()
    {
        var root = CreateTemporaryDirectory();
        var downloads = Path.Combine(root, "downloads");
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        await using var sender = CreateNode("Sender", Path.Combine(root, "sender"), Path.Combine(root, "sender-downloads"), senderPort);
        await using var receiver = CreateNode("Receiver", Path.Combine(root, "receiver"), downloads, receiverPort);
        try
        {
            await Task.WhenAll(receiver.StartAsync(), sender.StartAsync());
            var receiveTask = AcceptNextAsync(receiver);
            var device = new LocalSendDevice("Receiver", "2.2", null, LocalSendDeviceType.Mobile,
                receiver.Identity!.Fingerprint, false,
                [new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https)], DateTimeOffset.UtcNow);

            var sent = await sender.SendAsync(device, [new SendTextItem("portable hello")]);
            Assert.True(sent.IsSuccess, sent.Failure?.Message);
            var received = await receiveTask;
            Assert.True(received.IsSuccess, received.Failure?.Message);
            Assert.Equal("portable hello", await File.ReadAllTextAsync(Path.Combine(downloads, "message.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task InfoEndpointAllowsBrowserWithoutClientCertificate()
    {
        var root = CreateTemporaryDirectory();
        var port = GetFreePort();
        await using var node = CreateNode("Portable", Path.Combine(root, "data"), Path.Combine(root, "downloads"), port);
        try
        {
            await node.StartAsync();
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };
            using var client = new HttpClient(handler);
            var json = await client.GetStringAsync($"https://127.0.0.1:{port}/api/localsend/v2/info");
            Assert.Contains("Portable", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task PortableServerSupportsPinProtectedTransfer()
    {
        var root = CreateTemporaryDirectory();
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        await using var sender = CreateNode("Sender", Path.Combine(root, "sender"), Path.Combine(root, "sender-downloads"), senderPort);
        await using var receiver = CreateNode("Receiver", Path.Combine(root, "receiver"), Path.Combine(root, "downloads"), receiverPort, "2468");
        try
        {
            await Task.WhenAll(receiver.StartAsync(), sender.StartAsync());
            var device = DeviceFor(receiver, receiverPort);
            await Assert.ThrowsAsync<PinRequiredException>(() => sender.SendAsync(device, [new SendTextItem("secret")]));
            var receiveTask = AcceptNextAsync(receiver);
            var sent = await sender.SendAsync(device, [new SendTextItem("secret")], new SendOptions { Pin = "2468" });
            Assert.True(sent.IsSuccess, sent.Failure?.Message);
            Assert.True((await receiveTask).IsSuccess);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task PortableServerSupportsPartialAcceptance()
    {
        var root = CreateTemporaryDirectory();
        var downloads = Path.Combine(root, "downloads");
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        await using var sender = CreateNode("Sender", Path.Combine(root, "sender"), Path.Combine(root, "sender-downloads"), senderPort);
        await using var receiver = CreateNode("Receiver", Path.Combine(root, "receiver"), downloads, receiverPort);
        try
        {
            await Task.WhenAll(receiver.StartAsync(), sender.StartAsync());
            var receiveTask = AcceptNamedAsync(receiver, "keep.txt");
            var sent = await sender.SendAsync(DeviceFor(receiver, receiverPort),
                [new SendTextItem("keep", "keep.txt"), new SendTextItem("skip", "skip.txt")]);
            Assert.True(sent.IsSuccess, sent.Failure?.Message);
            Assert.True((await receiveTask).IsSuccess);
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(downloads, "keep.txt")));
            Assert.False(File.Exists(Path.Combine(downloads, "skip.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static LocalSendNode CreateNode(string alias, string data, string downloads, int port, string? pin = null) => new(new LocalSendOptions
    {
        Alias = alias,
        DataDirectory = data,
        DownloadDirectory = downloads,
        Port = port,
        RequestTimeout = TimeSpan.FromSeconds(5),
        IncomingDecisionTimeout = TimeSpan.FromSeconds(5),
        ReceivePin = pin,
    });

    private static LocalSendDevice DeviceFor(LocalSendNode node, int port) => new("Receiver", "2.2", null, LocalSendDeviceType.Mobile,
        node.Identity!.Fingerprint, false, [new DeviceEndpoint(IPAddress.Loopback, port, LocalSendProtocol.Https)], DateTimeOffset.UtcNow);

    private static async Task<TransferResult> AcceptNextAsync(LocalSendNode node)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync())
            return await node.AcceptAsync(request.RequestId);
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private static async Task<TransferResult> AcceptNamedAsync(LocalSendNode node, string fileName)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync())
        {
            var acceptedId = Assert.Single(request.Items, item => item.FileName == fileName).Id;
            return await node.AcceptAsync(request.RequestId, new AcceptTransferOptions { AcceptedItemIds = [acceptedId] });
        }
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "LocalSendDotNet-MobileTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
