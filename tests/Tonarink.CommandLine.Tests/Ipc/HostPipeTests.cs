using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LocalSendDotNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tonarink.Cli;
using Tonarink.TestSupport;

namespace Tonarink.CommandLine.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class HostPipeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentRequestsEnterBeforeReleaseAndKeepTheirResponsesSeparate(bool integrated)
    {
        const int clients = 6;
        var runtime = new RuntimeBoundary(integrated, admissions: clients) { BlockSaves = true };
        await using var host = new PipeFixture(runtime);
        var calls = Enumerable.Range(0, clients).Select(async index =>
        {
            using var pipe = await host.ConnectAsync();
            var value = $"device-{index}-测试";
            var directory = Path.Combine(host.Directory, index.ToString(CultureInfo.InvariantCulture));
            await WriteAsync(pipe, new(["settings", "set", "alias", value], directory, null, "en-US"));
            var response = await ReadAsync(pipe);
            Assert.AreEqual("result", response.Type);
            Assert.AreEqual(0, response.ExitCode);
            Assert.AreEqual(value, response.Message, "One client's result must not appear on another connection.");
        }).ToArray();
        try
        {
            await runtime.AllEntered.Task.WaitAsync(Deadline);
            Assert.HasCount(clients, runtime.Saves);
            Assert.IsTrue(calls.All(call => !call.IsCompleted), "No save can complete before the gate opens.");
            Assert.AreSequenceEqual(Enumerable.Range(0, clients).Select(index => $"device-{index}-测试").Order(),
                runtime.Saves.Select(save => save.Value).Order());
            foreach (var save in runtime.Saves)
                Assert.AreEqual(Path.Combine(host.Directory, save.Value.Split('-')[1]), save.Directory);
        }
        finally
        {
            runtime.Release.TrySetResult();
            await Task.WhenAll(calls).WaitAsync(Deadline);
        }
        Assert.AreEqual(clients, runtime.Committed);
        Assert.IsEmpty(host.Errors);
    }

    [TestMethod]
    public async Task DisconnectCancelsPendingBusinessWorkAndListenerStillAcceptsRequests()
    {
        var runtime = new RuntimeBoundary() { BlockSaves = true };
        await using var host = new PipeFixture(runtime);
        using (var pipe = await host.ConnectAsync())
        {
            await WriteAsync(pipe, SaveRequest(host.Directory));
            await runtime.AllEntered.Task.WaitAsync(Deadline);
            Assert.AreEqual(0, runtime.Committed);
        }
        await runtime.Cancelled.Task.WaitAsync(Deadline);
        Assert.AreEqual(0, runtime.Committed);
        await AssertHealthyAsync(host);
        Assert.IsEmpty(host.Errors);
    }

    [TestMethod]
    public async Task TimeoutProducesCancellationResultAndDoesNotCommitPendingSave()
    {
        var runtime = new RuntimeBoundary() { BlockSaves = true };
        await using var host = new PipeFixture(runtime);
        using var pipe = await host.ConnectAsync();
        await WriteAsync(pipe, new(["--timeout", "1", "settings", "set", "alias", "cancelled"], host.Directory, null, "en-US"));
        await runtime.AllEntered.Task.WaitAsync(Deadline);
        var response = await ReadAsync(pipe);
        Assert.AreEqual("result", response.Type);
        Assert.AreEqual(130, response.ExitCode);
        Assert.AreEqual("Command cancelled or timed out", response.Message);
        await runtime.Cancelled.Task.WaitAsync(Deadline);
        Assert.AreEqual(0, runtime.Committed);
        await AssertHealthyAsync(host);
        Assert.IsEmpty(host.Errors);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisposeClosesListenerAndCancelsConnectedReadOrBusinessWork(bool partialHeader)
    {
        var runtime = new RuntimeBoundary() { BlockSaves = true };
        await using var host = new PipeFixture(runtime);
        using var pipe = await host.ConnectAsync();
        if (partialHeader)
        {
            await pipe.WriteAsync(new byte[] { 42, 0 });
            await pipe.FlushAsync();
        }
        else
        {
            await WriteAsync(pipe, SaveRequest(host.Directory));
            await runtime.AllEntered.Task.WaitAsync(Deadline);
        }
        host.Stop();
        await host.Listener.WaitAsync(Deadline);
        if (!partialHeader)
            await runtime.Cancelled.Task.WaitAsync(Deadline);
        await AssertClosedAsync(pipe);
        Assert.AreEqual(0, runtime.Committed);
        Assert.IsEmpty(host.Errors);
        using var next = host.NewClient();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => next.ConnectAsync(200));
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnreadLargeResponseDoesNotBlockOtherClientsOrHostShutdown(bool integrated)
    {
        var runtime = new RuntimeBoundary(integrated);
        await using var host = new PipeFixture(runtime);
        using var pipe = await host.ConnectAsync();
        var value = new string('x', 2 * 1024 * 1024);
        await WriteAsync(pipe, new(["settings", "set", "alias", value], host.Directory, null, "en-US"));
        using var reading = new CancellationTokenSource(Deadline);
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, reading.Token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Assert.IsTrue(length > value.Length && length < 4 * 1024 * 1024,
            "The production host must begin a valid large response, not fail the frame-size limit.");
        Assert.AreEqual(1, runtime.Committed);
        Assert.AreEqual(value, Assert.ContainsSingle(runtime.Saves).Value);

        // Deliberately stop reading this body. A different real connection must
        // receive its own valid response while the large writer is backpressured.
        await AssertHealthyAsync(host);
        host.Stop();
        await host.Listener.WaitAsync(Deadline);

        // Now drain only the bytes already buffered by the OS. A full result
        // would mean this fixture never exercised an interrupted large write.
        // EOF/reset must arrive promptly, not at the final-delivery timeout.
        using var closeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[16 * 1024];
        var received = 0;
        try
        {
            while (true)
            {
                var count = await pipe.ReadAsync(buffer, closeDeadline.Token);
                if (count == 0) break;
                received += count;
                Assert.IsTrue(received < length, "The large result was not interrupted during shutdown.");
            }
        }
        catch (IOException) { /* Windows broken-pipe and Unix reset both mean the owned peer closed. */ }
        Console.WriteLine($"integrated={integrated}: declared response={length}, buffered body after stop={received}");
        Assert.IsTrue(received < length);
        Assert.IsEmpty(host.Errors);
        using var next = host.NewClient();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => next.ConnectAsync(200));
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FinalDeliveryDeadlineClosesOnlyTheUnreadConnectionWhileHostStaysUsable(bool integrated)
    {
        var runtime = new RuntimeBoundary(integrated);
        await using var host = new PipeFixture(runtime);
        using var pipe = await host.ConnectAsync();
        var value = new string('x', 2 * 1024 * 1024);
        await WriteAsync(pipe, new(["settings", "set", "alias", value], host.Directory, null, "en-US"));
        using var reading = new CancellationTokenSource(Deadline);
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, reading.Token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Assert.IsTrue(length > value.Length && length < 4 * 1024 * 1024);
        Assert.AreEqual(1, runtime.Committed);
        Assert.AreEqual(value, Assert.ContainsSingle(runtime.Saves).Value);
        await AssertHealthyAsync(host);

        // Do not read the body, close the client, set a business --timeout, or
        // stop the host. Intentionally hold the unread condition beyond the
        // production ten-second delivery budget. This is timed fault injection,
        // not a readiness sleep: draining early would unblock the very write
        // being tested. A completed invocation's token is not a reliable signal
        // because System.CommandLine owns and disposes its linked source.
        await Task.Delay(TimeSpan.FromSeconds(12));
        Assert.IsFalse(host.Listener.IsCompleted);
        Assert.AreEqual(0, runtime.QuitCount);

        // After that unread interval, only buffered bytes may remain. Draining
        // must reach EOF/reset promptly and must not rescue a complete response.
        using var closeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[16 * 1024];
        var received = 0;
        try
        {
            while (true)
            {
                var count = await pipe.ReadAsync(buffer, closeDeadline.Token);
                if (count == 0) break;
                received += count;
                Assert.IsTrue(received < length, "The final-delivery deadline did not interrupt the blocked writer.");
            }
        }
        catch (IOException) { /* EOF/reset after the server owns and closes this connection. */ }
        Console.WriteLine($"integrated={integrated}: final-delivery response={length}, buffered body after deadline={received}");
        Assert.IsTrue(received < length);
        Assert.AreEqual(1, runtime.Committed, "Delivery failure must not repeat or roll back an already completed operation.");
        Assert.IsFalse(host.Listener.IsCompleted);
        await AssertHealthyAsync(host);
        Assert.IsEmpty(host.Errors);
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IdleWatchCanOutliveTheFrameDeadlineAndStillDeliverItsFinalResult(bool integrated)
    {
        await using var directory = new TemporaryDirectory();
        await using var node = await StartWatchNodeAsync(directory);
        var runtime = new RuntimeBoundary(integrated) { WatchNode = node, BlockWatchReads = false };
        await using var host = new PipeFixture(runtime);
        using var pipe = await host.ConnectAsync();
        try
        {
            // No incoming records exist: the reader is waiting for output, not
            // blocked writing a frame. A ten-second connection-wide timer would
            // wrongly kill this valid long-running command before it finishes.
            await WriteAsync(pipe, new(["receive", "watch", "--seconds", "12"], host.Directory, null, "en-US"));
            await runtime.WatchStarted.Task.WaitAsync(Deadline);
            await AssertHealthyAsync(host);
            using var reading = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await CliProtocol.ReadAsync(pipe, CliJsonContext.Default.CliResponse, reading.Token);
            Assert.AreEqual("result", response.Type);
            Assert.AreEqual(0, response.ExitCode);
            await runtime.WatchDisposed.Task.WaitAsync(Deadline);
            Assert.AreEqual(1, runtime.WatchDisposals);
            await AssertClosedAsync(pipe);
            Assert.IsFalse(host.Listener.IsCompleted);
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            await AssertHealthyAsync(host);
            Assert.IsEmpty(host.Errors);
        }
        finally
        {
            pipe.Dispose();
            host.Stop();
            runtime.Release.TrySetResult();
            await host.Listener.WaitAsync(Deadline);
            if (runtime.WatchStarted.Task.IsCompleted)
                await runtime.WatchDisposed.Task.WaitAsync(Deadline);
        }
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnreadWatchResponseExpiresDuringBusinessAndReleasesOnlyItsScope(bool integrated)
    {
        await using var directory = new TemporaryDirectory();
        await using var node = await StartWatchNodeAsync(directory);
        var runtime = new RuntimeBoundary(integrated)
        {
            WatchNode = node,
            BlockWatchReads = false,
            WatchIncoming = WatchRequests(64, new string('x', 512 * 1024))
        };
        await using var host = new PipeFixture(runtime);
        using var pipe = await host.ConnectAsync();
        try
        {
            await WriteAsync(pipe, new(["receive", "watch"], host.Directory, null, "en-US"));
            using var reading = new CancellationTokenSource(Deadline);
            var header = new byte[4];
            await pipe.ReadExactlyAsync(header, reading.Token);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            Assert.IsTrue(length > 1024 * 1024 && length < 4 * 1024 * 1024);
            await runtime.WatchStarted.Task.WaitAsync(Deadline);
            Assert.IsFalse(runtime.WatchDisposed.Task.IsCompleted);
            await AssertHealthyAsync(host);

            // Leave this client connected and its first body unread. No watch
            // duration, business timeout, caller cancellation or host shutdown
            // can end it; the real blocked frame must expire during the watch.
            await runtime.WatchDisposed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.AreEqual(1, runtime.WatchDisposals);
            using var closed = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var buffer = new byte[16 * 1024];
            var received = 0;
            try
            {
                while (true)
                {
                    var count = await pipe.ReadAsync(buffer, closed.Token);
                    if (count == 0) break;
                    received += count;
                    Assert.IsTrue(received < length, "The blocked frame was completed instead of being cancelled.");
                }
            }
            catch (IOException) { /* Reset and EOF both mean the owned connection has closed. */ }
            Console.WriteLine($"integrated={integrated}: watch frame={length}, remaining buffered bytes={received}");
            Assert.IsTrue(received < length);
            Assert.IsFalse(host.Listener.IsCompleted);
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            await AssertHealthyAsync(host);
            Assert.AreEqual(0, runtime.Committed);
            Assert.AreEqual(0, runtime.QuitCount);
            Assert.IsEmpty(host.Errors);
        }
        finally
        {
            pipe.Dispose();
            host.Stop();
            runtime.Release.TrySetResult();
            await host.Listener.WaitAsync(Deadline);
            if (runtime.WatchStarted.Task.IsCompleted)
                await runtime.WatchDisposed.Task.WaitAsync(Deadline);
        }
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadingWatchClientGetsEveryIncomingRecordInOrderAndOneFinalResult(bool integrated)
    {
        await using var directory = new TemporaryDirectory();
        await using var node = await StartWatchNodeAsync(directory);
        var incoming = WatchRequests(64, "synthetic.txt");
        var runtime = new RuntimeBoundary(integrated) { WatchNode = node, BlockWatchReads = false, WatchIncoming = incoming };
        await using var host = new PipeFixture(runtime);
        using var pipe = await host.ConnectAsync();
        try
        {
            await WriteAsync(pipe, new(["receive", "watch", "--seconds", "1"], host.Directory, null, "en-US"));
            foreach (var expected in incoming)
            {
                var response = await ReadAsync(pipe);
                Assert.AreEqual("incoming", response.Type);
                Assert.AreEqual(0, response.ExitCode);
                Assert.IsNotNull(response.Data);
                var item = Assert.ContainsSingle(response.Data.Value.EnumerateArray());
                Assert.AreEqual(expected.RequestId, item.GetProperty("id").GetGuid());
                Assert.AreEqual(expected.Items[0].FileName, item.GetProperty("files")[0].GetString());
            }
            var final = await ReadAsync(pipe);
            Assert.AreEqual("result", final.Type);
            Assert.AreEqual(0, final.ExitCode);
            await runtime.WatchDisposed.Task.WaitAsync(Deadline);
            await AssertClosedAsync(pipe);
            Assert.AreEqual(1, runtime.WatchDisposals);
            Assert.IsFalse(host.Listener.IsCompleted);
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            await AssertHealthyAsync(host);
            Assert.IsEmpty(host.Errors);
        }
        finally
        {
            pipe.Dispose();
            host.Stop();
            runtime.Release.TrySetResult();
            await host.Listener.WaitAsync(Deadline);
            if (runtime.WatchStarted.Task.IsCompleted)
                await runtime.WatchDisposed.Task.WaitAsync(Deadline);
        }
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisconnectDuringLargeWatchResponseCancelsWorkAndReleasesWatchScope(bool integrated)
    {
        await using var directory = new TemporaryDirectory();
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        await using var node = new LocalSendNode(new LocalSendOptions
        {
            Alias = "IPC watch boundary",
            DataDirectory = Path.Combine(directory.Path, "identity"),
            DownloadDirectory = Path.Combine(directory.Path, "downloads"),
            Port = port,
            NetworkWhitelist = []
        });
        using var deadline = new CancellationTokenSource(Deadline);
        await node.StartAsync(deadline.Token);
        var sender = new LocalSendDevice("Boundary sender", "2.2", null, LocalSendDeviceType.Headless,
            new string('A', 64), false, [], DateTimeOffset.UtcNow);
        // Synthetic runtime metadata controls response size; this is an IPC
        // backpressure test, not evidence of a real incoming transfer or GUI.
        var incoming = new IncomingTransferRequest(Guid.NewGuid(), Guid.NewGuid(), "boundary", sender,
            [new("file", new string('x', 512 * 1024), 1, "application/octet-stream", null, null, null, null)],
            DateTimeOffset.UtcNow);
        var runtime = new RuntimeBoundary(integrated) { WatchNode = node, WatchIncoming = [incoming] };
        await using var host = new PipeFixture(runtime);
        using var pipe = await host.ConnectAsync();
        try
        {
            await WriteAsync(pipe, new(["receive", "watch"], host.Directory, null, "en-US"));
            var header = new byte[4];
            await pipe.ReadExactlyAsync(header, deadline.Token);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            Assert.IsTrue(length > 1024 * 1024 && length < 4 * 1024 * 1024,
                "The real writer must begin a valid streaming response larger than the OS pipe buffer.");
            await runtime.AllEntered.Task.WaitAsync(Deadline);
            Assert.IsFalse(runtime.WatchDisposed.Task.IsCompleted);
            Assert.IsFalse(runtime.Cancelled.Task.IsCompleted);

            // Do not drain the body or stop the host. Only this caller goes
            // away while the writer and next runtime read are both pending.
            pipe.Dispose();
            await runtime.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await runtime.WatchDisposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(1, runtime.WatchDisposals);
            Assert.AreEqual(0, runtime.Committed);
            Assert.IsFalse(host.Listener.IsCompleted);
            Assert.AreEqual(LocalSendNodeState.Running, node.State);
            await AssertHealthyAsync(host);
            Assert.IsEmpty(host.Errors);
        }
        finally
        {
            // An assertion failure must not leave the controlled runtime read
            // pending or release the real node before its watch scope exits.
            pipe.Dispose();
            host.Stop();
            runtime.Release.TrySetResult();
            await host.Listener.WaitAsync(Deadline);
            if (runtime.AllEntered.Task.IsCompleted)
                await runtime.WatchDisposed.Task.WaitAsync(Deadline);
        }
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IncompleteConnectedRequestExpiresWithoutHostShutdownOrBusinessSideEffects(bool partialBody)
    {
        var runtime = new RuntimeBoundary();
        await using var host = new PipeFixture(runtime);
        using var pipe = await host.ConnectAsync();
        var bytes = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 20);
        bytes[4] = (byte)'{';
        await pipe.WriteAsync(partialBody ? bytes : bytes.AsMemory(0, 2));
        await pipe.FlushAsync();
        await AssertHealthyAsync(host);
        Assert.IsFalse(host.Listener.IsCompleted);

        // Leave the connection open and await actual EOF/reset under a bound
        // larger than the production ten-second admission deadline. No sleep,
        // caller cancellation, or host stop is used to end the request.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { Assert.AreEqual(0, await pipe.ReadAsync(new byte[1], deadline.Token)); }
        catch (IOException) { /* A closed Windows pipe or Unix socket may reset. */ }
        Assert.IsEmpty(runtime.Saves);
        Assert.AreEqual(0, runtime.Committed);
        Assert.IsFalse(host.Listener.IsCompleted);
        await AssertHealthyAsync(host);
        Assert.IsEmpty(host.Errors);
    }

    [TestMethod]
    [DataRow("length")]
    [DataRow("json")]
    [DataRow("truncated")]
    public async Task CorruptRequestOnlyClosesItsConnectionAndNextRequestSucceeds(string kind)
    {
        var runtime = new RuntimeBoundary();
        await using var host = new PipeFixture(runtime);
        using (var pipe = await host.ConnectAsync())
        {
            var bytes = new byte[5];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, kind == "length" ? -1 : kind == "json" ? 1 : 20);
            bytes[4] = (byte)'!';
            try
            {
                await pipe.WriteAsync(kind == "length" ? bytes.AsMemory(0, 4) : bytes);
                await pipe.FlushAsync();
            }
            catch (IOException) when (kind == "length")
            {
                // The host may reject the four-byte length and close before
                // WriteAsync completes. Its exact rejection is checked below.
            }
            // A truncated body needs the caller to close, not a synthetic read
            // error or a ten-second sleep to make the fixture finish.
            if (kind != "truncated")
            {
                await AssertClosedAsync(pipe);
                var error = await host.Reported.Task.WaitAsync(Deadline);
                Assert.AreEqual("CLI request failed", error.Message);
                if (kind == "length") Assert.IsInstanceOfType<InvalidDataException>(error.Exception);
                else Assert.IsInstanceOfType<JsonException>(error.Exception);
            }
        }
        await AssertHealthyAsync(host);
        Assert.AreEqual(kind == "truncated" ? 0 : 1, host.Errors.Count);
        Assert.IsEmpty(runtime.Saves);
    }

    [TestMethod]
    public async Task RapidTruncatedConnectionsDoNotResetTheNextHealthyClient()
    {
        var runtime = new RuntimeBoundary();
        await using var host = new PipeFixture(runtime);
        var truncated = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(truncated, 20);
        truncated[4] = (byte)'!';
        for (var iteration = 0; iteration < 32; iteration++)
        {
            using (var pipe = await host.ConnectAsync())
            {
                await pipe.WriteAsync(truncated);
                await pipe.FlushAsync();
            }
            // No retry: each first connection following a truncated frame
            // must receive its own valid response from the same listener.
            await AssertHealthyAsync(host);
        }
        Assert.IsEmpty(host.Errors);
        Assert.IsEmpty(runtime.Saves);
    }

    [TestMethod]
    public async Task ConcurrentRequestsUseCallerLanguagesWithoutLeakingCultureBetweenConnections()
    {
        var runtime = new RuntimeBoundary(admissions: 2) { BlockSettingsReads = true };
        await using var host = new PipeFixture(runtime);
        var calls = new[] { "en-US", "zh-CN" }.Select(async locale =>
        {
            using var pipe = await host.ConnectAsync();
            await WriteAsync(pipe, new(["settings", "get", "missing"], host.Directory, null, locale));
            var response = await ReadAsync(pipe);
            Assert.AreEqual(2, response.ExitCode);
            Assert.AreEqual(locale == "en-US" ? "Unknown settings key: missing" : "未知设置键：missing", response.Message);
        }).ToArray();
        try { await runtime.AllEntered.Task.WaitAsync(Deadline); }
        finally
        {
            runtime.Release.TrySetResult();
            await Task.WhenAll(calls).WaitAsync(Deadline);
        }
        Assert.AreSequenceEqual(new[] { "en-US", "zh-CN" }, runtime.ReadCultures.Order());
        Assert.IsEmpty(host.Errors);
    }

    [TestMethod]
    public async Task ParseFailureIsFramedAsUsageErrorAndDoesNotCallRuntime()
    {
        var runtime = new RuntimeBoundary();
        await using var host = new PipeFixture(runtime);
        using var pipe = await host.ConnectAsync();
        await WriteAsync(pipe, new(["settings", "set", "alias"], host.Directory, null, "en-US"));
        var response = await ReadAsync(pipe);
        Assert.AreEqual("result", response.Type);
        Assert.AreEqual(2, response.ExitCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.Message));
        Assert.IsEmpty(runtime.Saves);
        await AssertHealthyAsync(host);
        Assert.IsEmpty(host.Errors);
    }

    [TestMethod]
    public async Task QuitWritesFinalResponseBeforeInvokingRuntimeExit()
    {
        var runtime = new RuntimeBoundary();
        await using var host = new PipeFixture(runtime);
        runtime.ExitHost = host.Stop;
        using var pipe = await host.ConnectAsync();
        await WriteAsync(pipe, new(["app", "quit", "--yes"], host.Directory, null, "en-US"));
        var response = await ReadAsync(pipe);
        Assert.AreEqual("result", response.Type);
        Assert.AreEqual(0, response.ExitCode);
        await runtime.QuitCalled.Task.WaitAsync(Deadline);
        await host.Listener.WaitAsync(Deadline);
        Assert.AreEqual(1, runtime.QuitCount);
        Assert.IsEmpty(host.Errors);
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ClientClosingImmediatelyAfterFinalResultCannotDiscardAcceptedQuit(bool integrated)
    {
        // A CLI client closes immediately after reading its final result. The
        // existing quit test kept the connection alive until QuitCalled and
        // therefore did not exercise cancellation racing with final delivery.
        for (var iteration = 0; iteration < 32; iteration++)
        {
            var runtime = new RuntimeBoundary(integrated);
            await using var host = new PipeFixture(runtime);
            runtime.ExitHost = host.Stop;
            using (var pipe = await host.ConnectAsync())
            {
                await WriteAsync(pipe, new(["app", "quit", "--yes"], host.Directory, null, "en-US"));
                var response = await ReadAsync(pipe);
                Assert.AreEqual("result", response.Type);
                Assert.AreEqual(0, response.ExitCode);
            }
            await runtime.QuitCalled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await host.Listener.WaitAsync(Deadline);
            Assert.AreEqual(1, runtime.QuitCount, $"mode={integrated}, iteration={iteration}");
            Assert.IsEmpty(host.Errors);
        }
    }

    private static CliRequest SaveRequest(string directory) => new(["settings", "set", "alias", "pending"], directory, null, "en-US");

    private static IncomingTransferRequest[] WatchRequests(int count, string fileName)
    {
        var sender = new LocalSendDevice("Synthetic watch sender", "2.2", null, LocalSendDeviceType.Headless,
            new string('A', 64), false, [], DateTimeOffset.UtcNow);
        return Enumerable.Range(0, count).Select(index => new IncomingTransferRequest(Guid.NewGuid(), Guid.NewGuid(), "watch", sender,
            [new("file", $"{index}-{fileName}", 1, "application/octet-stream", null, null, null, null)], DateTimeOffset.UtcNow)).ToArray();
    }

    private static async Task<LocalSendNode> StartWatchNodeAsync(TemporaryDirectory directory)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var node = new LocalSendNode(new LocalSendOptions
        {
            Alias = "IPC bounded watch",
            DataDirectory = Path.Combine(directory.Path, "identity"),
            DownloadDirectory = Path.Combine(directory.Path, "downloads"),
            Port = port,
            NetworkWhitelist = []
        });
        try
        {
            using var starting = new CancellationTokenSource(Deadline);
            await node.StartAsync(starting.Token);
            return node;
        }
        catch { await node.DisposeAsync(); throw; }
    }

    private static async Task WriteAsync(Stream pipe, CliRequest request)
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        await CliProtocol.WriteAsync(pipe, request, CliJsonContext.Default.CliRequest, cancellation.Token);
    }

    private static async Task<CliResponse> ReadAsync(Stream pipe)
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        return await CliProtocol.ReadAsync(pipe, CliJsonContext.Default.CliResponse, cancellation.Token);
    }

    private static async Task AssertClosedAsync(Stream pipe)
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        try { Assert.AreEqual(0, await pipe.ReadAsync(new byte[1], cancellation.Token)); }
        catch (IOException) { /* The peer can report pipe/socket reset rather than EOF. */ }
    }

    private static async Task AssertHealthyAsync(PipeFixture host)
    {
        using var pipe = await host.ConnectAsync();
        await WriteAsync(pipe, new(["status"], host.Directory, null, "en-US"));
        var response = await ReadAsync(pipe);
        Assert.AreEqual(0, response.ExitCode);
        Assert.AreEqual("result", response.Type);
        Assert.IsNotNull(response.Data);
        Assert.IsTrue(response.Data.Value.GetProperty("appRunning").GetBoolean());
    }

    private sealed class PipeFixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory _directory = new();
        private readonly CliHost _host;
        public string Directory => _directory.Path;
        public Task Listener { get; }
        public ConcurrentQueue<(string Message, Exception Exception)> Errors { get; } = new();
        public TaskCompletionSource<(string Message, Exception Exception)> Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PipeFixture(ICliRuntime runtime)
        {
            _host = new(runtime, Directory, (message, exception) =>
            {
                Errors.Enqueue((message, exception));
                Reported.TrySetResult((message, exception));
            });
            Listener = _host.RunAsync();
        }

        public NamedPipeClientStream NewClient() => new(".", CliProtocol.PipeName(Directory), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        public async Task<NamedPipeClientStream> ConnectAsync()
        {
            var pipe = NewClient();
            try { await pipe.ConnectAsync(5000); return pipe; }
            catch { pipe.Dispose(); throw; }
        }

        public void Stop() => _host.Dispose();

        public async ValueTask DisposeAsync()
        {
            Stop();
            await Listener.WaitAsync(Deadline);
            await _directory.DisposeAsync();
        }
    }

    // Only the runtime boundary is replaced to control admission/cancellation.
    // Production CliHost, System.CommandLine, localization and framed responses
    // execute unchanged through real OS pipes in every case above.
    private sealed class RuntimeBoundary(bool integrated = false, int admissions = 1) : ICliRuntime
    {
        private int _entered, _committed, _quitCount, _watchActive, _watchReads, _watchDisposals;
        public bool Integrated => integrated;
        public bool BlockSaves { get; init; }
        public bool BlockSettingsReads { get; init; }
        public bool BlockWatchReads { get; init; } = true;
        public LocalSendNode? WatchNode { get; init; }
        public IncomingTransferRequest[] WatchIncoming { get; init; } = [];
        public Action? ExitHost { get; set; }
        public ConcurrentQueue<(string Value, string Directory)> Saves { get; } = new();
        public ConcurrentQueue<string> ReadCultures { get; } = new();
        public int Committed => Volatile.Read(ref _committed);
        public int QuitCount => Volatile.Read(ref _quitCount);
        public int WatchDisposals => Volatile.Read(ref _watchDisposals);
        public TaskCompletionSource AllEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource QuitCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WatchDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task EnterAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _entered) == admissions) AllEntered.TrySetResult();
            try { await Release.Task.WaitAsync(token); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }

        public async Task<CliResponse> SaveSettingAsync(string key, string value, string workingDirectory, bool restart, CancellationToken token)
        {
            Saves.Enqueue((value, workingDirectory));
            if (BlockSaves) await EnterAsync(token);
            Interlocked.Increment(ref _committed);
            return CliProtocol.Success(value);
        }

        public async Task<CliSetting[]> ReadSettingsAsync(bool showSecrets, CancellationToken token)
        {
            ReadCultures.Enqueue(CultureInfo.CurrentUICulture.Name);
            if (BlockSettingsReads) await EnterAsync(token);
            return [];
        }

        public async Task<CliState> ReadAsync(CancellationToken token)
        {
            if (Volatile.Read(ref _watchActive) != 0 && Interlocked.Increment(ref _watchReads) > 1 && BlockWatchReads)
                await EnterAsync(token);
            return new(WatchNode, WatchNode is not null, WatchNode?.State ?? LocalSendNodeState.Stopped,
                WatchNode?.Identity, null, null, WatchIncoming, new(Path.GetTempPath(), true, true, true, "en-US"));
        }
        public Task QuitAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _quitCount);
            ExitHost?.Invoke();
            QuitCalled.TrySetResult();
            return Task.CompletedTask;
        }
        public Task<CliResponse> ServerAsync(CliServerAction action, CancellationToken token) => throw new NotSupportedException();
        public Task<CliFavorite[]> FavoritesAsync(CancellationToken token) => throw new NotSupportedException();
        public Task SaveFavoriteAsync(LocalSendDevice device, CancellationToken token) => throw new NotSupportedException();
        public Task RemoveFavoriteAsync(string fingerprint, CancellationToken token) => throw new NotSupportedException();
        public Task<CliHistory[]> HistoryAsync(CancellationToken token) => throw new NotSupportedException();
        public Task RemoveHistoryAsync(Guid? id, CancellationToken token) => throw new NotSupportedException();
        public Task RecordReceiveAsync(IncomingTransferRequest request, ReceiveOutcome.Completed result, CancellationToken token) => throw new NotSupportedException();
        public Task DismissIncomingAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public IDisposable BeginReceiveWatch()
        {
            if (WatchNode is null) throw new NotSupportedException();
            Interlocked.Exchange(ref _watchActive, 1);
            WatchStarted.TrySetResult();
            return new CliScope(() =>
            {
                Interlocked.Exchange(ref _watchActive, 0);
                Interlocked.Increment(ref _watchDisposals);
                WatchDisposed.TrySetResult();
            });
        }
        public Task OpenAppAsync(string? favorite, Guid? history, CancellationToken token) => throw new NotSupportedException();
    }
}
