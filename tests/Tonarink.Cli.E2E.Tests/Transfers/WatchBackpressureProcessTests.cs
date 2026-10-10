using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tonarink.Cli.E2E.Tests;

[TestClass]
[TestCategory("E2E")]
public sealed class WatchBackpressureProcessTests
{
    [TestMethod, Timeout(90_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LargeRealTextWatchKeepsTheHostAndOfferUsableWhetherTheClientReads(bool reads)
    {
        await using var sender = await CliNode.StartAsync("Watch sender 测试");
        await using var receiver = await CliNode.StartAsync("Watch receiver 测试");
        var before = (await receiver.SuccessAsync("status")).Data;
        var text = "start 🌏\r\n" + new string('文', 600_000) + "\n end \t";
        var sending = sender.RunAsync(["send", "--target", receiver.Target, "--text", "-"],
            input: text, timeoutSeconds: 25);
        JsonElement? offer = null;
        try
        {
            offer = await receiver.WaitForIncomingAsync();
            Assert.AreEqual(text, offer.Value.GetProperty("text").GetString());
            await using var pipe = await ConnectWatchAsync(receiver.Profile, reads);
            using var headerDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var length = await ReadLengthAsync(pipe, headerDeadline.Token);
            Assert.IsGreaterThan(1_048_576, length, "This must be an actual large peer payload, not a small unblocked frame.");

            // The receiver stays operational while the first watch body is
            // unread. All CLI commands target this owned profile and daemon.
            await AssertSameRunningNodeAsync(receiver, before);
            CliInvocation accepted;
            if (reads)
            {
                using var bodyDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var incoming = await ReadBodyAsync(pipe, length, bodyDeadline.Token);
                Assert.AreEqual("incoming", incoming.GetProperty("type").GetString());
                Assert.AreEqual(0, incoming.GetProperty("exitCode").GetInt32());
                var watchedOffer = Assert.ContainsSingle(incoming.GetProperty("data").EnumerateArray().ToArray());
                Assert.AreEqual(offer.Value.GetProperty("id").GetString(), watchedOffer.GetProperty("id").GetString());
                Assert.AreEqual(text, watchedOffer.GetProperty("text").GetString());
                Assert.AreEqual(offer.Value.GetProperty("bytes").GetInt64(), watchedOffer.GetProperty("bytes").GetInt64());
                accepted = await receiver.SuccessAsync("receive", "accept", offer.Value.GetProperty("id").GetString()!);

                // A healthy reader has no idle deadline. The production watch
                // lasts twelve seconds, longer than the ten-second write budget.
                using var finalDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var finalLength = await ReadLengthAsync(pipe, finalDeadline.Token);
                var final = await ReadBodyAsync(pipe, finalLength, finalDeadline.Token);
                Assert.AreEqual("result", final.GetProperty("type").GetString());
                Assert.AreEqual(0, final.GetProperty("exitCode").GetInt32());
                Assert.AreEqual(0, await pipe.ReadAsync(new byte[1], finalDeadline.Token), "The successful final must be the last frame.");
            }
            else
            {
                // Deliberately keep a valid connection open without reading its
                // body for longer than the production write deadline. This is
                // the fault stimulus, not a sleep used to guess readiness.
                var unreadInterval = Stopwatch.StartNew();
                var holdFor = TimeSpan.FromSeconds(12);
                while (unreadInterval.Elapsed < holdFor)
                {
                    var remaining = holdFor - unreadInterval.Elapsed;
                    await Task.Delay(remaining > TimeSpan.FromMilliseconds(10) ? remaining : TimeSpan.FromMilliseconds(10));
                }
                Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(12), unreadInterval.Elapsed);
                using var drainDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var delivered = await DrainClosedFrameAsync(pipe, length, drainDeadline.Token);
                Assert.IsLessThan(length, delivered, "Reading after the write deadline must not revive the expired frame.");

                await AssertSameRunningNodeAsync(receiver, before);
                var stillPending = Assert.ContainsSingle((await receiver.SuccessAsync("receive", "list")).Data.EnumerateArray().ToArray());
                Assert.AreEqual(offer.Value.GetProperty("id").GetString(), stillPending.GetProperty("id").GetString());
                Assert.AreEqual(text, stillPending.GetProperty("text").GetString());
                accepted = await receiver.SuccessAsync("receive", "accept", stillPending.GetProperty("id").GetString()!);
            }

            var sent = await sending;
            Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
            Assert.AreEqual("Completed", sent.Data.GetProperty("state").GetString());
            Assert.AreEqual("Completed", accepted.Data.GetProperty("state").GetString());
            Assert.AreEqual(offer.Value.GetProperty("transferId").GetString(), accepted.Data.GetProperty("transferId").GetString());
            Assert.AreEqual(text, accepted.Data.GetProperty("text").GetString());
            await receiver.WaitForNoIncomingAsync();
            Assert.IsEmpty((await receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
            Assert.IsFalse(Directory.Exists(receiver.Downloads)
                && Directory.EnumerateFiles(receiver.Downloads, "*", SearchOption.AllDirectories).Any());
            await AssertSameRunningNodeAsync(receiver, before);
        }
        finally
        {
            // Observe the owned send even if an earlier watch assertion fails.
            // Never abandon its process while the profile is being disposed.
            if (!sending.IsCompleted && offer is { } pending)
            {
                try { _ = await receiver.RunAsync(["cancel", pending.GetProperty("transferId").GetString()!]); }
                catch (Exception exception) { Console.WriteLine($"Owned offer cleanup: {exception.Message}"); }
            }
            try { _ = await sending.WaitAsync(TimeSpan.FromSeconds(35)); }
            catch (Exception exception) { Console.WriteLine($"Owned sender cleanup: {exception.Message}"); }
        }
    }

    private static async Task AssertSameRunningNodeAsync(CliNode receiver, JsonElement before)
    {
        var current = (await receiver.SuccessAsync("--no-start", "status")).Data;
        Assert.AreEqual("Running", current.GetProperty("state").GetString());
        Assert.AreEqual(before.GetProperty("fingerprint").GetString(), current.GetProperty("fingerprint").GetString());
        Assert.AreEqual(before.GetProperty("port").GetInt32(), current.GetProperty("port").GetInt32());
        Assert.AreEqual(before.GetProperty("name").GetString(), current.GetProperty("name").GetString());
    }

    private static async Task<NamedPipeClientStream> ConnectWatchAsync(string profile, bool reads)
    {
        // Exercise the external IPC wire rather than invoking a managed command
        // handler in the controller. The selected target may be a Native AOT
        // executable; these are only the public envelope/framing fields.
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile));
        var pipeName = "Tonarink.Cli." + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? directory.ToUpperInvariant() : directory)))[..24];
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pipe.ConnectAsync(deadline.Token);
            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                arguments = reads ? new[] { "receive", "watch", "--seconds", "12" } : ["receive", "watch"],
                workingDirectory = profile,
                language = "en-US",
            });
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
            await pipe.WriteAsync(header, deadline.Token);
            await pipe.WriteAsync(body, deadline.Token);
            await pipe.FlushAsync(deadline.Token);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    private static async Task<int> ReadLengthAsync(Stream pipe, CancellationToken token)
    {
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Assert.IsTrue(length is > 0 and <= 4 * 1024 * 1024, $"Invalid external IPC frame length: {length}");
        return length;
    }

    private static async Task<JsonElement> ReadBodyAsync(Stream pipe, int length, CancellationToken token)
    {
        var body = new byte[length];
        await pipe.ReadExactlyAsync(body, token);
        using var parsed = JsonDocument.Parse(body);
        return parsed.RootElement.Clone();
    }

    private static async Task<int> DrainClosedFrameAsync(Stream pipe, int length, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        var delivered = 0;
        try
        {
            while (delivered < length)
            {
                var count = await pipe.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, length - delivered)), token);
                if (count == 0) return delivered;
                delivered += count;
            }
        }
        catch (IOException)
        {
            // A reset after the deadline is equivalent to EOF on Unix pipes.
            return delivered;
        }
        return delivered;
    }
}
