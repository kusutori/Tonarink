using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Tonarink.Cli.E2E.Tests;

[TestClass]
[TestCategory("E2E")]
public sealed class ReceiveChecksumProcessTests
{
    [TestMethod, Timeout(60_000)]
    [DataRow("default")]
    [DataRow("command-override")]
    [DataRow("saved-setting")]
    [DataRow("length-mismatch")]
    public async Task ReceiveChecksumPolicyControlsRealUploadWithoutDisablingLengthChecks(string policy)
    {
        await using var receiver = await CliNode.StartAsync("Checksum receiver 测试");
        var receiverFingerprint = (await receiver.SuccessAsync("status")).Data.GetProperty("fingerprint").GetString()!;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Tonarink owned test sender", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        // Match the production identity's PKCS#12 loading path. The directly
        // generated in-memory key cannot acquire Schannel credentials on Windows.
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null);
        var senderFingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        using var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            // Pin this isolated receiver; never bypass validation for another host.
            ServerCertificateCustomValidationCallback = (_, peer, _, _) => peer is not null
                && Convert.ToHexString(SHA256.HashData(peer.RawData)).Equals(receiverFingerprint, StringComparison.OrdinalIgnoreCase),
        };
        handler.ClientCertificates.Add(certificate);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(receiver.Target), Timeout = TimeSpan.FromSeconds(20) };
        if (policy == "saved-setting") await receiver.SuccessAsync("settings", "set", "receive-checksums", "false");
        var skip = policy is "command-override" or "length-mismatch";
        var shouldAccept = policy is "command-override" or "saved-setting";
        var bytes = Encoding.UTF8.GetBytes("payload 🌏\r\n has a deliberately incorrect declared digest");
        var (upload, accepted) = await UploadThroughCliAsync(receiver, client, senderFingerprint, "checksum 测试.bin", bytes,
            new string('0', 64), skip, policy == "length-mismatch" ? bytes.Length + 1 : bytes.Length);

        if (shouldAccept)
            Assert.AreEqual(HttpStatusCode.OK, upload);
        else
            Assert.IsFalse((int)upload is >= 200 and < 300, $"Invalid content must be rejected, received HTTP {(int)upload}.");
        Assert.AreEqual(shouldAccept ? 0 : 1, accepted.Process.ExitCode, accepted.Process.StandardOutput);
        Assert.AreEqual(shouldAccept ? "Completed" : "Failed", accepted.Data.GetProperty("state").GetString());
        Assert.IsFalse(accepted.Data.TryGetProperty("text", out _));
        if (shouldAccept)
        {
            Assert.IsFalse(accepted.Data.TryGetProperty("error", out _));
            Assert.AreSequenceEqual(bytes, await File.ReadAllBytesAsync(Path.Combine(receiver.Downloads, "checksum 测试.bin")));
            var history = Assert.ContainsSingle((await receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
            Assert.AreEqual("checksum 测试.bin", history.GetProperty("fileName").GetString());
            Assert.AreEqual((long)bytes.Length, history.GetProperty("size").GetInt64());
        }
        else
        {
            // CLI callers must receive a failure diagnostic, not a particular
            // English sentence. Core's checksum/length tests assert the stable
            // structured failure codes independently of translated messages.
            Assert.IsFalse(string.IsNullOrWhiteSpace(accepted.Data.GetProperty("error").GetString()));
            Assert.IsEmpty(Directory.EnumerateFiles(receiver.Downloads, "*", SearchOption.AllDirectories).ToArray());
            Assert.IsEmpty((await receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        }
        await receiver.WaitForNoIncomingAsync();
        Assert.IsEmpty(Directory.EnumerateFiles(receiver.Downloads, "*.part-*", SearchOption.AllDirectories).ToArray());
        var expectedSetting = policy == "saved-setting" ? "false" : "true";
        Assert.AreEqual(expectedSetting, (await receiver.SuccessAsync("settings", "get", "receive-checksums")).Data[0].GetProperty("value").GetString());
        using (var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(receiver.Profile, "settings.json"))))
            Assert.AreEqual(policy != "saved-setting", saved.RootElement.GetProperty("receiveChecksums").GetBoolean());

        // Reuse the same host after failure/success. Verification must still work
        // and no consumed token/session or partial-file lease may prevent it.
        await receiver.SuccessAsync("settings", "set", "receive-checksums", "true");
        var (recoveryUpload, recovery) = await UploadThroughCliAsync(receiver, client, senderFingerprint, "verified.bin", bytes,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), false, bytes.Length);
        Assert.AreEqual(HttpStatusCode.OK, recoveryUpload);
        Assert.AreEqual(0, recovery.Process.ExitCode, recovery.Process.StandardOutput);
        Assert.AreEqual("Completed", recovery.Data.GetProperty("state").GetString());
        Assert.AreSequenceEqual(bytes, await File.ReadAllBytesAsync(Path.Combine(receiver.Downloads, "verified.bin")));
        Assert.HasCount(shouldAccept ? 2 : 1, (await receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.HasCount(shouldAccept ? 2 : 1, Directory.EnumerateFiles(receiver.Downloads, "*", SearchOption.AllDirectories).ToArray());
        await receiver.WaitForNoIncomingAsync();
        Assert.AreEqual("Running", (await receiver.SuccessAsync("status")).Data.GetProperty("state").GetString());
    }

    private static async Task<(HttpStatusCode Upload, CliInvocation Accepted)> UploadThroughCliAsync(
        CliNode receiver, HttpClient client, string senderFingerprint, string name, byte[] bytes, string digest, bool skip, int declaredSize)
    {
        // Public wire fields, not a copied Core client/parser. A real TLS sender
        // advertises its client-certificate fingerprint and a bad digest/length.
        var metadata = JsonSerializer.Serialize(new
        {
            info = new { alias = "Checksum sender", version = "2.2", deviceType = "headless", fingerprint = senderFingerprint, port = receiver.Port, protocol = "https" },
            files = new Dictionary<string, object>
            {
                ["file"] = new { id = "file", fileName = name, size = declaredSize, fileType = "application/octet-stream", sha256 = digest },
            },
        });
        using var payload = new StringContent(metadata, Encoding.UTF8, "application/json");
        var preparing = client.PostAsync("/api/localsend/v2/prepare-upload", payload);
        JsonElement incoming;
        try { incoming = await receiver.WaitForIncomingAsync(); }
        catch
        {
            using var failedPrepare = await preparing;
            Console.WriteLine($"Prepare response: {failedPrepare.StatusCode}: {await failedPrepare.Content.ReadAsStringAsync()}");
            throw;
        }
        Assert.AreEqual(senderFingerprint, incoming.GetProperty("fingerprint").GetString());
        var accepting = receiver.RunAsync(["receive", "accept", incoming.GetProperty("id").GetString()!, .. skip ? new[] { "--no-checksum" } : Array.Empty<string>()]);
        using var preparedResponse = await preparing;
        Assert.AreEqual(HttpStatusCode.OK, preparedResponse.StatusCode, await preparedResponse.Content.ReadAsStringAsync());
        using var prepared = JsonDocument.Parse(await preparedResponse.Content.ReadAsStringAsync());
        var session = prepared.RootElement.GetProperty("sessionId").GetString();
        var token = prepared.RootElement.GetProperty("files").GetProperty("file").GetString();
        using var body = new ByteArrayContent(bytes);
        using var uploading = await client.PostAsync($"/api/localsend/v2/upload?sessionId={session}&fileId=file&token={token}", body);
        return (uploading.StatusCode, await accepting);
    }
}
