using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using LocalSendDotNet.Protocol.V2;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class PinSecurityTests
{
    private const string Pin = "2468";
    private const string Content = "PIN recovery 🌏\r\n exact content \t";

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LocalSendPinCountsOnlyConsecutiveWrongValuesAndRecoversOnTheSameNodes(bool successfulReset)
    {
        var root = TestDirectory.Create();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var receiverOptions = CreateOptions(root, "receiver", Pin, https: true);
            await using var receiver = new LocalSendNode(receiverOptions);
            await receiver.StartAsync(deadline.Token);
            await using var sender = new LocalSendNode(CreateOptions(root, "sender", null, https: true));
            await sender.StartAsync(deadline.Token);
            var fingerprint = receiver.Identity!.Fingerprint;
            var device = await sender.AddKnownDeviceAsync(new(IPAddress.Loopback, receiverOptions.Port, LocalSendProtocol.Https),
                fingerprint, deadline.Token);

            for (var index = 0; index < 4; index++)
            {
                var missing = (await sender.SendAsync(device, [new SendTextItem(Content)], cancellationToken: deadline.Token)).RequirePinRequired();
                Assert.IsFalse(missing.InvalidPin, "Missing PIN must not count as an incorrect supplied value.");
                Assert.IsEmpty(receiver.GetPendingIncomingTransfers());
            }
            await AssertWrongPinsAsync(sender, receiver, device, 2, deadline.Token);

            if (successfulReset)
            {
                _ = (await SendAndAcceptAsync(sender, receiver, device, deadline.Token)).RequireCompleted();
                await AssertWrongPinsAsync(sender, receiver, device, 2, deadline.Token);
                // Without resetting after success, these two failures would be
                // the third/fourth of the old sequence and trigger a lock.
                _ = (await SendAndAcceptAsync(sender, receiver, device, deadline.Token)).RequireCompleted();
            }
            else
            {
                await AssertWrongPinsAsync(sender, receiver, device, 1, deadline.Token);
                foreach (var value in new string?[] { Pin, "0000", null })
                {
                    var locked = await sender.SendAsync(device, [new SendTextItem(Content)], new() { Pin = value },
                        cancellationToken: deadline.Token);
                    Assert.IsTrue(locked is SendOutcome.PinRateLimited, $"Locked PIN {value ?? "<missing>"}: {Describe(locked)}");
                    Assert.IsEmpty(receiver.GetPendingIncomingTransfers());
                }
                var recovery = Stopwatch.StartNew();
                SendOutcome recovered;
                do
                {
                    recovered = await SendAndAcceptAsync(sender, receiver, device, deadline.Token);
                    if (recovered is not SendOutcome.PinRateLimited) break;
                    Assert.IsLessThan(TimeSpan.FromSeconds(5), recovery.Elapsed, "PIN lock did not expire on the same node.");
                    await Task.Delay(50, deadline.Token);
                } while (true);
                _ = recovered.RequireCompleted();
            }

            Assert.AreEqual(fingerprint, receiver.Identity!.Fingerprint);
            Assert.AreEqual(LocalSendNodeState.Running, receiver.State);
            Assert.AreEqual(LocalSendNodeState.Running, sender.State);
            Assert.IsEmpty(receiver.GetPendingIncomingTransfers());
            Assert.IsEmpty(Directory.EnumerateFiles(receiverOptions.DownloadDirectory, "*", SearchOption.AllDirectories).ToArray());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod, Timeout(20_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BrowserDownloadPinLockAndSuccessfulResetPreserveTheShare(bool successfulReset)
    {
        var root = TestDirectory.Create();
        try
        {
            var options = CreateOptions(root, "web", null, https: false);
            await using var node = new LocalSendNode(options);
            await node.StartAsync();
            await node.StartWebShareAsync([new SendTextItem(Content, "secret.txt")], new() { Pin = Pin, AutoAccept = true });
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Port}"), Timeout = TimeSpan.FromSeconds(5) };
            for (var index = 0; index < 4; index++)
                await AssertPrepareStatusAsync(client, null, HttpStatusCode.Unauthorized);
            for (var index = 0; index < 2; index++)
                await AssertPrepareStatusAsync(client, "0000", HttpStatusCode.Unauthorized);
            Assert.IsEmpty(node.GetWebShare().Requests);

            if (successfulReset)
            {
                Assert.IsTrue(await DownloadWithPinAsync(client));
                for (var index = 0; index < 2; index++)
                    await AssertPrepareStatusAsync(client, "0000", HttpStatusCode.Unauthorized);
                Assert.IsTrue(await DownloadWithPinAsync(client));
            }
            else
            {
                await AssertPrepareStatusAsync(client, "0000", HttpStatusCode.Unauthorized);
                await AssertPrepareStatusAsync(client, Pin, HttpStatusCode.TooManyRequests);
                await AssertPrepareStatusAsync(client, null, HttpStatusCode.TooManyRequests);
                Assert.IsEmpty(node.GetWebShare().Requests);
                var recovery = Stopwatch.StartNew();
                while (!await DownloadWithPinAsync(client))
                {
                    Assert.IsLessThan(TimeSpan.FromSeconds(5), recovery.Elapsed, "Web PIN lock did not expire without restarting the share.");
                    await Task.Delay(50);
                }
            }
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            Assert.IsTrue(node.GetWebShare().Active);
            Assert.HasCount(successfulReset ? 2 : 1, node.GetWebShare().Requests);
            Assert.IsTrue(node.GetWebShare().Requests.All(request => !request.Pending));
            Assert.IsEmpty(node.GetPendingIncomingTransfers());
            Assert.IsEmpty(Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories).ToArray());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod, Timeout(20_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BrowserReceivePinLockAndSuccessfulResetPreserveUploads(bool successfulReset)
    {
        var root = TestDirectory.Create();
        try
        {
            // Web receive has its own PIN, independent of LocalSend ReceivePin.
            var options = CreateOptions(root, "web-receive", "1357", https: false);
            await using var node = new LocalSendNode(options);
            await node.StartAsync();
            await node.StartWebReceiveAsync(new() { Pin = Pin, AutoAccept = true });
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Port}"), Timeout = TimeSpan.FromSeconds(5) };
            for (var index = 0; index < 4; index++)
                await AssertWebUploadDeniedAsync(client, node, null, HttpStatusCode.Unauthorized);
            for (var index = 0; index < 2; index++)
                await AssertWebUploadDeniedAsync(client, node, "0000", HttpStatusCode.Unauthorized);
            Assert.IsEmpty(Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories).ToArray());

            if (successfulReset)
            {
                Assert.IsTrue(await UploadWithPinAsync(client, "receive-1.txt"));
                for (var index = 0; index < 2; index++)
                    await AssertWebUploadDeniedAsync(client, node, "0000", HttpStatusCode.Unauthorized);
                Assert.IsTrue(await UploadWithPinAsync(client, "receive-2.txt"));
            }
            else
            {
                await AssertWebUploadDeniedAsync(client, node, "0000", HttpStatusCode.Unauthorized);
                foreach (var supplied in new string?[] { Pin, "0000", null })
                    await AssertWebUploadDeniedAsync(client, node, supplied, HttpStatusCode.TooManyRequests);
                Assert.IsEmpty(Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories).ToArray());
                var recovery = Stopwatch.StartNew();
                while (!await UploadWithPinAsync(client, "receive-1.txt"))
                {
                    Assert.IsLessThan(TimeSpan.FromSeconds(5), recovery.Elapsed, "Web receive PIN lock did not expire on the same node.");
                    await Task.Delay(50);
                }
            }

            var files = Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories).ToArray();
            Assert.HasCount(successfulReset ? 2 : 1, files);
            foreach (var file in files)
            {
                Assert.IsTrue(Path.GetFileName(file) is "receive-1.txt" or "receive-2.txt");
                CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(Content), await File.ReadAllBytesAsync(file));
            }
            Assert.IsEmpty(node.GetPendingIncomingTransfers());
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            Assert.IsTrue(node.GetWebShare().Active);
            Assert.AreEqual(WebShareMode.Receive, node.GetWebShare().Mode);
            // The LocalSend PIN must not substitute for the Web receive PIN.
            await AssertWebUploadDeniedAsync(client, node, "1357", HttpStatusCode.Unauthorized);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod, Timeout(15_000)]
    public async Task BrowserPinLockIsScopedToRemoteAddressAndSurvivesOtherClientsSuccess()
    {
        var root = TestDirectory.Create();
        try
        {
            var options = CreateOptions(root, "web-ip-isolation", null, https: false, pinLockout: TimeSpan.FromSeconds(30));
            await using var node = new LocalSendNode(options);
            await node.StartAsync();
            await node.StartWebShareAsync([new SendTextItem(Content, "secret.txt")], new() { Pin = Pin, AutoAccept = true });
            var target = new Uri($"http://127.0.0.1:{options.Port}");
            using var lockedClient = CreateBoundClient(target, IPAddress.Loopback);
            using var independentClient = CreateBoundClient(target, IPAddress.Parse("127.0.0.2"));
            for (var index = 0; index < 3; index++)
                await AssertPrepareStatusAsync(lockedClient, "0000", HttpStatusCode.Unauthorized);
            await AssertPrepareStatusAsync(lockedClient, Pin, HttpStatusCode.TooManyRequests);
            Assert.IsEmpty(node.GetWebShare().Requests);

            Assert.IsTrue(await DownloadWithPinAsync(independentClient), "Another source IP must not inherit the lock.");
            // Successful authentication on a different connection/IP must not
            // clear the first client's lock or create an unauthorized session.
            await AssertPrepareStatusAsync(lockedClient, Pin, HttpStatusCode.TooManyRequests);
            await AssertPrepareStatusAsync(lockedClient, null, HttpStatusCode.TooManyRequests);
            Assert.AreEqual("127.0.0.2", Assert.ContainsSingle(node.GetWebShare().Requests).Ip);
            Assert.IsEmpty(node.GetPendingIncomingTransfers());
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            Assert.IsEmpty(Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories).ToArray());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod, Timeout(15_000)]
    public async Task BrowserUploadPinLockIsScopedToRemoteAddress()
    {
        var root = TestDirectory.Create();
        try
        {
            var options = CreateOptions(root, "web-upload-ip-isolation", null, https: false, pinLockout: TimeSpan.FromSeconds(30));
            await using var node = new LocalSendNode(options);
            await node.StartAsync();
            await node.StartWebReceiveAsync(new() { Pin = Pin, AutoAccept = true });
            var target = new Uri($"http://127.0.0.1:{options.Port}");
            using var lockedClient = CreateBoundClient(target, IPAddress.Loopback);
            using var independentClient = CreateBoundClient(target, IPAddress.Parse("127.0.0.2"));
            for (var index = 0; index < 3; index++)
                await AssertWebUploadDeniedAsync(lockedClient, node, "0000", HttpStatusCode.Unauthorized);
            await AssertWebUploadDeniedAsync(lockedClient, node, Pin, HttpStatusCode.TooManyRequests);
            Assert.IsEmpty(Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories).ToArray());

            Assert.IsTrue(await UploadWithPinAsync(independentClient, "independent.txt"));
            await AssertWebUploadDeniedAsync(lockedClient, node, Pin, HttpStatusCode.TooManyRequests);
            await AssertWebUploadDeniedAsync(lockedClient, node, null, HttpStatusCode.TooManyRequests);
            var file = Assert.ContainsSingle(Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories));
            Assert.AreEqual("independent.txt", Path.GetFileName(file));
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(Content), await File.ReadAllBytesAsync(file));
            Assert.IsEmpty(node.GetPendingIncomingTransfers());
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            Assert.AreEqual(WebShareMode.Receive, node.GetWebShare().Mode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod, Timeout(20_000)]
    public async Task LocalSendTlsPinLockIsScopedToRemoteAddressNotClientCertificate()
    {
        var root = TestDirectory.Create();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var options = CreateOptions(root, "tls-ip-isolation", Pin, https: true, pinLockout: TimeSpan.FromSeconds(30));
            await using var node = new LocalSendNode(options);
            await node.StartAsync(deadline.Token);
            using var identity = await DeviceIdentityStore.LoadOrCreateAsync(Path.Combine(root, "client"), deadline.Token);
            var target = new Uri($"https://127.0.0.1:{options.Port}");
            using var lockedClient = CreateBoundClient(target, IPAddress.Loopback, identity, node.Identity!.Fingerprint);
            using var independentClient = CreateBoundClient(target, IPAddress.Parse("127.0.0.2"), identity, node.Identity!.Fingerprint);
            for (var index = 0; index < 3; index++)
                await AssertLocalSendPrepareStatusAsync(lockedClient, identity, "0000", HttpStatusCode.Unauthorized, deadline.Token);
            await AssertLocalSendPrepareStatusAsync(lockedClient, identity, Pin, HttpStatusCode.TooManyRequests, deadline.Token);
            Assert.IsEmpty(node.GetPendingIncomingTransfers());

            await using var incoming = node.WatchIncomingTransfersAsync(deadline.Token).GetAsyncEnumerator();
            var arriving = incoming.MoveNextAsync().AsTask();
            using var metadata = LocalSendMetadata(identity);
            var preparing = independentClient.PostAsync("/api/localsend/v2/prepare-upload?pin=" + Pin, metadata, deadline.Token);
            Task<ReceiveOutcome>? accepting = null;
            HttpResponseMessage? preparedResponse = null;
            try
            {
                if (await Task.WhenAny(arriving, preparing) == preparing)
                {
                    preparedResponse = await preparing;
                    Assert.Fail($"Independent source IP did not reach the normal acceptance flow: {preparedResponse.StatusCode}.");
                }
                Assert.IsTrue(await arriving);
                var request = incoming.Current;
                Assert.AreEqual(identity.Fingerprint, request.Sender.Fingerprint, "Both clients deliberately use the same TLS certificate.");
                Assert.AreEqual(IPAddress.Parse("127.0.0.2"), Assert.ContainsSingle(request.Sender.Endpoints).Address);
                accepting = node.AcceptAsync(request.RequestId, cancellationToken: deadline.Token);
                preparedResponse = await preparing;
                Assert.AreEqual(HttpStatusCode.OK, preparedResponse.StatusCode);
                using var prepared = JsonDocument.Parse(await preparedResponse.Content.ReadAsStringAsync(deadline.Token));
                var session = prepared.RootElement.GetProperty("sessionId").GetString();
                var token = prepared.RootElement.GetProperty("files").GetProperty("bound-file").GetString();
                using var body = new ByteArrayContent(Encoding.UTF8.GetBytes(Content));
                using var uploaded = await independentClient.PostAsync($"/api/localsend/v2/upload?sessionId={session}&fileId=bound-file&token={token}", body, deadline.Token);
                Assert.AreEqual(HttpStatusCode.OK, uploaded.StatusCode);
                var item = Assert.ContainsSingle((await accepting).RequireCompleted().Items);
                Assert.AreEqual((long)Encoding.UTF8.GetByteCount(Content), item.BytesTransferred);
                Assert.IsNotNull(item.SavedPath);
                CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(Content), await File.ReadAllBytesAsync(item.SavedPath, deadline.Token));
            }
            finally
            {
                await deadline.CancelAsync();
                foreach (var task in new Task?[] { arriving, preparing, accepting })
                {
                    if (task is null) continue;
                    try { await task.WaitAsync(TimeSpan.FromSeconds(3)); }
                    catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
                }
                preparedResponse?.Dispose();
            }

            // Only the other IP succeeded: the original IP remains locked even
            // though its certificate was accepted on the second connection.
            await AssertLocalSendPrepareStatusAsync(lockedClient, identity, Pin, HttpStatusCode.TooManyRequests, CancellationToken.None);
            await AssertLocalSendPrepareStatusAsync(lockedClient, identity, null, HttpStatusCode.TooManyRequests, CancellationToken.None);
            Assert.IsEmpty(node.GetPendingIncomingTransfers());
            Assert.HasCount(1, Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories));
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod, Timeout(30_000)]
    public async Task ConcurrentIncorrectTlsPinsCannotLoseAttemptsOrAuthorizeAnOffer()
    {
        var root = TestDirectory.Create();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var options = CreateOptions(root, "tls-concurrent-pin", Pin, https: true, pinLockout: TimeSpan.FromSeconds(30));
            await using var node = new LocalSendNode(options);
            await node.StartAsync(deadline.Token);
            using var identity = await DeviceIdentityStore.LoadOrCreateAsync(Path.Combine(root, "client"), deadline.Token);
            var target = new Uri($"https://127.0.0.1:{options.Port}");
            // Each round uses a fresh IP on the same server, so no lock expiry,
            // new node or production-state reset can hide lost updates.
            for (var round = 0; round < 24; round++)
            {
                using var client = CreateBoundClient(target, IPAddress.Parse($"127.0.0.{20 + round}"), identity, node.Identity!.Fingerprint);
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var attempts = Enumerable.Range(0, 8).Select(async _ =>
                {
                    await ready.Task.WaitAsync(deadline.Token);
                    using var metadata = LocalSendMetadata(identity);
                    using var response = await client.PostAsync("/api/localsend/v2/prepare-upload?pin=0000", metadata, deadline.Token);
                    return response.StatusCode;
                }).ToArray();
                ready.SetResult();
                var statuses = await Task.WhenAll(attempts).WaitAsync(deadline.Token);
                Assert.HasCount(3, statuses.Where(status => status == HttpStatusCode.Unauthorized),
                    $"Source IP round {round}: {string.Join(", ", statuses)}");
                Assert.HasCount(5, statuses.Where(status => status == HttpStatusCode.TooManyRequests));
                Assert.IsEmpty(node.GetPendingIncomingTransfers());
                await AssertLocalSendPrepareStatusAsync(client, identity, Pin, HttpStatusCode.TooManyRequests, deadline.Token);
            }
            Assert.IsEmpty(Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories).ToArray());
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentIncorrectBrowserPinsLockWithoutCreatingRequests(bool upload)
    {
        var root = TestDirectory.Create();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var options = CreateOptions(root, "web-concurrent-pin", null, https: false, pinLockout: TimeSpan.FromSeconds(30));
            await using var node = new LocalSendNode(options);
            await node.StartAsync(deadline.Token);
            if (upload)
                await node.StartWebReceiveAsync(new() { Pin = Pin, AutoAccept = true }, deadline.Token);
            else
                await node.StartWebShareAsync([new SendTextItem(Content, "secret.txt")], new() { Pin = Pin, AutoAccept = true }, deadline.Token);
            var target = new Uri($"http://127.0.0.1:{options.Port}");
            var endpoint = upload ? "/api/localsend/v2/prepare-web-upload" : "/api/localsend/v2/prepare-download";
            for (var round = 0; round < 16; round++)
            {
                using var client = CreateBoundClient(target, IPAddress.Parse($"127.0.0.{50 + round}"));
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var attempts = Enumerable.Range(0, 8).Select(async _ =>
                {
                    await ready.Task.WaitAsync(deadline.Token);
                    using var metadata = upload ? WebUploadMetadata("unauthorized.txt") : null;
                    using var response = await client.PostAsync(endpoint + "?pin=0000", metadata, deadline.Token);
                    return response.StatusCode;
                }).ToArray();
                ready.SetResult();
                var statuses = await Task.WhenAll(attempts).WaitAsync(deadline.Token);
                Assert.HasCount(3, statuses.Where(status => status == HttpStatusCode.Unauthorized),
                    $"Web {(upload ? "upload" : "download")} source IP round {round}: {string.Join(", ", statuses)}");
                Assert.HasCount(5, statuses.Where(status => status == HttpStatusCode.TooManyRequests));
                if (upload)
                    await AssertWebUploadDeniedAsync(client, node, Pin, HttpStatusCode.TooManyRequests);
                else
                    await AssertPrepareStatusAsync(client, Pin, HttpStatusCode.TooManyRequests);
                Assert.IsEmpty(node.GetWebShare().Requests);
                Assert.IsEmpty(node.GetPendingIncomingTransfers());
                Assert.IsEmpty(Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories).ToArray());
            }

            // The same share stays usable for a different source address; no
            // restart, lock expiry or replacement service hides the result.
            using var independentClient = CreateBoundClient(target, IPAddress.Parse("127.0.0.100"));
            Assert.IsTrue(upload ? await UploadWithPinAsync(independentClient, "independent.txt") : await DownloadWithPinAsync(independentClient));
            if (upload)
                Assert.IsEmpty(node.GetWebShare().Requests, "Completed uploads must leave no pending browser request.");
            else
            {
                var request = Assert.ContainsSingle(node.GetWebShare().Requests);
                Assert.AreEqual("127.0.0.100", request.Ip);
                Assert.IsFalse(request.Pending);
            }
            var files = Directory.EnumerateFiles(options.DownloadDirectory, "*", SearchOption.AllDirectories).ToArray();
            Assert.HasCount(upload ? 1 : 0, files);
            if (upload)
                CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(Content), await File.ReadAllBytesAsync(files[0], deadline.Token));
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task AssertLocalSendPrepareStatusAsync(HttpClient client, DeviceIdentity identity, string? pin,
        HttpStatusCode expected, CancellationToken cancellation)
    {
        using var metadata = LocalSendMetadata(identity);
        using var response = await client.PostAsync("/api/localsend/v2/prepare-upload" + (pin is null ? "" : "?pin=" + pin), metadata, cancellation);
        Assert.AreEqual(expected, response.StatusCode);
    }

    private static StringContent LocalSendMetadata(DeviceIdentity identity) => new(JsonSerializer.Serialize(new PrepareUploadRequestDto
    {
        Info = new DeviceInfoDto
        {
            Alias = "Bound TLS client",
            Version = V2Constants.Version,
            DeviceType = "desktop",
            Fingerprint = identity.Fingerprint,
            Port = 1,
            Protocol = "https",
        },
        Files = new()
        {
            ["bound-file"] = new FileDto { Id = "bound-file", FileName = "bound.txt", Size = Encoding.UTF8.GetByteCount(Content), FileType = "text/plain" },
        },
    }, V2JsonContext.Default.PrepareUploadRequestDto), Encoding.UTF8, "application/json");

    private static HttpClient CreateBoundClient(Uri target, IPAddress localAddress, DeviceIdentity? identity = null, string? fingerprint = null)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = async (context, cancellation) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    socket.Bind(new IPEndPoint(localAddress, 0));
                    await socket.ConnectAsync(context.DnsEndPoint, cancellation);
                    Assert.AreEqual(localAddress, ((IPEndPoint)socket.LocalEndPoint!).Address);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            },
        };
        if (identity is not null)
        {
            Assert.IsNotNull(fingerprint);
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection { identity.Certificate },
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    if (certificate is null) return false;
                    using var peer = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                    return DeviceIdentityStore.ValidatePeerCertificate(peer, fingerprint);
                },
            };
        }
        return new HttpClient(handler) { BaseAddress = target, Timeout = TimeSpan.FromSeconds(5) };
    }

    private static async Task AssertWebUploadDeniedAsync(HttpClient client, LocalSendNode node, string? pin, HttpStatusCode status)
    {
        using var metadata = WebUploadMetadata("denied.txt");
        using var response = await client.PostAsync("/api/localsend/v2/prepare-web-upload" + (pin is null ? "" : "?pin=" + pin), metadata);
        Assert.AreEqual(status, response.StatusCode);
        Assert.IsEmpty(node.GetPendingIncomingTransfers());
    }

    private static async Task<bool> UploadWithPinAsync(HttpClient client, string name)
    {
        using var metadata = WebUploadMetadata(name);
        using var response = await client.PostAsync("/api/localsend/v2/prepare-web-upload?pin=" + Pin, metadata);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) return false;
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var prepared = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var session = prepared.RootElement.GetProperty("sessionId").GetString();
        var token = prepared.RootElement.GetProperty("files").GetProperty("browser-file").GetString();
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(Content));
        using var upload = await client.PostAsync($"/api/localsend/v2/upload?sessionId={session}&fileId=browser-file&token={token}", content);
        Assert.AreEqual(HttpStatusCode.OK, upload.StatusCode);
        return true;
    }

    private static StringContent WebUploadMetadata(string name) => new(JsonSerializer.Serialize(new
    {
        files = new[] { new { id = "browser-file", fileName = name, size = Encoding.UTF8.GetByteCount(Content), fileType = "text/plain" } },
    }), Encoding.UTF8, "application/json");

    private static async Task AssertWrongPinsAsync(LocalSendNode sender, LocalSendNode receiver,
        LocalSendDevice device, int count, CancellationToken token)
    {
        for (var index = 0; index < count; index++)
        {
            var wrong = (await sender.SendAsync(device, [new SendTextItem(Content)], new() { Pin = "0000" },
                cancellationToken: token)).RequirePinRequired();
            Assert.IsTrue(wrong.InvalidPin);
            Assert.IsEmpty(receiver.GetPendingIncomingTransfers());
        }
    }

    private static async Task<SendOutcome> SendAndAcceptAsync(LocalSendNode sender, LocalSendNode receiver,
        LocalSendDevice device, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using var incoming = receiver.WatchIncomingTransfersAsync(lifetime.Token).GetAsyncEnumerator();
        var arriving = incoming.MoveNextAsync().AsTask();
        var sending = sender.SendAsync(device, [new SendTextItem(Content)], new() { Pin = Pin }, cancellationToken: lifetime.Token);
        try
        {
            if (await Task.WhenAny(sending, arriving) == sending)
            {
                var denied = await sending;
                Assert.IsTrue(denied is SendOutcome.PinRateLimited, $"Recovery probe: {Describe(denied)}");
                Assert.IsFalse(arriving.IsCompleted);
                Assert.IsEmpty(receiver.GetPendingIncomingTransfers());
                return denied;
            }
            Assert.IsTrue(await arriving);
            var request = incoming.Current;
            Assert.AreEqual(Content, Assert.ContainsSingle(request.Items).Preview);
            var received = (await receiver.AcceptAsync(request.RequestId, cancellationToken: lifetime.Token)).RequireCompleted();
            var item = Assert.ContainsSingle(received.Items);
            Assert.IsNull(item.SavedPath);
            Assert.AreEqual((long)Encoding.UTF8.GetByteCount(Content), item.BytesTransferred);
            return await sending;
        }
        finally
        {
            await lifetime.CancelAsync();
            try { _ = await sending.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            try { _ = await arriving.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
    }

    private static async Task AssertPrepareStatusAsync(HttpClient client, string? pin, HttpStatusCode expected)
    {
        using var response = await client.PostAsync("/api/localsend/v2/prepare-download" + (pin is null ? "" : "?pin=" + pin), null);
        Assert.AreEqual(expected, response.StatusCode);
    }

    private static async Task<bool> DownloadWithPinAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/localsend/v2/prepare-download?pin=" + Pin, null);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) return false;
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var prepared = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var session = prepared.RootElement.GetProperty("sessionId").GetString();
        var file = Assert.ContainsSingle(prepared.RootElement.GetProperty("files").EnumerateArray().ToArray());
        using var download = await client.GetAsync($"/api/localsend/v2/download?sessionId={session}&fileId={file.GetProperty("id").GetString()}&pin={Pin}");
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode);
        Assert.AreEqual(Content, await download.Content.ReadAsStringAsync());
        return true;
    }

    private static string Describe(SendOutcome outcome) => outcome switch
    {
        SendOutcome.Completed value => value.ToString(),
        SendOutcome.Cancelled value => value.ToString(),
        SendOutcome.PinRequired value => value.ToString(),
        SendOutcome.PinRateLimited value => value.ToString(),
        SendOutcome.PeerBusy value => value.ToString(),
        SendOutcome.Declined value => value.ToString(),
        SendOutcome.Failed value => value.ToString(),
    };

    private static LocalSendOptions CreateOptions(string root, string name, string? pin, bool https, TimeSpan? pinLockout = null)
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new LocalSendOptions
        {
            Alias = name,
            DataDirectory = Path.Combine(root, name, "identity"),
            DownloadDirectory = Path.Combine(root, name, "downloads"),
            Port = port,
            EnableHttps = https,
            ReceivePin = pin,
            PinLockoutDuration = pinLockout ?? TimeSpan.FromSeconds(2),
            RequestTimeout = TimeSpan.FromSeconds(5),
            IncomingDecisionTimeout = TimeSpan.FromSeconds(5),
            NetworkWhitelist = [],
        };
    }
}
