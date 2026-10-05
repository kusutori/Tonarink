using System.IO.Pipes;
using System.CommandLine;
using System.Threading.Channels;

namespace Tonarink.Cli;

sealed class CliHost(ICliRuntime runtime, string dataDirectory, Action<string, Exception> report) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
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
                var output = Channel.CreateUnbounded<CliResponse>(new() { SingleReader = true });
                var writing = WriteResponsesAsync(pipe, output.Reader, cancellation);
                var disconnected = WatchDisconnectAsync(pipe, cancellation);
                var responded = false;
                var commandLine = new CliCommandLine(runtime.Integrated, runtime: runtime, request: request,
                    emit: response => { if (response.Type == "result") responded = true; output.Writer.TryWrite(response); });
                var parsed = commandLine.Root.Parse(request.Arguments);
                using var diagnostics = new StringWriter();
                var exit = 2;
                if (parsed.Errors.Count > 0)
                    output.Writer.TryWrite(CliProtocol.Error(string.Join('\n', parsed.Errors.Select(e => e.Message)), 2));
                else
                {
                    if (parsed.GetValue(commandLine.Timeout) is { } timeout) cancellation.CancelAfter(TimeSpan.FromSeconds(timeout));
                    exit = await parsed.InvokeAsync(new InvocationConfiguration
                    { EnableDefaultExceptionHandler = false, ProcessTerminationTimeout = null,
                      Output = diagnostics, Error = diagnostics }, cancellation.Token).ConfigureAwait(false);
                    if (!responded) output.Writer.TryWrite(new("result", exit, diagnostics.ToString().TrimEnd()));
                }
                output.Writer.TryComplete();
                await writing.ConfigureAwait(false);
                if (exit == 0 && commandLine.IsQuit(parsed))
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
