using System.Threading.Channels;

namespace Tonarink.Cli;

// Business records are never dropped: their producer asynchronously waits for
// space. Progress is already sampled telemetry, so it may skip an intermediate
// update rather than block Core's synchronous IProgress callback or accumulate
// unbounded pending writes. The final business result uses WriteAsync too.
sealed class CliResponseQueue(CancellationToken connection)
{
    internal const int Capacity = 16;
    private readonly Channel<CliResponse> _channel = Channel.CreateBounded<CliResponse>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });

    public ChannelReader<CliResponse> Reader => _channel.Reader;

    public ValueTask WriteAsync(CliResponse response, CancellationToken token = default) =>
        !token.CanBeCanceled || token == connection
            ? _channel.Writer.WriteAsync(response, connection)
            : WriteLinkedAsync(response, token);

    private async ValueTask WriteLinkedAsync(CliResponse response, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(connection, token);
        await _channel.Writer.WriteAsync(response, cancellation.Token).ConfigureAwait(false);
    }

    public bool TryReportProgress(CliResponse response) =>
        !connection.IsCancellationRequested && _channel.Writer.TryWrite(response);

    public void Complete() => _channel.Writer.TryComplete();
}
