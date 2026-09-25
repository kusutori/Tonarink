using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LocalSendDotNet.Core.Tests;

public sealed class NodeIntegrationTests
{
    [Fact(Timeout = 30_000)]
    public async Task TwoNodesTransferTextOverMutualTls()
    {
        var root = TestDirectory.Create();
        var senderData = Path.Combine(root, "sender");
        var receiverData = Path.Combine(root, "receiver");
        var downloads = Path.Combine(root, "downloads");
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        await using var sender = CreateNode("Sender", senderData, Path.Combine(root, "sender-downloads"), senderPort);
        await using var receiver = CreateNode("Receiver", receiverData, downloads, receiverPort);
        try
        {
            await Task.WhenAll(receiver.StartAsync(), sender.StartAsync());
            var requestTask = NextRequestAsync(receiver);
            await Task.Delay(100);

            var fingerprint = await ReadFingerprintAsync(receiverData);
            var device = new LocalSendDevice("Receiver", "2.2", null, LocalSendDeviceType.Desktop, fingerprint, false,
                [new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https)], DateTimeOffset.UtcNow);
            var sendTask = sender.SendAsync(device, [new SendTextItem("hello from dotnet")], new SendOptions { ComputeSha256 = true });
            var request = await requestTask;
            var incomingItem = Assert.Single(request.Items);
            Assert.Equal("text/plain", incomingItem.ContentType);
            Assert.Equal("hello from dotnet", incomingItem.Preview);
            var received = await receiver.AcceptAsync(request.RequestId);
            var sent = await sendTask;

            _ = sent.RequireCompleted();
            var receivedCompleted = received.RequireCompleted();
            Assert.Null(Assert.Single(receivedCompleted.Items).SavedPath);
            Assert.Empty(Directory.EnumerateFiles(downloads));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact(Timeout = 30_000)]
    public async Task KnownDeviceCanBeAddedAndWaitingTransferCanBeCancelledById()
    {
        var root = TestDirectory.Create();
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        await using var sender = CreateNode("Sender", Path.Combine(root, "sender"), Path.Combine(root, "sender-downloads"), senderPort);
        await using var receiver = CreateNode("Receiver", Path.Combine(root, "receiver"), Path.Combine(root, "downloads"), receiverPort);
        try
        {
            await Task.WhenAll(receiver.StartAsync(), sender.StartAsync());
            Assert.Equal(LocalSendNodeState.Running, sender.State);
            var fingerprint = receiver.Identity!.Fingerprint;
            var endpoint = new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https);
            var probe = await sender.ProbeDeviceAsync(endpoint);
            Assert.True(probe.IdentityVerified);
            Assert.Equal(fingerprint, probe.Device.Fingerprint);
            var device = await sender.AddKnownDeviceAsync(endpoint, fingerprint);
            Assert.Equal("Receiver", device.Alias);

            var progress = new InlineProgress<TransferProgress>();
            var requestTask = NextRequestAsync(receiver);
            var sendTask = sender.SendAsync(device, [new SendTextItem("cancel me")], progress: progress);
            var request = await requestTask;
            Assert.NotEqual(Guid.Empty, request.TransferId);
            Assert.True(await sender.CancelTransferAsync(progress.Last!.TransferId));
            var result = await sendTask;
            _ = result.RequireCancelled();

            await sender.StopAsync();
            Assert.Equal(LocalSendNodeState.Stopped, sender.State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact(Timeout = 60_000)]
    public async Task LargeVirtualStreamTransfersWithoutMaterializingSource()
    {
        const long length = 32L * 1024 * 1024;
        var root = TestDirectory.Create();
        var senderData = Path.Combine(root, "sender");
        var receiverData = Path.Combine(root, "receiver");
        var downloads = Path.Combine(root, "downloads");
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        await using var sender = CreateNode("Sender", senderData, Path.Combine(root, "sender-downloads"), senderPort);
        await using var receiver = CreateNode("Receiver", receiverData, downloads, receiverPort);
        try
        {
            await Task.WhenAll(receiver.StartAsync(), sender.StartAsync());
            var receiveTask = AcceptNextAsync(receiver);
            var device = new LocalSendDevice("Receiver", "2.2", null, LocalSendDeviceType.Desktop, receiver.Identity!.Fingerprint, false,
                [new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https)], DateTimeOffset.UtcNow);
            var item = new SendStreamItem("large.bin", length,
                _ => ValueTask.FromResult<Stream>(new GeneratedStream(length)));

            var sent = await sender.SendAsync(device, [item]);
            var received = await receiveTask;

            _ = sent.RequireCompleted();
            _ = received.RequireCompleted();
            Assert.Equal(length, new FileInfo(Path.Combine(downloads, "large.bin")).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact(Timeout = 30_000)]
    public async Task PinAndPartialAcceptanceAreEnforced()
    {
        var root = TestDirectory.Create();
        var senderData = Path.Combine(root, "sender");
        var receiverData = Path.Combine(root, "receiver");
        var downloads = Path.Combine(root, "downloads");
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        await using var sender = CreateNode("Sender", senderData, Path.Combine(root, "sender-downloads"), senderPort);
        await using var receiver = CreateNode("Receiver", receiverData, downloads, receiverPort, "2468");
        try
        {
            await Task.WhenAll(receiver.StartAsync(), sender.StartAsync());
            var fingerprint = await ReadFingerprintAsync(receiverData);
            var device = new LocalSendDevice("Receiver", "2.2", null, LocalSendDeviceType.Desktop, fingerprint, false,
                [new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https)], DateTimeOffset.UtcNow);
            var items = new SendItem[] { new SendTextItem("one", "one.txt"), new SendTextItem("two", "two.txt") };

            var missing = (await sender.SendAsync(device, items)).RequirePinRequired();
            Assert.False(missing.InvalidPin);
            var wrong = (await sender.SendAsync(device, items, new SendOptions { Pin = "0000" })).RequirePinRequired();
            Assert.True(wrong.InvalidPin);

            var receiveTask = AcceptNextAsync(receiver, request => [request.Items.Single(static x => x.FileName == "one.txt").Id]);
            await Task.Delay(100);
            var sent = await sender.SendAsync(device, items, new SendOptions { Pin = "2468" });
            var received = await receiveTask;

            var sentCompleted = sent.RequireCompleted();
            var receivedCompleted = received.RequireCompleted();
            Assert.Single(sentCompleted.Items);
            Assert.Equal("one.txt", Assert.Single(receivedCompleted.Items).FileName);
            Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(downloads, "one.txt")));
            Assert.False(File.Exists(Path.Combine(downloads, "two.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static LocalSendNode CreateNode(string alias, string data, string downloads, int port, string? receivePin = null) => new(new LocalSendOptions
    {
        Alias = alias,
        DataDirectory = data,
        DownloadDirectory = downloads,
        Port = port,
        RequestTimeout = TimeSpan.FromSeconds(5),
        IncomingDecisionTimeout = TimeSpan.FromSeconds(5),
        ReceivePin = receivePin
    });

    private static async Task<ReceiveOutcome> AcceptNextAsync(LocalSendNode node)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync())
            return await node.AcceptAsync(request.RequestId);
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private static async Task<IncomingTransferRequest> NextRequestAsync(LocalSendNode node)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync())
            return request;
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private static async Task<ReceiveOutcome> AcceptNextAsync(LocalSendNode node, Func<IncomingTransferRequest, IReadOnlyCollection<string>> select)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync())
            return await node.AcceptAsync(request.RequestId, new AcceptTransferOptions { AcceptedItemIds = select(request) });
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private static async Task<string> ReadFingerprintAsync(string dataDirectory)
    {
        using var identity = await DeviceIdentityStore.LoadOrCreateAsync(dataDirectory, default);
        return identity.Fingerprint;
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

internal sealed class InlineProgress<T> : IProgress<T>
{
    public T? Last { get; private set; }
    public void Report(T value) => Last = value;
}

internal sealed class GeneratedStream(long length) : Stream
{
    private long _position;
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = (int)Math.Min(count, length - _position);
        if (read <= 0) return 0;
        Array.Clear(buffer, offset, read);
        _position += read;
        return read;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = (int)Math.Min(buffer.Length, length - _position);
        if (read <= 0) return ValueTask.FromResult(0);
        buffer.Span[..read].Clear();
        _position += read;
        return ValueTask.FromResult(read);
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
