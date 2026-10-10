using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace LocalSendDotNet;

internal sealed class BroadcastHub<T>(int capacity, bool dropOldest = true)
{
    private readonly ConcurrentDictionary<long, Channel<T>> _subscribers = new();
    private long _nextId;
    private int _completed;

    public void Publish(T item)
    {
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(item);
    }

    public async Task PublishAsync(T item, CancellationToken cancellationToken)
    {
        foreach (var channel in _subscribers.Values)
        {
            try { await channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false); }
            // A subscriber can leave, or the hub can complete, while a reliable
            // writer is waiting for capacity. Neither closes other subscribers;
            // caller cancellation still propagates rather than being swallowed.
            catch (ChannelClosedException) { }
        }
    }

    public async IAsyncEnumerable<T> Subscribe([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _completed) != 0)
            yield break;

        var id = Interlocked.Increment(ref _nextId);
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = dropOldest ? BoundedChannelFullMode.DropOldest : BoundedChannelFullMode.Wait
        });
        _subscribers[id] = channel;
        if (Volatile.Read(ref _completed) != 0 && _subscribers.TryRemove(id, out _))
            channel.Writer.TryComplete();
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            // Removing the dictionary entry alone leaves writers that captured
            // this full channel blocked indefinitely, even after hub completion.
            channel.Writer.TryComplete();
        }
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryComplete();
        _subscribers.Clear();
    }
}
