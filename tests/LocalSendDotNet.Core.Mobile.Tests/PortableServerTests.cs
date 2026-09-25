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
            _ = RequireCompleted(sent);
            var received = await receiveTask;
            var item = Assert.Single(RequireCompleted(received).Items);
            Assert.Equal("message.txt", item.FileName);
            Assert.Null(item.SavedPath);
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
            Assert.True((await sender.SendAsync(device, [new SendTextItem("secret")])) is SendOutcome.PinRequired);
            var receiveTask = AcceptNextAsync(receiver);
            var sent = await sender.SendAsync(device, [new SendTextItem("secret")], new SendOptions { Pin = "2468" });
            _ = RequireCompleted(sent);
            _ = RequireCompleted(await receiveTask);
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
            _ = RequireCompleted(sent);
            _ = RequireCompleted(await receiveTask);
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

    private static async Task<ReceiveOutcome> AcceptNextAsync(LocalSendNode node)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync())
            return await node.AcceptAsync(request.RequestId);
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private static async Task<ReceiveOutcome> AcceptNamedAsync(LocalSendNode node, string fileName)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync())
        {
            var acceptedId = Assert.Single(request.Items, item => item.FileName == fileName).Id;
            return await node.AcceptAsync(request.RequestId, new AcceptTransferOptions { AcceptedItemIds = [acceptedId] });
        }
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private static SendOutcome.Completed RequireCompleted(SendOutcome outcome) => outcome switch
    {
        SendOutcome.Completed completed => completed,
        _ => throw new Xunit.Sdk.XunitException("Expected completed send outcome."),
    };

    private static ReceiveOutcome.Completed RequireCompleted(ReceiveOutcome outcome) => outcome switch
    {
        ReceiveOutcome.Completed completed => completed,
        _ => throw new Xunit.Sdk.XunitException("Expected completed receive outcome."),
    };

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
