using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Tonarink.Cli.E2E.Tests;

[TestClass]
[TestCategory("E2E")]
public sealed class PinSecurityProcessTests
{
    [TestMethod, Timeout(90_000)]
    public async Task ConcurrentIncorrectPinsLockTheRealCliReceiverWithoutPublishingFiles()
    {
        await using var receiver = await CliNode.StartAsync("Concurrent PIN receiver 测试");
        await receiver.SuccessAsync("settings", "set", "receive-pin", "2468");
        await receiver.SuccessAsync("settings", "set", "pin-enabled", "true", "--restart");
        await receiver.SuccessAsync("settings", "set", "auto-save", "on");
        var before = (await receiver.SuccessAsync("status")).Data;
        var receiverFingerprint = before.GetProperty("fingerprint").GetString()!;
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=Tonarink owned PIN sender", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        certificateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var generated = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null);
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        var bytes = Encoding.UTF8.GetBytes("Concurrent PIN 🌏\r\n exact bytes \t");
        var metadata = JsonSerializer.Serialize(new
        {
            info = new { alias = "Owned PIN sender", version = "2.2", deviceType = "headless", fingerprint, port = receiver.Port, protocol = "https" },
            files = new Dictionary<string, object>
            {
                ["file"] = new { id = "file", fileName = "verified.txt", size = bytes.Length, fileType = "text/plain" },
            },
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        for (var round = 0; round < 12; round++)
        {
            // One real receiver and certificate, fresh real source addresses.
            // Neither service restarts nor lock expiry can hide lost counters.
            using var client = CreateBoundClient(receiver, IPAddress.Parse($"127.0.0.{20 + round}"), certificate, receiverFingerprint);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = Enumerable.Range(0, 8).Select(async _ =>
            {
                await ready.Task.WaitAsync(deadline.Token);
                using var response = await PrepareAsync(client, metadata, "0000", deadline.Token);
                return response.StatusCode;
            }).ToArray();
            ready.SetResult();
            var statuses = await Task.WhenAll(attempts).WaitAsync(deadline.Token);
            Assert.HasCount(3, statuses.Where(status => status == HttpStatusCode.Unauthorized),
                $"CLI source IP round {round}: {string.Join(", ", statuses)}");
            Assert.HasCount(5, statuses.Where(status => status == HttpStatusCode.TooManyRequests));
            foreach (var pin in new string?[] { "2468", null })
            {
                using var locked = await PrepareAsync(client, metadata, pin, deadline.Token);
                Assert.AreEqual(HttpStatusCode.TooManyRequests, locked.StatusCode);
            }
        }
        await receiver.WaitForNoIncomingAsync();
        Assert.IsEmpty((await receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.IsFalse(Directory.Exists(receiver.Downloads) && Directory.EnumerateFiles(receiver.Downloads, "*", SearchOption.AllDirectories).Any());

        using var independent = CreateBoundClient(receiver, IPAddress.Parse("127.0.0.100"), certificate, receiverFingerprint);
        using var preparedResponse = await PrepareAsync(independent, metadata, "2468", deadline.Token);
        Assert.AreEqual(HttpStatusCode.OK, preparedResponse.StatusCode);
        using var prepared = JsonDocument.Parse(await preparedResponse.Content.ReadAsStringAsync(deadline.Token));
        var session = prepared.RootElement.GetProperty("sessionId").GetString();
        var token = prepared.RootElement.GetProperty("files").GetProperty("file").GetString();
        using var content = new ByteArrayContent(bytes);
        using var upload = await independent.PostAsync($"/api/localsend/v2/upload?sessionId={session}&fileId=file&token={token}", content, deadline.Token);
        Assert.AreEqual(HttpStatusCode.OK, upload.StatusCode);
        using var original = CreateBoundClient(receiver, IPAddress.Parse("127.0.0.20"), certificate, receiverFingerprint);
        using var stillLocked = await PrepareAsync(original, metadata, "2468", deadline.Token);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, stillLocked.StatusCode, "Another source's success must not unlock the first address.");
        await receiver.WaitForNoIncomingAsync();
        Assert.HasCount(1, (await receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        var file = Assert.ContainsSingle(Directory.EnumerateFiles(receiver.Downloads, "*", SearchOption.AllDirectories));
        Assert.AreEqual("verified.txt", Path.GetFileName(file));
        Assert.AreSequenceEqual(bytes, await File.ReadAllBytesAsync(file, deadline.Token));
        var after = (await receiver.SuccessAsync("status")).Data;
        Assert.AreEqual("Running", after.GetProperty("state").GetString());
        Assert.AreEqual(receiverFingerprint, after.GetProperty("fingerprint").GetString());
    }

    private static async Task<HttpResponseMessage> PrepareAsync(HttpClient client, string metadata, string? pin, CancellationToken cancellation)
    {
        using var content = new StringContent(metadata, Encoding.UTF8, "application/json");
        return await client.PostAsync("/api/localsend/v2/prepare-upload" + (pin is null ? "" : "?pin=" + pin), content, cancellation);
    }

    private static HttpClient CreateBoundClient(CliNode receiver, IPAddress address, X509Certificate2 certificate, string fingerprint)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection { certificate },
                RemoteCertificateValidationCallback = (_, peer, _, _) => peer is not null
                    && Convert.ToHexString(SHA256.HashData(peer.GetRawCertData())).Equals(fingerprint, StringComparison.OrdinalIgnoreCase),
            },
            ConnectCallback = async (context, cancellation) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    socket.Bind(new IPEndPoint(address, 0));
                    await socket.ConnectAsync(context.DnsEndPoint, cancellation);
                    Assert.AreEqual(address, ((IPEndPoint)socket.LocalEndPoint!).Address);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            },
        };
        return new HttpClient(handler) { BaseAddress = new Uri(receiver.Target), Timeout = TimeSpan.FromSeconds(10) };
    }
}
