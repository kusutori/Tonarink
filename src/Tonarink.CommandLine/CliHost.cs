using System.IO.Pipes;
using System.Threading.Channels;

namespace Tonarink.Cli;

sealed class CliHost(ICliRuntime runtime, string dataDirectory, Action<string, Exception> report) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CliCommands _commands = new(runtime);
    public void Start() => _ = RunAsync();
    public void Dispose() => _lifetime.Cancel();

    public async Task RunAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(CliProtocol.PipeName(dataDirectory), PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(_lifetime.Token).ConfigureAwait(false); }
                catch { pipe.Dispose(); throw; }
                _ = HandleAsync(pipe);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { report("CLI listener failed", exception); throw; }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
        {
            try
            {
                cancellation.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await CliProtocol.ReadAsync(pipe, CliJsonContext.Default.CliRequest, cancellation.Token).ConfigureAwait(false);
                cancellation.CancelAfter(Timeout.InfiniteTimeSpan);
                var args = new CliCommandLine(runtime.Integrated).Parse(request.Arguments);
                var timeout = args.Integer("timeout", 0);
                if (timeout > 0) cancellation.CancelAfter(TimeSpan.FromSeconds(timeout));
                var output = Channel.CreateUnbounded<CliResponse>(new() { SingleReader = true });
                var writing = WriteResponsesAsync(pipe, output.Reader, cancellation);
                var disconnected = WatchDisconnectAsync(pipe, cancellation);
                CliResponse result;
                try { result = await _commands.ExecuteAsync(request, args, response => output.Writer.TryWrite(response), cancellation.Token).ConfigureAwait(false); }
                catch (CliException exception) { result = CliProtocol.Error(exception.Message, exception.ExitCode); }
                catch (OperationCanceledException) { result = CliProtocol.Error("Command cancelled or timed out", 130); }
                catch (Exception exception) { result = CliProtocol.Error(exception.Message); }
                output.Writer.TryWrite(result); output.Writer.TryComplete();
                await writing.ConfigureAwait(false);
                if (result.ExitCode == 0 && (args.Command, args.Action) is ("app", "quit"))
                    await runtime.QuitAsync(_lifetime.Token).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);
                await disconnected.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
            catch (Exception exception) { report("CLI request failed", exception); }
        }
    }
    private static async Task WatchDisconnectAsync(Stream pipe, CancellationTokenSource cancellation)
    {
        try { _ = await pipe.ReadAsync(new byte[1], cancellation.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { await cancellation.CancelAsync().ConfigureAwait(false); }
    }
    private static async Task WriteResponsesAsync(Stream pipe, ChannelReader<CliResponse> reader, CancellationTokenSource cancellation)
    {
        try
        {
            await foreach (var response in reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
                await CliProtocol.WriteAsync(pipe, response, CliJsonContext.Default.CliResponse, cancellation.Token).ConfigureAwait(false);
        }
        catch { await cancellation.CancelAsync().ConfigureAwait(false); throw; }
    }
}
