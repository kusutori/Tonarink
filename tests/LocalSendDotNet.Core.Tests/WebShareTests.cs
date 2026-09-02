using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocalSendDotNet.Core.Tests;

public sealed class WebShareTests
{
    [Fact(Timeout = 20_000)]
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
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            await node.StartWebShareAsync([new SendTextItem("hello", "hello.txt")], new WebShareOptions { AutoAccept = true });
            var page = await client.GetAsync($"http://127.0.0.1:{port}/");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("Tonarink", html, StringComparison.Ordinal);
            Assert.Contains("prepare-download", html, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact(Timeout = 20_000)]
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
            Assert.False(string.IsNullOrWhiteSpace(sessionId));
            Assert.False(string.IsNullOrWhiteSpace(fileId));

            var state = node.GetWebShare();
            Assert.True(state.Active);
            var request = Assert.Single(state.Requests);
            Assert.False(request.Pending);
            Assert.Contains("Firefox", request.DeviceInfo, StringComparison.Ordinal);

            var download = await client.GetStringAsync($"http://127.0.0.1:{port}/api/localsend/v2/download?sessionId={sessionId}&fileId={fileId}");
            Assert.Equal("shared-body", download);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact(Timeout = 20_000)]
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
            Assert.NotNull(request);
            Assert.True(request.Pending);
            Assert.True(node.AcceptWebShareRequest(request.SessionId));
            using var prepared = await prepareTask;
            prepared.EnsureSuccessStatusCode();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact(Timeout = 20_000)]
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
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using var allowed = await client.PostAsync($"http://127.0.0.1:{port}/api/localsend/v2/prepare-download?pin=BtNqca", content: null);
            allowed.EnsureSuccessStatusCode();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact(Timeout = 20_000)]
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
            Assert.Contains("function createFileId()", page, StringComparison.Ordinal);
            Assert.DoesNotContain("id: crypto.randomUUID()", page, StringComparison.Ordinal);

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

            Assert.True(await incomingMove);
            var request = incoming.Current;
            Assert.Equal("Web browser", request.Sender.Alias);
            var acceptTask = node.AcceptAsync(request.RequestId);

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
            Assert.Equal(TransferState.Completed, result.State);
            Assert.Equal("browser-body", await File.ReadAllTextAsync(Path.Combine(root, "downloads", "from-browser.txt")));
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
