using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
public sealed class WebShareTests
{
    [TestMethod, Timeout(20_000)]
    [DataRow("decline")]
    [DataRow("disconnect")]
    [DataRow("stop")]
    [DataRow("timeout")]
    public async Task PendingSnapshotRemovesOffersAfterDecisionDisconnectOrShutdown(string ending)
    {
        var root = TestDirectory.Create();
        var port = GetFreePort();
        await using var node = CreateNode(root, port);
        try
        {
            Assert.IsEmpty(node.GetPendingIncomingTransfers());
            await node.StartAsync();
            await node.StartWebReceiveAsync();
            using var client = CreateClient();
            using var cancellation = new CancellationTokenSource();
            await using var incoming = node.WatchIncomingTransfersAsync().GetAsyncEnumerator();
            var arriving = incoming.MoveNextAsync().AsTask();
            var preparing = client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/localsend/v2/prepare-web-upload",
                new { files = new[] { new { id = "pending", fileName = "pending.txt", size = 1, fileType = "text/plain" } } }, cancellation.Token);
            Assert.IsTrue(await arriving.WaitAsync(TimeSpan.FromSeconds(5)));
            var request = incoming.Current;
            Assert.AreEqual(request.RequestId, Assert.ContainsSingle(node.GetPendingIncomingTransfers()).RequestId);
            if (ending == "decline")
            {
                await node.DeclineAsync(request.RequestId);
                using var declined = await preparing;
                Assert.AreEqual(HttpStatusCode.Forbidden, declined.StatusCode);
            }
            else if (ending == "disconnect")
            {
                await cancellation.CancelAsync();
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    using var response = await preparing;
                });
            }
            else if (ending == "stop")
            {
                await node.StopAsync();
                using var stopped = await preparing;
                // Stop cancels the decision wait and also declines pending
                // offers. Either terminal path can win, but not an accepted session.
                Assert.IsTrue(stopped.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.RequestTimeout);
            }
            else
            {
                using var timedOut = await preparing;
                Assert.AreEqual(HttpStatusCode.RequestTimeout, timedOut.StatusCode);
            }
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (node.GetPendingIncomingTransfers().Count > 0 && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.IsEmpty(node.GetPendingIncomingTransfers());
            Assert.IsFalse(File.Exists(Path.Combine(root, "downloads", "pending.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task RootIsNotFoundUntilWebShareStarts()
    {
        var root = TestDirectory.Create();
        var port = GetFreePort();
        await using var node = CreateNode(root, port);
        try
        {
            await node.StartAsync();
            using var client = CreateClient();
            var missing = await client.GetAsync($"http://127.0.0.1:{port}/");
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);

            await node.StartWebShareAsync([new SendTextItem("hello", "hello.txt")], new WebShareOptions { AutoAccept = true });
            var page = await client.GetAsync($"http://127.0.0.1:{port}/");
            Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("Tonarink", html, StringComparison.Ordinal);
            Assert.Contains("prepare-download", html, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task AutoAcceptAllowsImmediateDownload()
    {
        var root = TestDirectory.Create();
        var port = GetFreePort();
        await using var node = CreateNode(root, port);
        try
        {
            await node.StartAsync();
            await node.StartWebShareAsync([new SendTextItem("shared-body", "note.txt")], new WebShareOptions { AutoAccept = true });
            using var client = CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Firefox/128.0 (Windows)");
            using var prepared = await client.PostAsync($"http://127.0.0.1:{port}/api/localsend/v2/prepare-download", content: null);
            prepared.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await prepared.Content.ReadAsStringAsync());
            var sessionId = document.RootElement.GetProperty("sessionId").GetString();
            var fileId = document.RootElement.GetProperty("files")[0].GetProperty("id").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(sessionId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(fileId));

            var state = node.GetWebShare();
            Assert.IsTrue(state.Active);
            var request = Assert.ContainsSingle(state.Requests);
            Assert.IsFalse(request.Pending);
            Assert.Contains("Firefox", request.DeviceInfo, StringComparison.Ordinal);

            var download = await client.GetStringAsync($"http://127.0.0.1:{port}/api/localsend/v2/download?sessionId={sessionId}&fileId={fileId}");
            Assert.AreEqual("shared-body", download);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task HostCanAcceptAPendingBrowser()
    {
        var root = TestDirectory.Create();
        var port = GetFreePort();
        await using var node = CreateNode(root, port);
        try
        {
            await node.StartAsync();
            await node.StartWebShareAsync([new SendTextItem("later", "later.txt")]);
            using var client = CreateClient();
            var prepareTask = client.PostAsync($"http://127.0.0.1:{port}/api/localsend/v2/prepare-download", content: null);
            WebShareRequest? request = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            do
            {
                request = node.GetWebShare().Requests.FirstOrDefault();
                if (request is not null)
                    break;
                await Task.Delay(50);
            }
            while (DateTime.UtcNow < deadline);
            Assert.IsNotNull(request);
            Assert.IsTrue(request.Pending);
            Assert.IsTrue(node.AcceptWebShareRequest(request.SessionId));
            using var prepared = await prepareTask;
            prepared.EnsureSuccessStatusCode();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task PinIsRequiredWhenConfigured()
    {
        var root = TestDirectory.Create();
        var port = GetFreePort();
        await using var node = CreateNode(root, port);
        try
        {
            await node.StartAsync();
            await node.StartWebShareAsync(
                [new SendTextItem("secret", "secret.txt")],
                new WebShareOptions { AutoAccept = true, Pin = "BtNqca" });
            using var client = CreateClient();
            using var denied = await client.PostAsync($"http://127.0.0.1:{port}/api/localsend/v2/prepare-download", content: null);
            Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
            using var allowed = await client.PostAsync($"http://127.0.0.1:{port}/api/localsend/v2/prepare-download?pin=BtNqca", content: null);
            allowed.EnsureSuccessStatusCode();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task BrowserUploadUsesTheNormalIncomingTransferFlow()
    {
        var root = TestDirectory.Create();
        var port = GetFreePort();
        await using var node = CreateNode(root, port);
        try
        {
            await node.StartAsync();
            await node.StartWebReceiveAsync();
            using var client = CreateClient();

            var page = await client.GetStringAsync($"http://127.0.0.1:{port}/");
            Assert.Contains("prepare-web-upload", page, StringComparison.Ordinal);

            await using var incoming = node.WatchIncomingTransfersAsync().GetAsyncEnumerator();
            var incomingMove = incoming.MoveNextAsync().AsTask();
            var prepareTask = client.PostAsJsonAsync(
                $"http://127.0.0.1:{port}/api/localsend/v2/prepare-web-upload",
                new
                {
                    files = new[]
                    {
                        new { id = "browser-file", fileName = "from-browser.txt", size = 12, fileType = "text/plain" }
                    }
                });

            Assert.IsTrue(await incomingMove);
            var request = incoming.Current;
            Assert.AreEqual(LocalSendDeviceType.Web, request.Sender.DeviceType);
            var offered = Assert.ContainsSingle(request.Items);
            Assert.AreEqual("from-browser.txt", offered.FileName);
            Assert.AreEqual(12L, offered.Size);
            var acceptTask = node.AcceptAsync(request.RequestId);
            Assert.IsEmpty(node.GetPendingIncomingTransfers());

            using var preparedResponse = await prepareTask;
            preparedResponse.EnsureSuccessStatusCode();
            using var prepared = JsonDocument.Parse(await preparedResponse.Content.ReadAsStringAsync());
            var sessionId = prepared.RootElement.GetProperty("sessionId").GetString();
            var token = prepared.RootElement.GetProperty("files").GetProperty("browser-file").GetString();
            using var content = new ByteArrayContent("browser-body"u8.ToArray());
            using var upload = await client.PostAsync(
                $"http://127.0.0.1:{port}/api/localsend/v2/upload?sessionId={sessionId}&fileId=browser-file&token={token}",
                content);
            upload.EnsureSuccessStatusCode();

            var result = await acceptTask;
            _ = result.RequireCompleted();
            Assert.AreEqual("browser-body", await File.ReadAllTextAsync(Path.Combine(root, "downloads", "from-browser.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static LocalSendNode CreateNode(string root, int port) => new(new LocalSendOptions
    {
        Alias = "Tonarink",
        DataDirectory = Path.Combine(root, "data"),
        DownloadDirectory = Path.Combine(root, "downloads"),
        Port = port,
        EnableHttps = false,
        IncomingDecisionTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(5)
    });

    private static HttpClient CreateClient() => new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
