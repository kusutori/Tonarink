using System.Threading.Channels;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class BroadcastLifecycleTests
{
    [TestMethod, Timeout(10_000)]
    public async Task CancelledPublisherPreservesBufferedEventsAndSubscriberRemainsUsable()
    {
        var hub = new BroadcastHub<int>(1, dropOldest: false);
        await using var reader = hub.Subscribe(default).GetAsyncEnumerator();
        var initial = reader.MoveNextAsync().AsTask();
        await hub.PublishAsync(1, default);
        Assert.IsTrue(await initial);
        Assert.AreEqual(1, reader.Current);
        await hub.PublishAsync(2, default);
        using var cancellation = new CancellationTokenSource();
        var blocked = hub.PublishAsync(3, cancellation.Token);
        Assert.IsFalse(blocked.IsCompleted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => blocked);
        Assert.IsTrue(await reader.MoveNextAsync());
        Assert.AreEqual(2, reader.Current);
        var next = reader.MoveNextAsync().AsTask();
        await hub.PublishAsync(4, default);
        Assert.IsTrue(await next);
        Assert.AreEqual(4, reader.Current);
        hub.Complete();
        Assert.IsFalse(await reader.MoveNextAsync());
    }

    [TestMethod, Timeout(10_000)]
    public async Task DisposingFullSubscriberReleasesBlockedPublisherAndNewSubscriberStillWorks()
    {
        var hub = new BroadcastHub<int>(1, dropOldest: false);
        await using var reader = hub.Subscribe(default).GetAsyncEnumerator();
        var initial = reader.MoveNextAsync().AsTask();
        await hub.PublishAsync(1, default);
        Assert.IsTrue(await initial);
        await hub.PublishAsync(2, default);
        using var cancellation = new CancellationTokenSource();
        var blocked = hub.PublishAsync(3, cancellation.Token);
        Assert.IsFalse(blocked.IsCompleted);
        try
        {
            await reader.DisposeAsync();
            await blocked.WaitAsync(TimeSpan.FromSeconds(2));
            await using var replacement = hub.Subscribe(default).GetAsyncEnumerator();
            var reading = replacement.MoveNextAsync().AsTask();
            await hub.PublishAsync(4, default);
            Assert.IsTrue(await reading);
            Assert.AreEqual(4, replacement.Current);
        }
        finally
        {
            await cancellation.CancelAsync();
            hub.Complete();
            await ObserveTerminatedPublisherAsync(blocked);
        }
    }

    [TestMethod, Timeout(10_000)]
    public async Task DisposingSlowSubscriberDoesNotPreventDeliveryToRemainingSubscriber()
    {
        var hub = new BroadcastHub<int>(1, dropOldest: false);
        await using var slow = hub.Subscribe(default).GetAsyncEnumerator();
        await using var fast = hub.Subscribe(default).GetAsyncEnumerator();
        var slowInitial = slow.MoveNextAsync().AsTask();
        var fastInitial = fast.MoveNextAsync().AsTask();
        await hub.PublishAsync(1, default);
        Assert.IsTrue(await slowInitial);
        Assert.IsTrue(await fastInitial);
        await hub.PublishAsync(2, default);
        Assert.IsTrue(await fast.MoveNextAsync());
        Assert.AreEqual(2, fast.Current);
        using var cancellation = new CancellationTokenSource();
        var blocked = hub.PublishAsync(3, cancellation.Token);
        Assert.IsFalse(blocked.IsCompleted);
        try
        {
            await slow.DisposeAsync();
            await blocked.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsTrue(await fast.MoveNextAsync());
            Assert.AreEqual(3, fast.Current);
            hub.Complete();
            Assert.IsFalse(await fast.MoveNextAsync());
        }
        finally
        {
            await cancellation.CancelAsync();
            hub.Complete();
            await ObserveTerminatedPublisherAsync(blocked);
        }
    }

    [TestMethod, Timeout(10_000)]
    public async Task CompletingFullHubReleasesBlockedPublisherAndDrainsExistingEvents()
    {
        var hub = new BroadcastHub<int>(1, dropOldest: false);
        await using var reader = hub.Subscribe(default).GetAsyncEnumerator();
        var initial = reader.MoveNextAsync().AsTask();
        await hub.PublishAsync(1, default);
        Assert.IsTrue(await initial);
        await hub.PublishAsync(2, default);
        using var cancellation = new CancellationTokenSource();
        var blocked = hub.PublishAsync(3, cancellation.Token);
        Assert.IsFalse(blocked.IsCompleted);
        try
        {
            hub.Complete();
            hub.Complete();
            await blocked.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsTrue(await reader.MoveNextAsync());
            Assert.AreEqual(2, reader.Current);
            Assert.IsFalse(await reader.MoveNextAsync());
            await using var late = hub.Subscribe(default).GetAsyncEnumerator();
            Assert.IsFalse(await late.MoveNextAsync());
            await hub.PublishAsync(4, default);
        }
        finally
        {
            await cancellation.CancelAsync();
            hub.Complete();
            await ObserveTerminatedPublisherAsync(blocked);
        }
    }

    private static async Task ObserveTerminatedPublisherAsync(Task publisher)
    {
        // Preserve the primary assertion and still observe/stop our own task.
        try { await publisher.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException) { }
    }
}
