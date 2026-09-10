using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LocalSendDotNet.Protocol.V2;

namespace LocalSendDotNet.Core.Tests;

public sealed class TransportReliabilityTests
{
    [Fact(Timeout = 30_000)]
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
            var prepared = Assert.IsType<PrepareUploadResponseDto>(await prepareTask);

            await Task.WhenAll(files.Select(pair => UploadAsync(setup, prepared, pair.Value, pair.Key == "a" ? "alpha" : "bravo")));
            var result = await receiveTask;

            Assert.Equal(TransferState.Completed, result.State);
            Assert.Equal(2, result.Items.Count);
            Assert.Equal("alpha", await System.IO.File.ReadAllTextAsync(Path.Combine(setup.Downloads, "a.txt")));
            Assert.Equal("bravo", await System.IO.File.ReadAllTextAsync(Path.Combine(setup.Downloads, "b.txt")));
        }
        finally { setup.Delete(); }
    }

    [Fact(Timeout = 30_000)]
    public async Task AcceptedButAbandonedSessionTimesOutAndReleasesSlot()
    {
        var setup = await TestPeers.CreateAsync(incomingTransferTimeout: TimeSpan.FromMilliseconds(200));
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
            Assert.Equal(TransferState.Failed, result.State);
            Assert.Equal("transfer_timeout", result.Failure!.Code);
        }
        finally { setup.Delete(); }
    }

    [Fact(Timeout = 30_000)]
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

            await Assert.ThrowsAsync<PeerBusyException>(() => setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["b"] = File("b", "b.txt", "bravo") }), null, default));
            await receiver.DeclineAsync(firstRequest.RequestId);
            await Assert.ThrowsAsync<TransferDeclinedException>(() => firstPrepare);
        }
        finally { setup.Delete(); }
    }

    [Fact(Timeout = 30_000)]
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
            var prepared = Assert.IsType<PrepareUploadResponseDto>(await prepareTask);

            await Assert.ThrowsAsync<HttpRequestException>(() => UploadAsync(setup, prepared, file, "expected"));
            var result = await receiveTask;
            Assert.Equal(TransferState.Failed, result.State);
            Assert.Equal("checksum_mismatch", result.Failure!.Code);
            Assert.False(System.IO.File.Exists(Path.Combine(setup.Downloads, "bad.txt")));
            Assert.Empty(Directory.EnumerateFiles(setup.Downloads, "*.part-*"));
        }
        finally { setup.Delete(); }
    }

    [Fact(Timeout = 30_000)]
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
            var prepared = Assert.IsType<PrepareUploadResponseDto>(await prepareTask);
            var validToken = prepared.Files[file.Id];
            var bytes = System.Text.Encoding.UTF8.GetBytes("retry");

            await Assert.ThrowsAsync<HttpRequestException>(() => setup.Client.UploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                prepared.SessionId, file.Id, validToken + "wrong", new MemoryStream(bytes), bytes.Length, file.FileType, default));
            await UploadAsync(setup, prepared, file, "retry");

            Assert.Equal(TransferState.Completed, (await receiveTask).State);
            Assert.Equal("retry", await System.IO.File.ReadAllTextAsync(Path.Combine(setup.Downloads, "retry.txt")));
        }
        finally { setup.Delete(); }
    }

    [Fact(Timeout = 30_000)]
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
            var prepared = Assert.IsType<PrepareUploadResponseDto>(await prepareTask);

            await Assert.ThrowsAnyAsync<Exception>(() => setup.Client.UploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                prepared.SessionId, file.Id, prepared.Files[file.Id], new MemoryStream([1, 2]), file.Size, file.FileType, default));
            var result = await receiveTask;
            Assert.True(result.State is TransferState.Cancelled or TransferState.Failed);
            Assert.False(System.IO.File.Exists(Path.Combine(setup.Downloads, "interrupted.txt")));
            Assert.Empty(Directory.EnumerateFiles(setup.Downloads, "*.part-*"));
        }
        finally { setup.Delete(); }
    }

    [Fact(Timeout = 30_000)]
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
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                setup.Client.RegisterAsync(setup.Endpoint, setup.ReceiverFingerprint, spoofed, default));
            Assert.DoesNotContain(receiver.GetDevices(), device => device.Fingerprint == spoofed.Fingerprint);
        }
        finally { setup.Delete(); }
    }

    [Fact(Timeout = 30_000)]
    public async Task PrepareMetadataLimitRemainsEnforcedWhileLargeUploadsAreAllowed()
    {
        var setup = await TestPeers.CreateAsync(maxPrepareRequestBytes: 128);
        await using var receiver = setup.Receiver;
        try
        {
            var file = File("a", new string('x', 256) + ".txt", "content");
            await Assert.ThrowsAsync<HttpRequestException>(() => setup.Client.PrepareUploadAsync(setup.Endpoint, setup.ReceiverFingerprint,
                setup.Request(new Dictionary<string, FileDto> { ["a"] = file }), null, default));
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
