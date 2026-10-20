using System.Threading.Channels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tonarink.Cli;

namespace Tonarink.CommandLine.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class ResponseQueueTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    public async Task FullQueueWaitsForSpaceThenPreservesEveryBusinessRecordInOrder()
    {
        using var connection = new CancellationTokenSource();
        var output = new CliResponseQueue(connection.Token);
        await FillAsync(output);
        var next = CliProtocol.Success("next");
        var pending = output.WriteAsync(next).AsTask();
        try
        {
            Assert.IsFalse(pending.IsCompleted, "The real default queue must apply backpressure at its capacity.");
            Assert.AreEqual(CliResponseQueue.Capacity, output.Reader.Count);
            Assert.IsTrue(output.Reader.TryRead(out var first));
            Assert.AreEqual("0", first.Message);
            await pending.WaitAsync(Deadline);
            Assert.AreEqual(CliResponseQueue.Capacity, output.Reader.Count);
            for (var index = 1; index < CliResponseQueue.Capacity; index++)
            {
                Assert.IsTrue(output.Reader.TryRead(out var item));
                Assert.AreEqual(index.ToString(), item.Message);
            }
            Assert.IsTrue(output.Reader.TryRead(out var last));
            Assert.AreEqual(next, last);
            output.Complete();
            await output.Reader.Completion.WaitAsync(Deadline);
        }
        finally { await connection.CancelAsync(); await ObserveCancellationAsync(pending); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationReleasesOnlyTheWaitingWriteWithoutPublishingIt(bool cancelConnection)
    {
        using var connection = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        var output = new CliResponseQueue(connection.Token);
        await FillAsync(output);
        var pending = output.WriteAsync(CliProtocol.Success("cancelled"), caller.Token).AsTask();
        try
        {
            Assert.IsFalse(pending.IsCompleted);
            await (cancelConnection ? connection : caller).CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));
            Assert.AreEqual(CliResponseQueue.Capacity, output.Reader.Count);
            Assert.IsTrue(output.Reader.TryRead(out var first));
            Assert.AreEqual("0", first.Message);
            if (!cancelConnection)
            {
                await output.WriteAsync(CliProtocol.Success("healthy")).AsTask().WaitAsync(Deadline);
                Assert.AreEqual(CliResponseQueue.Capacity, output.Reader.Count);
            }
            var remaining = new List<string?>();
            while (output.Reader.TryRead(out var item)) remaining.Add(item.Message);
            Assert.IsFalse(remaining.Contains("cancelled"));
            Assert.AreEqual(cancelConnection ? (CliResponseQueue.Capacity - 1).ToString() : "healthy", remaining[^1]);
            output.Complete();
            await output.Reader.Completion.WaitAsync(Deadline);
        }
        finally { await connection.CancelAsync(); await ObserveCancellationAsync(pending); }
    }

    [TestMethod]
    public async Task SaturatedProgressSkipsIntermediateUpdatesButNeverDropsTheFinalBusinessResult()
    {
        using var connection = new CancellationTokenSource();
        var output = new CliResponseQueue(connection.Token);
        for (var index = 0; index < CliResponseQueue.Capacity; index++)
            Assert.IsTrue(output.TryReportProgress(new("progress", 0, index.ToString())));
        for (var index = 0; index < 10_000; index++)
            Assert.IsFalse(output.TryReportProgress(new("progress", 0, "sampled")));
        Assert.AreEqual(CliResponseQueue.Capacity, output.Reader.Count);
        var final = CliProtocol.Success("completed");
        var pending = output.WriteAsync(final).AsTask();
        try
        {
            Assert.IsFalse(pending.IsCompleted);
            Assert.IsTrue(output.Reader.TryRead(out var first));
            Assert.AreEqual("0", first.Message);
            await pending.WaitAsync(Deadline);
            for (var index = 1; index < CliResponseQueue.Capacity; index++)
            {
                Assert.IsTrue(output.Reader.TryRead(out var progress));
                Assert.AreEqual("progress", progress.Type);
                Assert.AreEqual(index.ToString(), progress.Message);
            }
            Assert.IsTrue(output.Reader.TryRead(out var last));
            Assert.AreEqual(final, last);
            Assert.AreEqual("result", last.Type);
            output.Complete();
            await output.Reader.Completion.WaitAsync(Deadline);
        }
        finally { await connection.CancelAsync(); await ObserveCancellationAsync(pending); }
    }

    [TestMethod]
    public async Task CompletionRejectsPendingWritersAndDrainsAlreadyAcceptedRecords()
    {
        using var connection = new CancellationTokenSource();
        var output = new CliResponseQueue(connection.Token);
        await FillAsync(output);
        var pending = output.WriteAsync(CliProtocol.Success("too late")).AsTask();
        try
        {
            Assert.IsFalse(pending.IsCompleted);
            output.Complete();
            await Assert.ThrowsExactlyAsync<ChannelClosedException>(() => pending.WaitAsync(Deadline));
            Assert.IsFalse(output.TryReportProgress(new("progress", 0, "too late")));
            for (var index = 0; index < CliResponseQueue.Capacity; index++)
            {
                Assert.IsTrue(output.Reader.TryRead(out var item));
                Assert.AreEqual(index.ToString(), item.Message);
            }
            await output.Reader.Completion.WaitAsync(Deadline);
            output.Complete();
            Assert.IsTrue(output.Reader.Completion.IsCompletedSuccessfully);
        }
        finally { await connection.CancelAsync(); await ObserveCancellationAsync(pending); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AlreadyCancelledTokenCannotPublishEvenWhenTheQueueIsEmpty(bool cancelConnection)
    {
        using var connection = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        await (cancelConnection ? connection : caller).CancelAsync();
        var output = new CliResponseQueue(connection.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => output.WriteAsync(CliProtocol.Success("not published"), caller.Token).AsTask());
        Assert.AreEqual(0, output.Reader.Count);
        Assert.AreEqual(!cancelConnection, output.TryReportProgress(new("progress", 0, "sample")));
    }

    [TestMethod]
    public async Task ProgressCanResumeAfterCapacityBecomesAvailable()
    {
        using var connection = new CancellationTokenSource();
        var output = new CliResponseQueue(connection.Token);
        await FillAsync(output);
        Assert.IsFalse(output.TryReportProgress(new("progress", 0, "full")));
        Assert.IsTrue(output.Reader.TryRead(out _));
        var progress = new CliResponse("progress", 0, "resumed");
        Assert.IsTrue(output.TryReportProgress(progress));
        Assert.AreEqual(CliResponseQueue.Capacity, output.Reader.Count);
        CliResponse? last = null;
        while (output.Reader.TryRead(out var item)) last = item;
        Assert.AreEqual(progress, last);
        output.Complete();
        await output.Reader.Completion.WaitAsync(Deadline);
    }

    private static async Task FillAsync(CliResponseQueue output)
    {
        for (var index = 0; index < CliResponseQueue.Capacity; index++)
            await output.WriteAsync(CliProtocol.Success(index.ToString()));
    }

    private static async Task ObserveCancellationAsync(Task pending)
    {
        try { await pending.WaitAsync(Deadline); }
        catch (Exception exception) when (exception is OperationCanceledException or ChannelClosedException) { }
    }
}
