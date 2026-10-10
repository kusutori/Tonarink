using System.Net;
using System.Net.Sockets;

namespace Tonarink.Cli.E2E.Tests;

// An opaque transport gate: both production CLIs still negotiate TLS and the
// LocalSend protocol. Small control requests on other connections remain live.
internal sealed class PausedTcpRelay : IAsyncDisposable
{
    private const int ForwardLimit = 256 * 1024;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Task> _connections = [];
    private readonly TaskCompletionSource<long> _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _accepting;
    private readonly int _destinationPort;
    private bool _disposed;

    public string Target { get; }
    public Task<long> Paused => _paused.Task;

    public PausedTcpRelay(int destinationPort)
    {
        _destinationPort = destinationPort;
        _listener.Start();
        Target = $"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _accepting = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var source = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                // Only this loop writes the list; disposal awaits it before
                // observing the connections. Each task owns its exact sockets.
                _connections.Add(RelayAsync(source));
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task RelayAsync(TcpClient source)
    {
        using (source)
        using (var destination = new TcpClient())
        using (var connection = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
        {
            Task? upstream = null, downstream = null;
            try
            {
                await destination.ConnectAsync(IPAddress.Loopback, _destinationPort, connection.Token);
                upstream = ForwardAsync(source.GetStream(), destination.GetStream(), pause: true, connection.Token);
                downstream = ForwardAsync(destination.GetStream(), source.GetStream(), pause: false, connection.Token);
                await Task.WhenAny(upstream, downstream);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            // A remote-cancel attempt can arrive after the destination was
            // intentionally stopped. Preserve the diagnostic and close the
            // owned connection; the business assertions still must pass.
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused)
            {
                Console.WriteLine("Relay destination is no longer listening.");
            }
            finally
            {
                await connection.CancelAsync();
                if (upstream is not null && downstream is not null)
                {
                    try { await Task.WhenAll(upstream, downstream); }
                    catch (OperationCanceledException) when (connection.IsCancellationRequested) { }
                    // A cancelled HTTP upload can reset either TLS connection.
                    // Keep diagnostics; assertions still require both CLI results.
                    catch (IOException exception) { Console.WriteLine($"Relay peer closed: {exception.Message}"); }
                    catch (SocketException exception) { Console.WriteLine($"Relay peer reset: {exception.SocketErrorCode}"); }
                }
            }
        }
    }

    private async Task ForwardAsync(NetworkStream source, NetworkStream destination, bool pause, CancellationToken token)
    {
        var buffer = new byte[32 * 1024];
        long forwarded = 0;
        while (true)
        {
            if (pause && forwarded == ForwardLimit)
            {
                _paused.TrySetResult(forwarded);
                await Task.Delay(Timeout.Infinite, token);
            }
            var available = pause ? (int)Math.Min(buffer.Length, ForwardLimit - forwarded) : buffer.Length;
            var read = await source.ReadAsync(buffer.AsMemory(0, available), token);
            if (read == 0) return;
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            forwarded += read;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _lifetime.CancelAsync();
        try
        {
            try { await _accepting.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { await Task.WhenAll(_connections).WaitAsync(TimeSpan.FromSeconds(10)); }
        }
        finally
        {
            _listener.Stop();
            _lifetime.Dispose();
        }
    }
}
