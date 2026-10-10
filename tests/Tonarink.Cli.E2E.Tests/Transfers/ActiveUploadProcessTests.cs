using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tonarink.Cli.E2E.Tests;

[TestClass]
[TestCategory("E2E")]
public sealed class ActiveUploadProcessTests
{
    private CliNode _sender = null!, _receiver = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _sender = await CliNode.StartAsync("Active sender");
        _receiver = await CliNode.StartAsync("Active receiver");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        try { if (_receiver is not null) await _receiver.DisposeAsync(); }
        finally { if (_sender is not null) await _sender.DisposeAsync(); }
    }

    [TestMethod, Timeout(60_000)]
    [DataRow("sender", "cancel")]
    [DataRow("receiver", "cancel")]
    [DataRow("sender", "stop")]
    [DataRow("sender", "restart")]
    [DataRow("receiver", "stop")]
    [DataRow("receiver", "restart")]
    public async Task ActiveUploadCancellationOrServiceTransitionLeavesBothHostsUsable(string side, string action)
    {
        const int length = 8 * 1024 * 1024;
        const string fileName = "active-upload.bin";
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        var source = Path.Combine(_sender.Profile, fileName);
        await File.WriteAllBytesAsync(source, bytes);
        var sourceHash = SHA256.HashData(bytes);
        var senderIdentity = (await _sender.SuccessAsync("status")).Data.GetProperty("fingerprint").GetString();
        var receiverIdentity = (await _receiver.SuccessAsync("status")).Data.GetProperty("fingerprint").GetString();
        var sendingProgress = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivingProgress = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using (var relay = new PausedTcpRelay(_receiver.Port))
        {
            var sending = _sender.RunAsync(["--no-start", "send", "--target", relay.Target, fileName],
                output: message => CaptureActiveProgress(message, sendingProgress));
            Task<CliInvocation>? accepting = null, changing = null;
            try
            {
                var incoming = await _receiver.WaitForIncomingAsync();
                accepting = _receiver.RunAsync(["--no-start", "receive", "accept", incoming.GetProperty("id").GetString()!],
                    output: message => CaptureActiveProgress(message, receivingProgress));
                var forwarded = await relay.Paused.WaitAsync(TimeSpan.FromSeconds(10));
                var outgoing = await sendingProgress.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var receiving = await receivingProgress.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.AreEqual("Send", outgoing.GetProperty("direction").GetString());
                Assert.AreEqual("Receive", receiving.GetProperty("direction").GetString());
                Assert.AreEqual(incoming.GetProperty("transferId").GetString(), receiving.GetProperty("transferId").GetString());
                Assert.AreEqual(length, outgoing.GetProperty("totalBytes").GetInt64());
                Assert.AreEqual(length, receiving.GetProperty("totalBytes").GetInt64());
                Assert.IsTrue(outgoing.GetProperty("bytesTransferred").GetInt64() < length);
                Assert.IsTrue(receiving.GetProperty("bytesTransferred").GetInt64() < length);
                Assert.IsTrue(forwarded < length);
                Assert.HasCount(1, Directory.EnumerateFiles(_receiver.Downloads, "*.part-*", SearchOption.AllDirectories).ToArray());
                var transferId = outgoing.GetProperty("transferId").GetString()!;
                Assert.AreNotEqual(incoming.GetProperty("transferId").GetString(), transferId);

                var actor = side == "sender" ? _sender : _receiver;
                var affectedId = side == "sender" ? transferId : incoming.GetProperty("transferId").GetString()!;
                Console.WriteLine($"Before {side} {action}: relay={forwarded}, sender={outgoing}, receiver={receiving}");
                changing = action == "cancel"
                    ? actor.RunAsync(["--no-start", "cancel", affectedId])
                    : actor.RunAsync(["--no-start", "server", action]);
                // Explicit cancellation must finish, not silently fall back to
                // the command's independent 20-second client timeout.
                var sent = await sending.WaitAsync(TimeSpan.FromSeconds(5));
                var received = await accepting.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(0, (await changing).Process.ExitCode);
                Assert.AreEqual(130, sent.Process.ExitCode, sent.Process.StandardOutput);
                Assert.AreEqual("Cancelled", sent.Data.GetProperty("state").GetString());
                Assert.AreEqual(130, received.Process.ExitCode, received.Process.StandardOutput);
                Assert.AreEqual("Cancelled", received.Data.GetProperty("state").GetString());
                Assert.IsEmpty(sent.Data.GetProperty("files").EnumerateArray().ToArray());
                Assert.IsEmpty(received.Data.GetProperty("files").EnumerateArray().ToArray());
                if (action == "cancel")
                {
                    Assert.AreEqual(3, (await _sender.RunAsync(["--no-start", "cancel", transferId])).Process.ExitCode);
                    Assert.AreEqual(3, (await _receiver.RunAsync(["--no-start", "cancel", incoming.GetProperty("transferId").GetString()!])).Process.ExitCode);
                }
                Console.WriteLine($"Actual {side} {action}: relay={forwarded}, sender={outgoing}, receiver={receiving}");
            }
            finally
            {
                // Drop only owned sockets, including when an assertion fails,
                // and observe both invocations before their profiles are deleted.
                await relay.DisposeAsync();
                var invocations = new List<Task<CliInvocation>> { sending };
                if (accepting is not null) invocations.Add(accepting);
                if (changing is not null) invocations.Add(changing);
                await Task.WhenAll(invocations).WaitAsync(TimeSpan.FromSeconds(30));
                foreach (var invocation in invocations)
                    Console.WriteLine($"Owned invocation completed: {invocation.Result.Process.StandardOutput}");
            }
        }

        // No recovery command may start a replacement daemon and hide a crash.
        foreach (var (name, node) in new[] { ("sender", _sender), ("receiver", _receiver) })
        {
            var status = (await node.SuccessAsync("--no-start", "status")).Data;
            Assert.IsTrue(status.GetProperty("appRunning").GetBoolean());
            Assert.AreEqual(action == "stop" && side == name ? "Stopped" : "Running", status.GetProperty("state").GetString());
        }
        Assert.IsEmpty((await _receiver.SuccessAsync("--no-start", "receive", "list")).Data.EnumerateArray().ToArray());
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Directory.EnumerateFiles(_receiver.Downloads, "*", SearchOption.AllDirectories).Any() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.IsEmpty(Directory.EnumerateFiles(_receiver.Downloads, "*", SearchOption.AllDirectories).ToArray());
        Assert.IsEmpty((await _receiver.SuccessAsync("--no-start", "history", "list")).Data.EnumerateArray().ToArray());
        CollectionAssert.AreEqual(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        if (action == "stop")
            await (side == "sender" ? _sender : _receiver).SuccessAsync("--no-start", "server", "start");
        Assert.AreEqual(senderIdentity, (await _sender.SuccessAsync("--no-start", "status")).Data.GetProperty("fingerprint").GetString());
        Assert.AreEqual(receiverIdentity, (await _receiver.SuccessAsync("--no-start", "status")).Data.GetProperty("fingerprint").GetString());

        await _receiver.SuccessAsync("--no-start", "settings", "set", "auto-save", "on");
        await _sender.SuccessAsync("--no-start", "send", "--target", _receiver.Target, fileName);
        CollectionAssert.AreEqual(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(_receiver.Downloads, fileName))));
        Assert.HasCount(1, (await _receiver.SuccessAsync("--no-start", "history", "list")).Data.EnumerateArray().ToArray());
    }

    private static void CaptureActiveProgress(JsonElement message, TaskCompletionSource<JsonElement> target)
    {
        if (message.GetProperty("type").GetString() != "progress") return;
        var data = message.GetProperty("data");
        if (data.GetProperty("state").GetString() == "Transferring" && data.GetProperty("bytesTransferred").GetInt64() > 0)
            target.TrySetResult(data);
    }

    [TestMethod, Timeout(60_000)]
    public async Task ExplicitAddressUsesNewPortInsteadOfCachedEndpoint()
    {
        await _receiver.SuccessAsync("settings", "set", "auto-save", "on");
        await File.WriteAllBytesAsync(Path.Combine(_sender.Profile, "old-port.bin"), [1, 2, 3]);
        await _sender.SuccessAsync("send", "--target", _receiver.Target, "old-port.bin");
        var identity = (await _receiver.SuccessAsync("status")).Data.GetProperty("fingerprint").GetString();
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        Assert.AreNotEqual(_receiver.Port, port);
        await _receiver.SuccessAsync("settings", "set", "port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--restart");
        Assert.AreEqual(identity, (await _receiver.SuccessAsync("status")).Data.GetProperty("fingerprint").GetString());
        await File.WriteAllBytesAsync(Path.Combine(_sender.Profile, "new-port.bin"), [4, 5, 6]);
        var sent = await _sender.RunAsync(["send", "--target", $"https://127.0.0.1:{port}", "new-port.bin"]);
        Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(Path.Combine(_receiver.Downloads, "new-port.bin")));
        var peer = Assert.ContainsSingle((await _sender.SuccessAsync("devices", "--seconds", "1")).Data.EnumerateArray()
            .Where(item => item.GetProperty("fingerprint").GetString() == identity));
        var addresses = peer.GetProperty("addresses").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains(_receiver.Target, addresses);
        Assert.Contains($"https://127.0.0.1:{port}", addresses);
        Assert.HasCount(2, (await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
    }
}
