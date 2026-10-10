using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
public sealed class NodeIntegrationTests
{
    [TestMethod]
    [Timeout(30_000)]
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
            var incomingItem = Assert.ContainsSingle(request.Items);
            Assert.AreEqual("text/plain", incomingItem.ContentType);
            Assert.AreEqual("hello from dotnet", incomingItem.Preview);
            var received = await receiver.AcceptAsync(request.RequestId);
            var sent = await sendTask;

            _ = sent.RequireCompleted();
            var receivedCompleted = received.RequireCompleted();
            Assert.IsNull(Assert.ContainsSingle(receivedCompleted.Items).SavedPath);
            Assert.IsEmpty(Directory.EnumerateFiles(downloads));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [Timeout(30_000)]
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
            Assert.AreEqual(LocalSendNodeState.Running, sender.State);
            var fingerprint = receiver.Identity!.Fingerprint;
            var endpoint = new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https);
            var probe = await sender.ProbeDeviceAsync(endpoint);
            Assert.IsTrue(probe.IdentityVerified);
            Assert.AreEqual(fingerprint, probe.Device.Fingerprint);
            var device = await sender.AddKnownDeviceAsync(endpoint, fingerprint);
            Assert.AreEqual("Receiver", device.Alias);

            var progress = new InlineProgress<TransferProgress>();
            var requestTask = NextRequestAsync(receiver);
            var sendTask = sender.SendAsync(device, [new SendTextItem("cancel me")], progress: progress);
            var request = await requestTask;
            Assert.AreNotEqual(Guid.Empty, request.TransferId);
            Assert.IsTrue(await sender.CancelTransferAsync(progress.Last!.TransferId));
            var result = await sendTask;
            _ = result.RequireCancelled();

            await sender.StopAsync();
            Assert.AreEqual(LocalSendNodeState.Stopped, sender.State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [Timeout(60_000)]
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
            Assert.AreEqual(length, new FileInfo(Path.Combine(downloads, "large.bin")).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [Timeout(30_000)]
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
            Assert.IsFalse(missing.InvalidPin);
            var wrong = (await sender.SendAsync(device, items, new SendOptions { Pin = "0000" })).RequirePinRequired();
            Assert.IsTrue(wrong.InvalidPin);

            var receiveTask = AcceptNextAsync(receiver, request => [request.Items.Single(static x => x.FileName == "one.txt").Id]);
            await Task.Delay(100);
            var sent = await sender.SendAsync(device, items, new SendOptions { Pin = "2468" });
            var received = await receiveTask;

            var sentCompleted = sent.RequireCompleted();
            var receivedCompleted = received.RequireCompleted();
            Assert.ContainsSingle(sentCompleted.Items);
            Assert.AreEqual("one.txt", Assert.ContainsSingle(receivedCompleted.Items).FileName);
            Assert.AreEqual("one", await File.ReadAllTextAsync(Path.Combine(downloads, "one.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(downloads, "two.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod, Timeout(30_000)]
    [DataRow("sender")]
    [DataRow("receiver")]
    public async Task CallerCancellationDuringUploadNotifiesPeerAndReleasesSource(string side)
    {
        const int length = 1024 * 1024;
        var root = TestDirectory.Create();
        var downloads = Path.Combine(root, "downloads");
        var receiverPort = GetFreePort();
        await using var sender = CreateNode("Sender", Path.Combine(root, "sender"), Path.Combine(root, "out"), GetFreePort());
        await using var receiver = CreateNode("Receiver", Path.Combine(root, "receiver"), downloads, receiverPort);
        using var senderCancellation = new CancellationTokenSource();
        using var receiverCancellation = new CancellationTokenSource();
        using var watchCancellation = new CancellationTokenSource();
        using var source = new PausedSource(length);
        var sendingProgress = new TaskCompletionSource<TransferProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivingProgress = new TaskCompletionSource<TransferProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<SendOutcome>? sending = null;
        Task<ReceiveOutcome>? receiving = null;
        Task<IncomingTransferRequest>? requestTask = null;
        Task<ReceiveOutcome>? recoveryReceive = null;
        try
        {
            await Task.WhenAll(sender.StartAsync(), receiver.StartAsync());
            var device = await sender.AddKnownDeviceAsync(new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https), receiver.Identity!.Fingerprint);
            requestTask = NextRequestAsync(receiver, watchCancellation.Token);
            sending = sender.SendAsync(device,
                [new SendStreamItem("cancelled.bin", length, _ => ValueTask.FromResult<Stream>(source))],
                progress: new ActiveProgress(sendingProgress), cancellationToken: senderCancellation.Token);
            var request = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
            receiving = receiver.AcceptAsync(request.RequestId, progress: new ActiveProgress(receivingProgress), cancellationToken: receiverCancellation.Token);
            var sentProgress = await sendingProgress.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var receivedProgress = await receivingProgress.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await source.Paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(TransferDirection.Send, sentProgress.Direction);
            Assert.AreEqual(TransferDirection.Receive, receivedProgress.Direction);
            Assert.IsTrue(sentProgress.BytesTransferred < length && receivedProgress.BytesTransferred < length);
            Assert.HasCount(1, Directory.EnumerateFiles(downloads, "*.part-*", SearchOption.AllDirectories).ToArray());

            if (side == "sender")
            {
                await senderCancellation.CancelAsync();
                await Assert.ThrowsAsync<OperationCanceledException>(() => sending.WaitAsync(TimeSpan.FromSeconds(5)));
                var cancelledReceive = await receiving.WaitAsync(TimeSpan.FromSeconds(5));
                var cancelledItems = cancelledReceive switch
                {
                    ReceiveOutcome.Cancelled cancelled => cancelled.Items,
                    ReceiveOutcome.Failed failed => throw new AssertFailedException($"Expected cancellation, got {failed.Failure}."),
                    _ => throw new AssertFailedException("Expected cancellation, got a completed receive.")
                };
                Assert.IsEmpty(cancelledItems);
            }
            else
            {
                await receiverCancellation.CancelAsync();
                await Assert.ThrowsAsync<OperationCanceledException>(() => receiving.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.IsEmpty((await sending.WaitAsync(TimeSpan.FromSeconds(5))).RequireCancelled().Items);
            }
            Assert.IsTrue(source.Disposed, "The upload source must be released before SendAsync finishes.");
            var cleanupDeadline = DateTime.UtcNow.AddSeconds(5);
            while (Directory.EnumerateFiles(downloads, "*", SearchOption.AllDirectories).Any() && DateTime.UtcNow < cleanupDeadline)
                await Task.Delay(20);
            Assert.IsEmpty(Directory.EnumerateFiles(downloads, "*", SearchOption.AllDirectories).ToArray());
            Assert.IsEmpty(receiver.GetPendingIncomingTransfers());
            Assert.AreEqual(LocalSendNodeState.Running, sender.State);
            Assert.AreEqual(LocalSendNodeState.Running, receiver.State);

            var payload = new byte[length];
            RandomNumberGenerator.Fill(payload);
            recoveryReceive = AcceptNextAsync(receiver, watchCancellation.Token);
            var recoverySend = await sender.SendAsync(device,
                [new SendStreamItem("recovered.bin", length, _ => ValueTask.FromResult<Stream>(new MemoryStream(payload, writable: false)))],
                new SendOptions { ComputeSha256 = true });
            _ = recoverySend.RequireCompleted();
            _ = (await recoveryReceive.WaitAsync(TimeSpan.FromSeconds(5))).RequireCompleted();
            CollectionAssert.AreEqual(SHA256.HashData(payload), SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(downloads, "recovered.bin"))));
        }
        finally
        {
            try
            {
                await watchCancellation.CancelAsync();
                await senderCancellation.CancelAsync();
                await receiverCancellation.CancelAsync();
                // Release the gate and observe every owned invocation even if
                // an assertion failed before acceptance or recovery completed.
                source.Release();
                var invocations = new Task?[] { sending, receiving, requestTask, recoveryReceive }.OfType<Task>();
                try { await Task.WhenAll(invocations).WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (OperationCanceledException) { }
            }
            finally
            {
                await Task.WhenAll(sender.StopAsync(), receiver.StopAsync());
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod, Timeout(30_000)]
    [DataRow("sender", false)]
    [DataRow("sender", true)]
    [DataRow("receiver", false)]
    [DataRow("receiver", true)]
    public async Task StoppingOrDisposingDuringUploadNotifiesPeerAndPreservesIdentity(string side, bool dispose)
    {
        const int length = 1024 * 1024;
        var root = TestDirectory.Create();
        var downloads = Path.Combine(root, "downloads");
        var senderData = Path.Combine(root, "sender");
        var receiverData = Path.Combine(root, "receiver");
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        using var diagnostics = LoggerFactory.Create(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug));
        await using var sender = CreateNode("Sender", senderData, Path.Combine(root, "out"), senderPort, loggerFactory: diagnostics);
        await using var receiver = CreateNode("Receiver", receiverData, downloads, receiverPort, loggerFactory: diagnostics);
        using var watchCancellation = new CancellationTokenSource();
        using var source = new PausedSource(length);
        var outgoingProgress = new TaskCompletionSource<TransferProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var incomingProgress = new TaskCompletionSource<TransferProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<SendOutcome>? sending = null, recoverySend = null;
        Task<ReceiveOutcome>? receiving = null, recoveryReceive = null;
        Task<IncomingTransferRequest>? offer = null;
        Task? shutdown = null;
        LocalSendNode? replacement = null;
        try
        {
            await Task.WhenAll(sender.StartAsync(), receiver.StartAsync());
            var originalFingerprint = (side == "sender" ? sender : receiver).Identity!.Fingerprint;
            var device = await sender.AddKnownDeviceAsync(
                new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https), receiver.Identity!.Fingerprint);
            offer = NextRequestAsync(receiver, watchCancellation.Token);
            sending = sender.SendAsync(device, [new SendStreamItem("interrupted.bin", length,
                _ => ValueTask.FromResult<Stream>(source))], progress: new ActiveProgress(outgoingProgress));
            var request = await offer.WaitAsync(TimeSpan.FromSeconds(5));
            receiving = receiver.AcceptAsync(request.RequestId, progress: new ActiveProgress(incomingProgress),
                cancellationToken: watchCancellation.Token);
            await source.Paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var sentProgress = await outgoingProgress.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var receivedProgress = await incomingProgress.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(length, sentProgress.TotalBytes);
            Assert.AreEqual(length, receivedProgress.TotalBytes);
            Assert.IsTrue(sentProgress.BytesTransferred < length && receivedProgress.BytesTransferred < length);
            Assert.HasCount(1, Directory.EnumerateFiles(downloads, "*.part-*", SearchOption.AllDirectories).ToArray());

            var affected = side == "sender" ? sender : receiver;
            shutdown = dispose ? affected.DisposeAsync().AsTask() : affected.StopAsync();
            try { await Task.WhenAll(shutdown, sending, receiving).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch
            {
                Console.WriteLine($"{side}, dispose={dispose}: shutdown={shutdown.Status}, sending={sending.Status}, receiving={receiving.Status}, sourceDisposed={source.Disposed}, sender={sender.State}, receiver={receiver.State}");
                throw;
            }
            Assert.IsEmpty((await sending).RequireCancelled().Items);
            var received = await receiving;
            Assert.IsTrue(received is ReceiveOutcome.Cancelled, received switch
            {
                ReceiveOutcome.Failed failed => $"Expected a cancelled receive, got {failed.Failure}.",
                _ => "Expected a cancelled receive, got a completed receive."
            });
            Assert.IsEmpty(received switch { ReceiveOutcome.Cancelled cancelled => cancelled.Items, _ => [] });
            Assert.AreEqual(dispose ? LocalSendNodeState.Disposed : LocalSendNodeState.Stopped, affected.State);
            Assert.AreEqual(LocalSendNodeState.Running, (side == "sender" ? receiver : sender).State);
            Assert.IsTrue(source.Disposed);
            Assert.IsEmpty(receiver.GetPendingIncomingTransfers());
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Directory.EnumerateFiles(downloads, "*", SearchOption.AllDirectories).Any() && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.IsEmpty(Directory.EnumerateFiles(downloads, "*", SearchOption.AllDirectories).ToArray());

            // A node is one-shot. Recreate only the stopped side using the same
            // identity directory and port, keeping the unaffected peer alive.
            replacement = side == "sender"
                ? CreateNode("Sender", senderData, Path.Combine(root, "out"), senderPort)
                : CreateNode("Receiver", receiverData, downloads, receiverPort);
            await replacement.StartAsync();
            Assert.AreEqual(originalFingerprint, replacement.Identity!.Fingerprint);
            var activeSender = side == "sender" ? replacement : sender;
            var activeReceiver = side == "receiver" ? replacement : receiver;
            var target = await activeSender.AddKnownDeviceAsync(
                new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https), activeReceiver.Identity!.Fingerprint);
            var bytes = RandomNumberGenerator.GetBytes(length);
            recoveryReceive = AcceptNextAsync(activeReceiver, watchCancellation.Token);
            recoverySend = activeSender.SendAsync(target, [new SendStreamItem("recovered.bin", length,
                _ => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)))], new SendOptions { ComputeSha256 = true });
            await Task.WhenAll(recoverySend, recoveryReceive).WaitAsync(TimeSpan.FromSeconds(5));
            _ = (await recoverySend).RequireCompleted();
            _ = (await recoveryReceive).RequireCompleted();
            CollectionAssert.AreEqual(SHA256.HashData(bytes), SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(downloads, "recovered.bin"))));
        }
        finally
        {
            try
            {
                await watchCancellation.CancelAsync();
                source.Release();
                await Task.WhenAll(sender.State == LocalSendNodeState.Disposed ? Task.CompletedTask : sender.StopAsync(),
                    receiver.State == LocalSendNodeState.Disposed ? Task.CompletedTask : receiver.StopAsync());
                if (replacement is not null) await replacement.StopAsync();
                var owned = new Task?[] { offer, sending, receiving, shutdown, recoverySend, recoveryReceive }.OfType<Task>();
                try { await Task.WhenAll(owned).WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (OperationCanceledException) { }
            }
            finally
            {
                if (replacement is not null) await replacement.DisposeAsync();
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class ActiveProgress(TaskCompletionSource<TransferProgress> target) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value)
        {
            if (value.State == TransferState.Transferring && value.BytesTransferred > 0)
                target.TrySetResult(value);
        }
    }

    private sealed class PausedSource(int length) : MemoryStream(new byte[length], writable: false)
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public void Release() => _release.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            const int boundary = 256 * 1024;
            if (Position >= boundary)
            {
                Paused.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, boundary)], cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static LocalSendNode CreateNode(string alias, string data, string downloads, int port, string? receivePin = null, ILoggerFactory? loggerFactory = null) => new(new LocalSendOptions
    {
        Alias = alias,
        DataDirectory = data,
        DownloadDirectory = downloads,
        Port = port,
        RequestTimeout = TimeSpan.FromSeconds(5),
        IncomingDecisionTimeout = TimeSpan.FromSeconds(5),
        ReceivePin = receivePin
    }, loggerFactory);

    private static async Task<ReceiveOutcome> AcceptNextAsync(LocalSendNode node, CancellationToken cancellationToken = default)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync(cancellationToken))
            return await node.AcceptAsync(request.RequestId, cancellationToken: cancellationToken);
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private static async Task<IncomingTransferRequest> NextRequestAsync(LocalSendNode node, CancellationToken cancellationToken = default)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync(cancellationToken))
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
