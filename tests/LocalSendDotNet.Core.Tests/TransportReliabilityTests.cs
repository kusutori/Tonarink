using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LocalSendDotNet.Protocol.V2;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
public sealed class TransportReliabilityTests
{
    [TestMethod]
    [Timeout(30_000)]
    public async Task ReceiverHandlesConcurrentUploadsInOneSession()
    {
        var setup = await TestPeers.CreateAsync();
        await using var receiver = setup.Receiver;
        try
        {
            var files = new Dictionary<string, FileDto>(StringComparer.Ordinal)
            {
                ["a"] = File("a", "a.txt", "alpha"),
                ["b"] = File("b", "b.txt", "bravo")
            };
            var requestTask = NextRequestAsync(receiver);
            var prepareTask = setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint, setup.Request(files), null, default);
            var incoming = await requestTask;
            var receiveTask = receiver.AcceptAsync(incoming.RequestId);
            var prepared = await prepareTask;
            Assert.IsNotNull(prepared);

            await Task.WhenAll(files.Select(pair => UploadAsync(setup, prepared, pair.Value, pair.Key == "a" ? "alpha" : "bravo")));
            var result = await receiveTask;

            Assert.AreEqual(2, result.RequireCompleted().Items.Count);
            Assert.AreEqual("alpha", await System.IO.File.ReadAllTextAsync(Path.Combine(setup.Downloads, "a.txt")));
            Assert.AreEqual("bravo", await System.IO.File.ReadAllTextAsync(Path.Combine(setup.Downloads, "b.txt")));
        }
        finally { setup.Delete(); }
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task AcceptedButAbandonedSessionTimesOutAndReleasesSlot()
    {
        var setup = await TestPeers.CreateAsync(incomingTransferTimeout: TimeSpan.FromSeconds(1), maxConcurrentTransfers: 1);
        await using var receiver = setup.Receiver;
        try
        {
            var requestTask = NextRequestAsync(receiver);
            var prepareTask = setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["a"] = File("a", "a.txt", "alpha") }), null, default);
            var incoming = await requestTask;
            var receiveTask = receiver.AcceptAsync(incoming.RequestId);
            _ = await prepareTask;

            var result = await receiveTask;
            Assert.AreEqual("transfer_timeout", result.RequireFailed().Failure.Code);
            Assert.IsEmpty(Directory.EnumerateFiles(setup.Downloads, "*", SearchOption.AllDirectories));

            // With exactly one slot, a leaked reservation prevents this transfer.
            var next = NextRequestAsync(receiver);
            var recoveryFile = File("b", "recovered.txt", "recovered");
            var recoveryPrepare = setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["b"] = recoveryFile }), null, default);
            var recoveryRequest = await next.WaitAsync(TimeSpan.FromSeconds(5));
            var recoveryReceive = receiver.AcceptAsync(recoveryRequest.RequestId);
            var recovered = await recoveryPrepare;
            Assert.IsNotNull(recovered);
            await UploadAsync(setup, recovered, recoveryFile, "recovered");
            _ = (await recoveryReceive).RequireCompleted();
            Assert.AreEqual("recovered", await System.IO.File.ReadAllTextAsync(Path.Combine(setup.Downloads, "recovered.txt")));
        }
        finally { setup.Delete(); }
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task BusyReceiverRejectsNewSessionWithoutQueueing()
    {
        var setup = await TestPeers.CreateAsync(maxConcurrentTransfers: 1);
        await using var receiver = setup.Receiver;
        try
        {
            var firstRequestTask = NextRequestAsync(receiver);
            var firstPrepare = setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["a"] = File("a", "a.txt", "alpha") }), null, default);
            var firstRequest = await firstRequestTask;

            await Assert.ThrowsExactlyAsync<PeerBusyException>(() => setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["b"] = File("b", "b.txt", "bravo") }), null, default));
            await receiver.DeclineAsync(firstRequest.RequestId);
            await Assert.ThrowsExactlyAsync<TransferDeclinedException>(() => firstPrepare);
        }
        finally { setup.Delete(); }
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task IncorrectChecksumFailsAndDoesNotPublishFile()
    {
        var setup = await TestPeers.CreateAsync();
        await using var receiver = setup.Receiver;
        try
        {
            var file = File("a", "bad.txt", "expected", new string('0', 64));
            var requestTask = NextRequestAsync(receiver);
            var prepareTask = setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["a"] = file }), null, default);
            var incoming = await requestTask;
            var receiveTask = receiver.AcceptAsync(incoming.RequestId);
            var prepared = await prepareTask;
            Assert.IsNotNull(prepared);

            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => UploadAsync(setup, prepared, file, "expected"));
            var result = await receiveTask;
            Assert.AreEqual("checksum_mismatch", result.RequireFailed().Failure.Code);
            Assert.IsFalse(System.IO.File.Exists(Path.Combine(setup.Downloads, "bad.txt")));
            Assert.IsEmpty(Directory.EnumerateFiles(setup.Downloads, "*.part-*"));
        }
        finally { setup.Delete(); }
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task WrongUploadTokenDoesNotConsumeTheValidToken()
    {
        var setup = await TestPeers.CreateAsync();
        await using var receiver = setup.Receiver;
        try
        {
            var file = File("a", "retry.txt", "retry");
            var requestTask = NextRequestAsync(receiver);
            var prepareTask = setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["a"] = file }), null, default);
            var incoming = await requestTask;
            var receiveTask = receiver.AcceptAsync(incoming.RequestId);
            var prepared = await prepareTask;
            Assert.IsNotNull(prepared);
            var validToken = prepared.Files[file.Id];
            var bytes = System.Text.Encoding.UTF8.GetBytes("retry");

            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => setup.Client.UploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                prepared.SessionId, file.Id, validToken + "wrong", new MemoryStream(bytes), bytes.Length, file.FileType, default));
            await UploadAsync(setup, prepared, file, "retry");

            _ = (await receiveTask).RequireCompleted();
            Assert.AreEqual("retry", await System.IO.File.ReadAllTextAsync(Path.Combine(setup.Downloads, "retry.txt")));
        }
        finally { setup.Delete(); }
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task InterruptedBodyRemovesTemporaryFile()
    {
        var setup = await TestPeers.CreateAsync(incomingTransferTimeout: TimeSpan.FromSeconds(1));
        await using var receiver = setup.Receiver;
        try
        {
            var file = File("a", "interrupted.txt", "12345");
            var requestTask = NextRequestAsync(receiver);
            var prepareTask = setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["a"] = file }), null, default);
            var incoming = await requestTask;
            var receiveTask = receiver.AcceptAsync(incoming.RequestId);
            var prepared = await prepareTask;
            Assert.IsNotNull(prepared);

            await Assert.ThrowsAsync<Exception>(() => setup.Client.UploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                prepared.SessionId, file.Id, prepared.Files[file.Id], new MemoryStream([1, 2]), file.Size, file.FileType, default));
            var result = await receiveTask;
            Assert.IsTrue(result is ReceiveOutcome.Cancelled or ReceiveOutcome.Failed);
            Assert.IsFalse(System.IO.File.Exists(Path.Combine(setup.Downloads, "interrupted.txt")));
            Assert.IsEmpty(Directory.EnumerateFiles(setup.Downloads, "*.part-*"));
        }
        finally { setup.Delete(); }
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task RegisterRejectsFingerprintThatDiffersFromClientCertificate()
    {
        var setup = await TestPeers.CreateAsync();
        await using var receiver = setup.Receiver;
        try
        {
            var spoofed = new DeviceInfoDto
            {
                Alias = setup.SenderInfo.Alias,
                Version = setup.SenderInfo.Version,
                DeviceType = setup.SenderInfo.DeviceType,
                Fingerprint = new string('0', 64),
                Port = setup.SenderInfo.Port,
                Protocol = setup.SenderInfo.Protocol
            };
            var denied = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
                setup.Client.RegisterAsync(setup.Endpoint, setup.ReceiverFingerprint, spoofed, default));
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.DoesNotContain(device => device.Fingerprint == spoofed.Fingerprint, receiver.GetDevices());

            // Rejecting all registrations must not pass a spoof-rejection test.
            _ = await setup.Client.RegisterAsync(setup.Endpoint, setup.ReceiverFingerprint, setup.SenderInfo, default);
            Assert.Contains(device => device.Fingerprint == setup.SenderInfo.Fingerprint && device.Alias == "Sender", receiver.GetDevices());
        }
        finally { setup.Delete(); }
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task PrepareMetadataLimitRemainsEnforcedWhileLargeUploadsAreAllowed()
    {
        var setup = await TestPeers.CreateAsync(maxPrepareRequestBytes: 1024);
        await using var receiver = setup.Receiver;
        try
        {
            var file = File("a", new string('x', 2048) + ".txt", "content");
            var failure = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["a"] = file }), null, default));
            Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, failure.StatusCode,
                "An accepted request that later times out must not masquerade as metadata-limit enforcement.");
            Assert.IsEmpty(receiver.GetPendingIncomingTransfers());

            // Positive control: the limit applies to metadata, not file bytes.
            // A server rejecting every prepare request must fail this check.
            var content = new string('v', 4096);
            var valid = File("b", "valid.bin", content);
            var next = NextRequestAsync(receiver);
            var preparing = setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["b"] = valid }), null, default);
            var incoming = await next.WaitAsync(TimeSpan.FromSeconds(5));
            var receiving = receiver.AcceptAsync(incoming.RequestId);
            var prepared = await preparing;
            Assert.IsNotNull(prepared);
            await UploadAsync(setup, prepared, valid, content);
            _ = (await receiving).RequireCompleted();
            Assert.AreEqual(content, await System.IO.File.ReadAllTextAsync(Path.Combine(setup.Downloads, "valid.bin")));
        }
        finally { setup.Delete(); }
    }

    private static FileDto File(string id, string name, string content, string? sha256 = null)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new FileDto
        {
            Id = id,
            FileName = name,
            Size = bytes.Length,
            FileType = "text/plain",
            Sha256 = sha256 ?? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        };
    }

    private static Task UploadAsync(TestPeers setup, PrepareUploadResponseDto prepared, FileDto file, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return setup.Client.UploadAsync(setup.Endpoint, setup.ReceiverFingerprint, prepared.SessionId, file.Id,
            prepared.Files[file.Id], new MemoryStream(bytes, writable: false), bytes.Length, file.FileType, default);
    }

    private static async Task<IncomingTransferRequest> NextRequestAsync(LocalSendNode node)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync())
            return request;
        throw new InvalidOperationException("Incoming request stream ended.");
    }

    private sealed class TestPeers
    {
        private readonly DeviceIdentity _senderIdentity;
        public required string Root { get; init; }
        public required string Downloads { get; init; }
        public required LocalSendNode Receiver { get; init; }
        public required V2HttpClient Client { get; init; }
        public required DeviceEndpoint Endpoint { get; init; }
        public required string ReceiverFingerprint { get; init; }
        public required DeviceInfoDto SenderInfo { get; init; }

        private TestPeers(DeviceIdentity senderIdentity) => _senderIdentity = senderIdentity;

        public PrepareUploadRequestDto Request(Dictionary<string, FileDto> files) => new() { Info = SenderInfo, Files = files };

        public static async Task<TestPeers> CreateAsync(TimeSpan? incomingTransferTimeout = null, int maxConcurrentTransfers = 4,
            long maxPrepareRequestBytes = 4 * 1024 * 1024)
        {
            var root = TestDirectory.Create();
            var downloads = Path.Combine(root, "downloads");
            var receiverPort = GetFreePort();
            var senderPort = GetFreePort();
            var senderOptions = Options("Sender", Path.Combine(root, "sender"), Path.Combine(root, "sender-downloads"), senderPort);
            var receiverOptions = Options("Receiver", Path.Combine(root, "receiver"), downloads, receiverPort,
                incomingTransferTimeout ?? TimeSpan.FromSeconds(5), maxConcurrentTransfers, maxPrepareRequestBytes);
            var senderIdentity = await DeviceIdentityStore.LoadOrCreateAsync(senderOptions.DataDirectory, default);
            var receiver = new LocalSendNode(receiverOptions);
            await receiver.StartAsync();
            return new TestPeers(senderIdentity)
            {
                Root = root,
                Downloads = downloads,
                Receiver = receiver,
                Client = new V2HttpClient(senderIdentity, senderOptions),
                Endpoint = new DeviceEndpoint(IPAddress.Loopback, receiverPort, LocalSendProtocol.Https),
                ReceiverFingerprint = receiver.Identity!.Fingerprint,
                SenderInfo = new DeviceInfoDto
                {
                    Alias = "Sender",
                    Version = "2.2",
                    DeviceType = "desktop",
                    Fingerprint = senderIdentity.Fingerprint,
                    Port = senderPort,
                    Protocol = "https"
                }
            };
        }

        public void Delete()
        {
            _senderIdentity.Dispose();
            Directory.Delete(Root, recursive: true);
        }

        private static LocalSendOptions Options(string alias, string data, string downloads, int port,
            TimeSpan? incomingTransferTimeout = null, int maxConcurrentTransfers = 4, long maxPrepareRequestBytes = 4 * 1024 * 1024) => new()
            {
                Alias = alias,
                DataDirectory = data,
                DownloadDirectory = downloads,
                Port = port,
                RequestTimeout = TimeSpan.FromSeconds(5),
                IncomingDecisionTimeout = TimeSpan.FromSeconds(5),
                IncomingTransferTimeout = incomingTransferTimeout ?? TimeSpan.FromSeconds(5),
                MaxConcurrentTransfers = maxConcurrentTransfers,
                MaxPrepareRequestBytes = maxPrepareRequestBytes
            };

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
