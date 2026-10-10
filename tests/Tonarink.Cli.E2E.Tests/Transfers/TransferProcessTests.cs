using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tonarink.Cli.E2E.Tests;

[TestClass]
[TestCategory("E2E")]
public sealed class TransferProcessTests
{
    private CliNode _sender = null!, _receiver = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _sender = await CliNode.StartAsync("Sender 测试");
        _receiver = await CliNode.StartAsync("Receiver 测试");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        try
        {
            if (_receiver is not null) await _receiver.DisposeAsync();
        }
        finally
        {
            if (_sender is not null) await _sender.DisposeAsync();
        }
    }

    [TestMethod, Timeout(60_000)]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task LiteralAndStdinTextRemainAvailableInIncomingAndAcceptedJson(bool stdin, bool empty)
    {
        var text = empty ? "" : "\t  起始 \"quote\" 🌏\r\nline two\n结束 \t\r\n";
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "--text", stdin ? "-" : text],
            input: stdin ? text : null);
        var incoming = await _receiver.WaitForIncomingAsync();
        var accepted = await _receiver.SuccessAsync("receive", "accept", incoming.GetProperty("id").GetString()!);
        var sent = await sending;
        Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
        Assert.AreEqual("Completed", sent.Data.GetProperty("state").GetString());
        Assert.AreEqual("Completed", accepted.Data.GetProperty("state").GetString());
        Assert.AreEqual(text, incoming.GetProperty("text").GetString());
        Assert.AreEqual(text, accepted.Data.GetProperty("text").GetString());
        if (!empty) Assert.IsFalse(accepted.Result.GetProperty("message").GetString()!.Contains(text, StringComparison.Ordinal));
        Assert.AreEqual((long)Encoding.UTF8.GetByteCount(text), incoming.GetProperty("bytes").GetInt64());
        Assert.AreSequenceEqual(new[] { "message.txt" }, incoming.GetProperty("files").EnumerateArray().Select(item => item.GetString()));
        AssertNoReceivedFiles();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        await _receiver.WaitForNoIncomingAsync();
    }

    [TestMethod, Timeout(60_000)]
    public async Task LargeUnicodeStdinTextFitsIpcWithoutDuplicatingContentInMessages()
    {
        var text = "  开始\n" + new string('文', 600_000) + "\n结束 🌏\t ";
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "--text", "-"], input: text);
        var incoming = await _receiver.WaitForIncomingAsync();
        var accepted = await _receiver.SuccessAsync("receive", "accept", incoming.GetProperty("id").GetString()!);
        var sent = await sending;
        Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
        Assert.AreEqual(text, incoming.GetProperty("text").GetString());
        Assert.AreEqual(text, accepted.Data.GetProperty("text").GetString());
        Assert.IsTrue(accepted.Result.GetProperty("message").GetString()!.Length < 1024);
        AssertNoReceivedFiles();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        await _receiver.WaitForNoIncomingAsync();
    }

    [TestMethod, Timeout(60_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeclinedOrCancelledTextDoesNotReportReceivedContent(bool cancel)
    {
        const string text = "do not accept 🌏\n private test content";
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "--text", text]);
        var incoming = await _receiver.WaitForIncomingAsync();
        var decision = cancel
            ? await _receiver.SuccessAsync("cancel", incoming.GetProperty("transferId").GetString()!)
            : await _receiver.SuccessAsync("receive", "decline", incoming.GetProperty("id").GetString()!);
        var rejected = await sending;
        Assert.AreEqual(4, rejected.Process.ExitCode, rejected.Process.StandardOutput);
        Assert.IsFalse(decision.Result.TryGetProperty("data", out _));
        Assert.IsFalse(rejected.Result.TryGetProperty("data", out _));
        Assert.IsFalse(decision.Process.StandardOutput.Contains(text, StringComparison.Ordinal));
        await _receiver.WaitForNoIncomingAsync();
        AssertNoReceivedFiles();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
    }

    [TestMethod, Timeout(60_000)]
    [DataRow("en-US", "Text:")]
    [DataRow("zh-CN", "文本：")]
    public async Task TextOutputShowsOfferedAndAcceptedContentWithoutSendingItToStderr(string language, string label)
    {
        const string text = "  命令行文本 🌏\r\nsecond line\n尾部空白 \t\n";
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "--text", text]);
        var incoming = await _receiver.WaitForIncomingAsync();
        var listed = await _receiver.RunTextOutputAsync(["receive", "list"], language);
        var accepted = await _receiver.RunTextOutputAsync(["receive", "accept", incoming.GetProperty("id").GetString()!], language);
        var sent = await sending;
        Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
        Assert.AreEqual(0, listed.ExitCode, listed.StandardError);
        Assert.AreEqual(0, accepted.ExitCode, accepted.StandardError);
        foreach (var output in new[] { listed, accepted })
        {
            Assert.Contains(label, output.StandardOutput);
            Assert.IsTrue(output.StandardOutput.EndsWith(text + Environment.NewLine, StringComparison.Ordinal), output.StandardOutput);
            Assert.IsFalse(output.StandardError.Contains(text, StringComparison.Ordinal));
        }
        AssertNoReceivedFiles();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        await _receiver.WaitForNoIncomingAsync();
    }

    [TestMethod, Timeout(60_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TextWatchReportsExactContentAndOnlyAutoAcceptEmitsTransfer(bool autoAccept)
    {
        const string text = " watch 🌏\n  文本\t ";
        var offered = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watching = _receiver.RunAsync(["receive", "watch", "--seconds", "6", .. autoAccept ? new[] { "--auto-accept" } : Array.Empty<string>()],
            output: message =>
            {
                if (message.GetProperty("type").GetString() == "incoming")
                    offered.TrySetResult(message.GetProperty("data")[0]);
            });
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "--text", "-"], input: text);
        var incoming = await offered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        CliInvocation? manuallyAccepted = null;
        if (!autoAccept)
        {
            var pending = Assert.ContainsSingle((await _receiver.SuccessAsync("receive", "list")).Data.EnumerateArray().ToArray());
            Assert.AreEqual(incoming.GetProperty("id").GetString(), pending.GetProperty("id").GetString());
            manuallyAccepted = await _receiver.SuccessAsync("receive", "accept", incoming.GetProperty("id").GetString()!);
        }
        var sent = await sending;
        var watched = await watching;
        Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
        Assert.AreEqual(0, watched.Process.ExitCode, watched.Process.StandardOutput);
        Assert.AreEqual(text, incoming.GetProperty("text").GetString());
        Assert.HasCount(1, watched.Messages.Where(message => message.GetProperty("type").GetString() == "incoming").ToArray());
        var transfers = watched.Messages.Where(message => message.GetProperty("type").GetString() == "transfer").ToArray();
        Assert.HasCount(autoAccept ? 1 : 0, transfers);
        var accepted = autoAccept ? transfers[0].GetProperty("data") : manuallyAccepted!.Data;
        Assert.AreEqual("Completed", accepted.GetProperty("state").GetString());
        Assert.AreEqual(incoming.GetProperty("transferId").GetString(), accepted.GetProperty("transferId").GetString());
        Assert.AreEqual(text, accepted.GetProperty("text").GetString());
        AssertNoReceivedFiles();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        await _receiver.WaitForNoIncomingAsync();
    }

    [TestMethod, Timeout(60_000)]
    public async Task FilesAndFolderTransferByRelativeUnicodePathsAndHistoryClearKeepsFiles()
    {
        var content = Enumerable.Range(0, 16384).Select(i => (byte)(i % 251)).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_sender.Profile, "照片 测试.bin"), content);
        Directory.CreateDirectory(Path.Combine(_sender.Profile, "Album", "nested"));
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "Album", "nested", "说明.txt"), "文件内容 🌏");
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "照片 测试.bin", "Album"]);
        var incoming = await _receiver.WaitForIncomingAsync();
        Assert.AreSequenceEqual(new[] { "照片 测试.bin", "Album/nested/说明.txt" },
            incoming.GetProperty("files").EnumerateArray().Select(item => item.GetString()));
        Assert.IsFalse(incoming.TryGetProperty("text", out _));
        var received = await _receiver.SuccessAsync("receive", "accept", incoming.GetProperty("id").GetString()!);
        Assert.IsFalse(received.Data.TryGetProperty("text", out _));
        var sent = await sending;
        Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
        Assert.AreEqual("Completed", sent.Data.GetProperty("state").GetString());
        Assert.AreEqual("Completed", received.Data.GetProperty("state").GetString());
        Assert.Contains(message => message.GetProperty("type").GetString() == "progress", sent.Messages);
        var saved = Path.Combine(_receiver.Downloads, "照片 测试.bin");
        Assert.AreSequenceEqual(SHA256.HashData(content), SHA256.HashData(await File.ReadAllBytesAsync(saved)));
        Assert.AreEqual("文件内容 🌏", await File.ReadAllTextAsync(Path.Combine(_receiver.Downloads, "Album", "nested", "说明.txt")));
        Assert.HasCount(2, (await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        await _receiver.SuccessAsync("app", "quit", "--yes");
        Assert.HasCount(2, (await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        await _receiver.SuccessAsync("history", "clear", "--yes");
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.IsTrue(File.Exists(saved));
    }

    [TestMethod, Timeout(60_000)]
    public async Task RelativeReceiveDirectoryUsesCallerWorkingDirectoryWithoutChangingSavedSettings()
    {
        const string fileName = "目录测试 🌏.bin";
        var content = Encoding.UTF8.GetBytes("custom destination\r\n内容 🌏");
        await File.WriteAllBytesAsync(Path.Combine(_sender.Profile, fileName), content);
        var callerDirectory = Directory.CreateDirectory(Path.Combine(_receiver.Profile, "caller cwd")).FullName;
        var destination = Path.Combine(callerDirectory, "覆盖目录", "nested");
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, fileName]);
        var incoming = await _receiver.WaitForIncomingAsync();
        var accepted = await _receiver.RunAsync(["receive", "accept", incoming.GetProperty("id").GetString()!,
            "--directory", Path.Combine("覆盖目录", "nested")], workingDirectory: callerDirectory);
        Assert.AreEqual(0, accepted.Process.ExitCode, accepted.Process.StandardOutput);
        Assert.AreEqual("Completed", accepted.Data.GetProperty("state").GetString());
        var sent = await sending;
        Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
        var saved = Path.Combine(destination, fileName);
        Assert.AreSequenceEqual(new[] { saved }, accepted.Data.GetProperty("files").EnumerateArray().Select(item => item.GetString()));
        Assert.AreSequenceEqual(content, await File.ReadAllBytesAsync(saved));
        AssertNoReceivedFiles();
        Assert.IsFalse(Directory.Exists(Path.Combine(_receiver.Profile, "覆盖目录")));
        var history = Assert.ContainsSingle((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.AreEqual(saved, history.GetProperty("path").GetString());
        Assert.AreEqual(content.LongLength, history.GetProperty("size").GetInt64());
        await _receiver.WaitForNoIncomingAsync();
        await _receiver.SuccessAsync("app", "quit", "--yes");
        var setting = Assert.ContainsSingle((await _receiver.SuccessAsync("settings", "get", "download-directory")).Data.EnumerateArray().ToArray());
        Assert.AreEqual(_receiver.Downloads, setting.GetProperty("value").GetString());
        Assert.AreEqual(saved, Assert.ContainsSingle((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray())
            .GetProperty("path").GetString());
        Assert.AreSequenceEqual(content, await File.ReadAllBytesAsync(saved));
    }

    [TestMethod, Timeout(60_000)]
    public async Task InvalidReceiveDirectoryAndUnknownDecisionLeaveOfferAvailableForValidAcceptance()
    {
        const string fileName = "recoverable.txt";
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, fileName), "received after correction");
        var obstacle = Path.Combine(_receiver.Profile, "not a directory");
        await File.WriteAllTextAsync(obstacle, "keep this file");
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, fileName]);
        var incoming = await _receiver.WaitForIncomingAsync();
        var requestId = incoming.GetProperty("id").GetString()!;
        var badDirectory = await _receiver.RunAsync(["receive", "accept", requestId, "--directory", obstacle]);
        Assert.AreEqual(1, badDirectory.Process.ExitCode, badDirectory.Process.StandardOutput);
        Assert.AreEqual("keep this file", await File.ReadAllTextAsync(obstacle));
        var unknownDestination = Path.Combine(_receiver.Profile, "unknown request destination");
        var unknown = Guid.NewGuid().ToString();
        Assert.AreEqual(3, (await _receiver.RunAsync(["receive", "accept", unknown, "--directory", unknownDestination])).Process.ExitCode);
        Assert.AreEqual(3, (await _receiver.RunAsync(["receive", "decline", unknown])).Process.ExitCode);
        Assert.IsFalse(Directory.Exists(unknownDestination));
        Assert.AreEqual(requestId, Assert.ContainsSingle((await _receiver.SuccessAsync("receive", "list")).Data.EnumerateArray().ToArray())
            .GetProperty("id").GetString());
        AssertNoReceivedFiles();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.IsFalse(sending.IsCompleted, "The failed local destination must not decide the remote offer.");
        await _receiver.SuccessAsync("receive", "accept", requestId);
        var sent = await sending;
        Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
        Assert.AreEqual("received after correction", await File.ReadAllTextAsync(Path.Combine(_receiver.Downloads, fileName)));
        await _receiver.WaitForNoIncomingAsync();
        Assert.AreEqual(3, (await _receiver.RunAsync(["receive", "accept", requestId])).Process.ExitCode);
        Assert.AreEqual(3, (await _receiver.RunAsync(["receive", "decline", requestId])).Process.ExitCode);
        Assert.HasCount(1, (await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
    }

    [TestMethod, Timeout(60_000)]
    public async Task HistoryRemovePersistsOnlySelectedDeletionAndNeverDeletesReceivedFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "first.txt"), "first content");
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "second.txt"), "second content");
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "first.txt", "second.txt"]);
        var incoming = await _receiver.WaitForIncomingAsync();
        await _receiver.SuccessAsync("receive", "accept", incoming.GetProperty("id").GetString()!);
        var sent = await sending;
        Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
        var history = (await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray();
        Assert.HasCount(2, history);
        var first = history.Single(item => item.GetProperty("fileName").GetString() == "first.txt");
        var second = history.Single(item => item.GetProperty("fileName").GetString() == "second.txt");
        var originalIds = history.Select(item => item.GetProperty("id").GetString()).Order().ToArray();
        var unknown = await _receiver.RunAsync(["history", "remove", Guid.NewGuid().ToString()]);
        Assert.AreEqual(3, unknown.Process.ExitCode, unknown.Process.StandardOutput);
        Assert.AreSequenceEqual(originalIds, (await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()).Order());
        await _receiver.SuccessAsync("history", "remove", first.GetProperty("id").GetString()!);
        Assert.AreEqual(3, (await _receiver.RunAsync(["history", "remove", first.GetProperty("id").GetString()!])).Process.ExitCode);
        await _receiver.SuccessAsync("app", "quit", "--yes");
        var retained = Assert.ContainsSingle((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.AreEqual(second.GetProperty("id").GetString(), retained.GetProperty("id").GetString());
        Assert.AreEqual(second.GetRawText(), retained.GetRawText());
        Assert.AreEqual("first content", await File.ReadAllTextAsync(first.GetProperty("path").GetString()!));
        Assert.AreEqual("second content", await File.ReadAllTextAsync(second.GetProperty("path").GetString()!));
    }

    [TestMethod, Timeout(60_000)]
    public async Task DevicesListsDirectlyProbedPeerIdentityAndCanSendUsingItsFingerprint()
    {
        var identity = (await _receiver.SuccessAsync("status")).Data;
        var fingerprint = identity.GetProperty("fingerprint").GetString()!;
        // This exercises direct probing, not multicast discovery. Other LAN
        // devices may legitimately be present and are not asserted away.
        await _sender.SuccessAsync("favorites", "add", _receiver.Target);
        var devices = (await _sender.SuccessAsync("devices", "--seconds", "1")).Data.EnumerateArray().ToArray();
        var peer = Assert.ContainsSingle(devices.Where(item => item.GetProperty("fingerprint").GetString() == fingerprint));
        Assert.AreEqual(identity.GetProperty("name").GetString(), peer.GetProperty("name").GetString());
        Assert.AreEqual("Headless", peer.GetProperty("type").GetString());
        Assert.Contains(_receiver.Target, peer.GetProperty("addresses").EnumerateArray().Select(item => item.GetString()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(peer.GetProperty("protocolVersion").GetString()));
        await _sender.SuccessAsync("favorites", "remove", fingerprint);
        Assert.IsEmpty((await _sender.SuccessAsync("favorites", "list")).Data.EnumerateArray().ToArray());
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "known-device.txt"), "known device content");
        var sending = _sender.RunAsync(["send", "--target", fingerprint, "known-device.txt"]);
        var incoming = await _receiver.WaitForIncomingAsync();
        await _receiver.SuccessAsync("receive", "accept", incoming.GetProperty("id").GetString()!);
        var sent = await sending;
        Assert.AreEqual(0, sent.Process.ExitCode, sent.Process.StandardOutput);
        Assert.AreEqual("known device content", await File.ReadAllTextAsync(Path.Combine(_receiver.Downloads, "known-device.txt")));
        var retained = Assert.ContainsSingle((await _sender.SuccessAsync("devices", "--seconds", "1")).Data.EnumerateArray()
            .Where(item => item.GetProperty("fingerprint").GetString() == fingerprint));
        Assert.Contains(_receiver.Target, retained.GetProperty("addresses").EnumerateArray().Select(item => item.GetString()));
    }

    [TestMethod, Timeout(60_000)]
    public async Task DeclineRemovesPendingRequestAndPublishesNoFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "declined.txt"), "not accepted");
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "declined.txt"]);
        var request = await _receiver.WaitForIncomingAsync();
        await _receiver.SuccessAsync("receive", "decline", request.GetProperty("id").GetString()!);
        Assert.AreEqual(4, (await sending).Process.ExitCode);
        Assert.IsEmpty((await _receiver.SuccessAsync("receive", "list")).Data.EnumerateArray().ToArray());
        Assert.IsFalse(File.Exists(Path.Combine(_receiver.Downloads, "declined.txt")));
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
    }

    [TestMethod, Timeout(60_000)]
    public async Task ReceiverCancelRemovesPendingOfferAndUnknownTransferDoesNotAffectIt()
    {
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "cancel.txt"), "never publish");
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "cancel.txt"]);
        var incoming = await _receiver.WaitForIncomingAsync();
        var transferId = incoming.GetProperty("transferId").GetString()!;
        var unknown = await _receiver.RunAsync(["cancel", Guid.NewGuid().ToString()]);
        Assert.AreEqual(3, unknown.Process.ExitCode, unknown.Process.StandardOutput);
        Assert.AreEqual(incoming.GetProperty("id").GetString(),
            Assert.ContainsSingle((await _receiver.SuccessAsync("receive", "list")).Data.EnumerateArray().ToArray())
                .GetProperty("id").GetString());
        // Incoming and outgoing transfer IDs are different: cancel on the receiver.
        await _receiver.SuccessAsync("cancel", transferId);
        var declined = await sending;
        Assert.AreEqual(4, declined.Process.ExitCode, declined.Process.StandardOutput);
        await _receiver.WaitForNoIncomingAsync();
        Assert.AreEqual(3, (await _receiver.RunAsync(["cancel", transferId])).Process.ExitCode);
        AssertNoReceivedFiles();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.AreEqual("Running", (await _sender.SuccessAsync("status")).Data.GetProperty("state").GetString());
        Assert.AreEqual("Running", (await _receiver.SuccessAsync("status")).Data.GetProperty("state").GetString());
    }

    [TestMethod, Timeout(60_000)]
    public async Task ClientTimeoutCancelsPendingSendAndLeavesBothHostsUsable()
    {
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "timeout.txt"), "never publish");
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "timeout.txt"], timeoutSeconds: 3);
        _ = await _receiver.WaitForIncomingAsync();
        var timedOut = await sending;
        Assert.AreEqual(130, timedOut.Process.ExitCode, timedOut.Process.StandardOutput);
        await _receiver.WaitForNoIncomingAsync();
        AssertNoReceivedFiles();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.AreEqual("Running", (await _sender.SuccessAsync("status")).Data.GetProperty("state").GetString());
        Assert.AreEqual("Running", (await _receiver.SuccessAsync("status")).Data.GetProperty("state").GetString());
        // Exercise the same hosts after cancellation, rather than just checking a flag.
        await _receiver.SuccessAsync("settings", "set", "auto-save", "on");
        await _sender.SuccessAsync("send", "--target", _receiver.Target, "timeout.txt");
        Assert.AreEqual("never publish", await File.ReadAllTextAsync(Path.Combine(_receiver.Downloads, "timeout.txt")));
    }

    [TestMethod, Timeout(60_000)]
    public async Task SenderCancelUsesLiveProgressIdAndRemovesTheRemoteOffer()
    {
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "sender-cancel.txt"), "not accepted");
        var progress = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = _sender.RunAsync(["send", "--target", _receiver.Target, "sender-cancel.txt"], output: message =>
        {
            if (message.GetProperty("type").GetString() == "progress"
                && message.GetProperty("data").GetProperty("state").GetString() == "WaitingForAcceptance")
                progress.TrySetResult(message.GetProperty("data"));
        });
        var incoming = await _receiver.WaitForIncomingAsync();
        var live = await progress.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var transferId = live.GetProperty("transferId").GetString()!;
        Assert.AreNotEqual(incoming.GetProperty("transferId").GetString(), transferId);
        await _sender.SuccessAsync("cancel", transferId);
        var cancelled = await sending;
        Assert.AreEqual(130, cancelled.Process.ExitCode, cancelled.Process.StandardOutput);
        Assert.AreEqual("Cancelled", cancelled.Data.GetProperty("state").GetString());
        await _receiver.WaitForNoIncomingAsync();
        AssertNoReceivedFiles();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.AreEqual(3, (await _sender.RunAsync(["cancel", transferId])).Process.ExitCode);
        Assert.AreEqual("Running", (await _sender.SuccessAsync("status")).Data.GetProperty("state").GetString());
        Assert.AreEqual("Running", (await _receiver.SuccessAsync("status")).Data.GetProperty("state").GetString());
    }

    [TestMethod, Timeout(60_000)]
    [DataRow("cancel")]
    [DataRow("stop")]
    [DataRow("restart")]
    public async Task ActiveBrowserUploadCanBeCancelledOrStoppedWithoutPublishingPartialContent(string action)
    {
        var identity = (await _receiver.SuccessAsync("status")).Data.GetProperty("fingerprint").GetString();
        await _receiver.SuccessAsync("web", "receive");
        using var client = BrowserClient(_receiver);
        const int length = 8 * 1024 * 1024;
        const string fileName = "partial.bin";
        var metadata = JsonSerializer.Serialize(new
        {
            files = new[] { new { id = "partial", fileName, size = length, fileType = "application/octet-stream" } }
        });
        using var payload = new StringContent(metadata, Encoding.UTF8, "application/json");
        var preparing = client.PostAsync("/api/localsend/v2/prepare-web-upload", payload);
        var incoming = await _receiver.WaitForIncomingAsync();
        var progress = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepting = _receiver.RunAsync(["receive", "accept", incoming.GetProperty("id").GetString()!], output: message =>
        {
            if (message.GetProperty("type").GetString() == "progress"
                && message.GetProperty("data").GetProperty("bytesTransferred").GetInt64() > 0)
                progress.TrySetResult(message.GetProperty("data"));
        });
        using var preparedResponse = await preparing;
        preparedResponse.EnsureSuccessStatusCode();
        using var prepared = JsonDocument.Parse(await preparedResponse.Content.ReadAsStringAsync());
        var session = prepared.RootElement.GetProperty("sessionId").GetString();
        var token = prepared.RootElement.GetProperty("files").GetProperty("partial").GetString();
        using var browserCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var body = new PausedUpload(length, browserCancellation.Token);
        var uploading = client.PostAsync($"/api/localsend/v2/upload?sessionId={session}&fileId=partial&token={token}", body, browserCancellation.Token);
        try
        {
            // This is actual Core receive progress, not merely bytes written by
            // the client. The rest of the declared body is held at a gate.
            var receiving = await progress.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(incoming.GetProperty("transferId").GetString(), receiving.GetProperty("transferId").GetString());
            Assert.AreEqual("Transferring", receiving.GetProperty("state").GetString());
            Assert.AreEqual("Receive", receiving.GetProperty("direction").GetString());
            Assert.IsTrue(receiving.GetProperty("bytesTransferred").GetInt64() < length);
            Assert.HasCount(1, Directory.EnumerateFiles(_receiver.Downloads, "*.part-*", SearchOption.AllDirectories).ToArray());
            var cancelling = action == "cancel"
                ? _receiver.RunAsync(["cancel", incoming.GetProperty("transferId").GetString()!])
                : _receiver.RunAsync(["server", action]);
            var cancelled = await accepting;
            Assert.AreEqual(130, cancelled.Process.ExitCode, cancelled.Process.StandardOutput);
            Assert.AreEqual("Cancelled", cancelled.Data.GetProperty("state").GetString());
            // Release the paused browser without requiring the HTTP server to
            // drain a deliberately unfinished request during stop/restart.
            await browserCancellation.CancelAsync();
            Assert.AreEqual(0, (await cancelling).Process.ExitCode);
        }
        finally
        {
            await browserCancellation.CancelAsync();
            try
            {
                using var rejected = await uploading.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            catch (OperationCanceledException) when (browserCancellation.IsCancellationRequested) { }
        }
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Directory.EnumerateFiles(_receiver.Downloads, "*", SearchOption.AllDirectories).Any() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        AssertNoReceivedFiles();
        await _receiver.WaitForNoIncomingAsync();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        var status = (await _receiver.SuccessAsync("status")).Data;
        Assert.IsTrue(status.GetProperty("appRunning").GetBoolean());
        Assert.AreEqual(action == "stop" ? "Stopped" : "Running", status.GetProperty("state").GetString());
        if (action == "stop") await _receiver.SuccessAsync("server", "start");
        Assert.AreEqual(identity, (await _receiver.SuccessAsync("status")).Data.GetProperty("fingerprint").GetString());
        await _receiver.SuccessAsync("settings", "set", "auto-save", "on");
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "after-cancel.txt"), "still healthy");
        await _sender.SuccessAsync("send", "--target", _receiver.Target, "after-cancel.txt");
        Assert.AreEqual("still healthy", await File.ReadAllTextAsync(Path.Combine(_receiver.Downloads, "after-cancel.txt")));
        Assert.HasCount(1, (await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
    }

    [TestMethod, Timeout(60_000)]
    public async Task PinFailuresDoNotPublishAndCorrectPinCanAutoReceive()
    {
        await _receiver.SuccessAsync("settings", "set", "receive-pin", "2468");
        await _receiver.SuccessAsync("settings", "set", "pin-enabled", "true", "--restart");
        await _receiver.SuccessAsync("settings", "set", "auto-save", "on");
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "secret.txt"), "synthetic secret");
        foreach (var pinArguments in new[] { Array.Empty<string>(), new[] { "--pin", "0000" } })
        {
            var denied = await _sender.RunAsync(["send", "--target", _receiver.Target, "secret.txt", .. pinArguments]);
            Assert.AreEqual(5, denied.Process.ExitCode);
            Assert.IsFalse(File.Exists(Path.Combine(_receiver.Downloads, "secret.txt")));
        }
        var hidden = await _receiver.SuccessAsync("settings", "get", "receive-pin");
        Assert.AreEqual("<redacted>", hidden.Data[0].GetProperty("value").GetString());
        await _sender.SuccessAsync("send", "--target", _receiver.Target, "secret.txt", "--pin", "2468");
        Assert.AreEqual("synthetic secret", await File.ReadAllTextAsync(Path.Combine(_receiver.Downloads, "secret.txt")));
    }

    [TestMethod, Timeout(60_000)]
    public async Task WatchAutoAcceptEmitsIncomingTransferAndFinalResult()
    {
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "watch.txt"), "watch payload");
        var watching = _receiver.RunAsync(["receive", "watch", "--auto-accept", "--seconds", "5"]);
        await _sender.SuccessAsync("send", "--target", _receiver.Target, "watch.txt");
        var watched = await watching;
        Assert.AreEqual(0, watched.Process.ExitCode, watched.Process.StandardOutput);
        Assert.Contains(message => message.GetProperty("type").GetString() == "incoming", watched.Messages);
        Assert.Contains(message => message.GetProperty("type").GetString() == "transfer"
            && message.GetProperty("data").GetProperty("state").GetString() == "Completed", watched.Messages);
        Assert.AreEqual("watch payload", await File.ReadAllTextAsync(Path.Combine(_receiver.Downloads, "watch.txt")));
    }

    [TestMethod, Timeout(60_000)]
    public async Task FavoriteIdentityPersistsAcrossHostRestartAndCanBeRemoved()
    {
        await _sender.SuccessAsync("favorites", "add", _receiver.Target);
        var favorites = (await _sender.SuccessAsync("favorites", "list")).Data.EnumerateArray().ToArray();
        var favorite = Assert.ContainsSingle(favorites);
        Assert.AreEqual("Receiver 测试", favorite.GetProperty("name").GetString());
        var fingerprint = favorite.GetProperty("fingerprint").GetString()!;
        await _sender.SuccessAsync("app", "quit", "--yes");
        var restored = Assert.ContainsSingle((await _sender.SuccessAsync("favorites", "list")).Data.EnumerateArray().ToArray());
        Assert.AreEqual(fingerprint, restored.GetProperty("fingerprint").GetString());
        await _receiver.SuccessAsync("settings", "set", "auto-save", "on");
        await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "favorite.txt"), "favorite payload");
        await _sender.SuccessAsync("send", "--target", fingerprint, "favorite.txt");
        Assert.AreEqual("favorite payload", await File.ReadAllTextAsync(Path.Combine(_receiver.Downloads, "favorite.txt")));
        await _sender.SuccessAsync("favorites", "remove", fingerprint);
        Assert.IsEmpty((await _sender.SuccessAsync("favorites", "list")).Data.EnumerateArray().ToArray());
    }

    [TestMethod, Timeout(60_000)]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("empty-folder")]
    public async Task InvalidFileSelectionFailsBeforeCreatingReceiverRequest(string kind)
    {
        string[] paths;
        if (kind == "duplicate")
        {
            await File.WriteAllTextAsync(Path.Combine(_sender.Profile, "same.txt"), "first");
            paths = ["same.txt", "same.txt"];
        }
        else if (kind == "empty-folder")
        {
            Directory.CreateDirectory(Path.Combine(_sender.Profile, "empty"));
            paths = ["empty"];
        }
        else paths = ["does-not-exist.txt"];
        var failed = await _sender.RunAsync(["send", "--target", _receiver.Target, .. paths]);
        // CliException classifies invalid user input as exit 2.
        Assert.AreEqual(2, failed.Process.ExitCode, failed.Process.StandardOutput);
        Assert.IsEmpty((await _receiver.SuccessAsync("receive", "list")).Data.EnumerateArray().ToArray());
    }

    [TestMethod, Timeout(60_000)]
    public async Task WebShareStdinUsesUtf8AndCliApprovesAndDeclinesRealBrowserRequests()
    {
        const string text = "CLI 标准输入 🌏\nnext line";
        var started = await _sender.RunAsync(["web", "send", "--text", "-"], text);
        Assert.AreEqual(0, started.Process.ExitCode, started.Process.StandardOutput);
        Assert.IsTrue(started.Data.GetProperty("active").GetBoolean());
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(_sender.Target), Timeout = TimeSpan.FromSeconds(15) };
        var preparing = client.PostAsync("/api/localsend/v2/prepare-download", null);
        var session = await WaitForBrowserRequestAsync();
        await _sender.SuccessAsync("web", "accept", session);
        using var prepared = await preparing;
        prepared.EnsureSuccessStatusCode();
        using var response = JsonDocument.Parse(await prepared.Content.ReadAsStringAsync());
        var fileId = response.RootElement.GetProperty("files")[0].GetProperty("id").GetString();
        Assert.AreEqual(text, await client.GetStringAsync($"/api/localsend/v2/download?sessionId={session}&fileId={fileId}"));
        // A separate browser session is intentionally rejected.
        preparing = client.PostAsync("/api/localsend/v2/prepare-download", null);
        session = await WaitForBrowserRequestAsync();
        await _sender.SuccessAsync("web", "decline", session);
        using var declined = await preparing;
        Assert.AreEqual(HttpStatusCode.Forbidden, declined.StatusCode);
        await _sender.SuccessAsync("web", "stop");
        Assert.IsFalse((await _sender.SuccessAsync("web", "status")).Data.GetProperty("active").GetBoolean());
    }

    [TestMethod, Timeout(60_000)]
    public async Task WebReceiveChecksPinAndTokenThenCliAcceptsAndRecordsTheRealUpload()
    {
        const string fileName = "browser 测试.bin";
        var body = Encoding.UTF8.GetBytes("browser payload 🌏");
        var started = await _receiver.SuccessAsync("web", "receive", "--pin", "2468");
        Assert.AreEqual("Receive", started.Data.GetProperty("mode").GetString());
        Assert.IsTrue(started.Data.GetProperty("pinRequired").GetBoolean());
        using var client = BrowserClient(_receiver);
        var metadata = JsonSerializer.Serialize(new
        {
            files = new[] { new { id = "browser-file", fileName, size = body.Length, fileType = "application/octet-stream" } }
        });
        foreach (var pin in new[] { "", "0000" })
        {
            using var payload = new StringContent(metadata, Encoding.UTF8, "application/json");
            using var denied = await client.PostAsync($"/api/localsend/v2/prepare-web-upload?pin={pin}", payload);
            Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.IsEmpty((await _receiver.SuccessAsync("receive", "list")).Data.EnumerateArray().ToArray());
        }
        using var requestBody = new StringContent(metadata, Encoding.UTF8, "application/json");
        var preparing = client.PostAsync("/api/localsend/v2/prepare-web-upload?pin=2468", requestBody);
        var incoming = await _receiver.WaitForIncomingAsync();
        Assert.AreEqual("Web browser", incoming.GetProperty("sender").GetString());
        Assert.AreSequenceEqual(new[] { fileName }, incoming.GetProperty("files").EnumerateArray().Select(item => item.GetString()));
        var accepting = _receiver.RunAsync(["receive", "accept", incoming.GetProperty("id").GetString()!]);
        using var preparedResponse = await preparing;
        preparedResponse.EnsureSuccessStatusCode();
        using var prepared = JsonDocument.Parse(await preparedResponse.Content.ReadAsStringAsync());
        var session = prepared.RootElement.GetProperty("sessionId").GetString();
        var token = prepared.RootElement.GetProperty("files").GetProperty("browser-file").GetString();
        var uploadPath = $"/api/localsend/v2/upload?sessionId={session}&fileId=browser-file&token=";
        using (var badBody = new ByteArrayContent(body))
        using (var forbidden = await client.PostAsync(uploadPath + "incorrect-token", badBody))
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        AssertNoReceivedFiles();
        using (var uploadBody = new ByteArrayContent(body))
        using (var upload = await client.PostAsync(uploadPath + token, uploadBody))
            upload.EnsureSuccessStatusCode();
        var accepted = await accepting;
        Assert.AreEqual(0, accepted.Process.ExitCode, accepted.Process.StandardOutput);
        Assert.AreEqual("Completed", accepted.Data.GetProperty("state").GetString());
        var saved = Path.Combine(_receiver.Downloads, fileName);
        Assert.AreSequenceEqual(SHA256.HashData(body), SHA256.HashData(await File.ReadAllBytesAsync(saved)));
        await _receiver.WaitForNoIncomingAsync();
        var history = Assert.ContainsSingle((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.AreEqual(fileName, history.GetProperty("fileName").GetString());
        Assert.AreEqual(saved, history.GetProperty("path").GetString());
        using (var duplicateBody = new ByteArrayContent(body))
        using (var duplicate = await client.PostAsync(uploadPath + token, duplicateBody))
            Assert.IsTrue(duplicate.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.HasCount(1, Directory.EnumerateFiles(_receiver.Downloads, "*", SearchOption.AllDirectories).ToArray());
        Assert.HasCount(1, (await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
    }

    [TestMethod, Timeout(60_000)]
    public async Task WebReceiveDeclineAndStopPublishNothingAndRejectNewBrowserUploads()
    {
        await _receiver.SuccessAsync("web", "receive");
        using var client = BrowserClient(_receiver);
        const string metadata = "{\"files\":[{\"id\":\"declined\",\"fileName\":\"declined.txt\",\"size\":1,\"fileType\":\"text/plain\"}]}";
        using var payload = new StringContent(metadata, Encoding.UTF8, "application/json");
        var preparing = client.PostAsync("/api/localsend/v2/prepare-web-upload", payload);
        var incoming = await _receiver.WaitForIncomingAsync();
        await _receiver.SuccessAsync("receive", "decline", incoming.GetProperty("id").GetString()!);
        using var declined = await preparing;
        Assert.AreEqual(HttpStatusCode.Forbidden, declined.StatusCode);
        await _receiver.WaitForNoIncomingAsync();
        AssertNoReceivedFiles();
        Assert.IsEmpty((await _receiver.SuccessAsync("history", "list")).Data.EnumerateArray().ToArray());
        Assert.IsFalse((await _receiver.SuccessAsync("web", "stop")).Data.GetProperty("active").GetBoolean());
        using var anotherPayload = new StringContent(metadata, Encoding.UTF8, "application/json");
        using var stopped = await client.PostAsync("/api/localsend/v2/prepare-web-upload", anotherPayload);
        Assert.AreEqual(HttpStatusCode.NotFound, stopped.StatusCode);
        Assert.IsEmpty((await _receiver.SuccessAsync("receive", "list")).Data.EnumerateArray().ToArray());
        Assert.AreEqual("Running", (await _receiver.SuccessAsync("status")).Data.GetProperty("state").GetString());
    }

    private static HttpClient BrowserClient(CliNode node) => new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    { BaseAddress = new Uri(node.Target), Timeout = TimeSpan.FromSeconds(15) };

    private void AssertNoReceivedFiles() => Assert.IsFalse(Directory.Exists(_receiver.Downloads)
        && Directory.EnumerateFiles(_receiver.Downloads, "*", SearchOption.AllDirectories).Any());

    private sealed class PausedUpload(int length, CancellationToken cancellation) : HttpContent
    {
        protected override bool TryComputeLength(out long value) { value = length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, cancellation);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            await stream.WriteAsync(new byte[64 * 1024], token);
            await stream.FlushAsync(token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }

    private async Task<string> WaitForBrowserRequestAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            var pending = (await _sender.SuccessAsync("web", "status")).Data.GetProperty("pendingRequests").EnumerateArray().ToArray();
            if (pending.Length > 0) return Assert.ContainsSingle(pending).GetString()!;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        throw new AssertFailedException("Browser session was not exposed by web status.");
    }
}
