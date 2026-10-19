using System.CommandLine;
using System.IO.Pipes;
using System.Threading.Channels;

namespace Tonarink.Cli;

sealed class CliHost(ICliRuntime runtime, string dataDirectory, Action<string, Exception> report) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    public void Start() => _ = RunAsync();
    public void Dispose() => _lifetime.Cancel();

    public async Task RunAsync()
    {
        NamedPipeServerStream? pipe = null;
        try
        {
            pipe = CreatePipe();
            while (!_lifetime.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(_lifetime.Token).ConfigureAwait(false);
                var connected = pipe;
                // Unix instances share a listening socket. Keep its next owner
                // alive before a fast/invalid request can dispose the last one
                // and reset clients already queued on that socket.
                try { pipe = CreatePipe(); }
                catch { connected.Dispose(); pipe = null; throw; }
                _ = HandleAsync(connected);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { report("CLI listener failed", exception); throw; }
        finally { pipe?.Dispose(); }
    }

    private NamedPipeServerStream CreatePipe() => new(CliProtocol.PipeName(dataDirectory), PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
        using (var commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
        {
            CliResponseQueue? output = null;
            Task<CliResponse?>? writing = null;
            Task? disconnected = null;
            var quitRequested = false;
            try
            {
                cancellation.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await CliProtocol.ReadAsync(pipe, CliJsonContext.Default.CliRequest, cancellation.Token).ConfigureAwait(false);
                // The caller's resolved system language travels with the
                // request; a GUI host can have a different display language.
                using var language = CliText.UseLanguage(request.Language);
                cancellation.CancelAfter(Timeout.InfiniteTimeSpan);
                output = new CliResponseQueue(cancellation.Token);
                writing = WriteResponsesAsync(pipe, output.Reader, cancellation);
                disconnected = WatchDisconnectAsync(pipe, cancellation);
                var responded = false;
                var commandLine = new CliCommandLine(runtime.Integrated, runtime: runtime, request: request,
                    emit: async (response, token) =>
                    {
                        await output.WriteAsync(response, token).ConfigureAwait(false);
                        if (response.Type == "result") responded = true;
                    }, reportProgress: response => output.TryReportProgress(response));
                var parsed = commandLine.Root.Parse(request.Arguments);
                quitRequested = parsed.Errors.Count == 0 && commandLine.IsQuit(parsed);
                using var diagnostics = new StringWriter();
                var exit = 2;
                if (parsed.Errors.Count > 0)
                    await output.WriteAsync(CliProtocol.Error(string.Join('\n', parsed.Errors.Select(e => e.Message)), 2)).ConfigureAwait(false);
                else
                {
                    // A command timeout must cancel business work, not its
                    // response channel. Otherwise the handler creates exit 130
                    // but the writer closes the pipe before delivering it.
                    if (parsed.GetValue(commandLine.Timeout) is { } timeout) commandCancellation.CancelAfter(TimeSpan.FromSeconds(timeout));
                    exit = await parsed.InvokeAsync(new InvocationConfiguration
                    {
                        EnableDefaultExceptionHandler = false,
                        ProcessTerminationTimeout = null,
                        Output = diagnostics,
                        Error = diagnostics
                    }, commandCancellation.Token).ConfigureAwait(false);
                    if (!responded) await output.WriteAsync(new("result", exit, diagnostics.ToString().TrimEnd())).ConfigureAwait(false);
                }
                // Bound final delivery separately from business execution;
                // a connected peer that stops reading must not pin a writer.
                cancellation.CancelAfter(TimeSpan.FromSeconds(10));
                output.Complete();
                await writing.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
            catch (Exception exception) { report("CLI request failed", exception); }
            finally
            {
                // A producer can now be awaiting queue space when its writer
                // faults. Observe both owned tasks before disposing their CTS;
                // otherwise the disconnect watcher can touch a disposed source.
                output?.Complete();
                await cancellation.CancelAsync().ConfigureAwait(false);
                foreach (var task in new[] { writing, disconnected })
                {
                    if (task is null) continue;
                    try { await task.ConfigureAwait(false); }
                    catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
                    catch (Exception exception) { report("CLI connection cleanup failed", exception); }
                }
                // Reading the successful final result is the client's signal
                // to close. That disconnect must not revoke an accepted quit
                // while invocation/queue cleanup is still finishing. A partial
                // or failed frame delivery does not authorize runtime exit.
                if (quitRequested && writing is { IsCompletedSuccessfully: true }
                    && writing.Result is { Type: "result", ExitCode: 0 })
                {
                    try { await runtime.QuitAsync(_lifetime.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                    catch (Exception exception) { report("CLI quit failed", exception); }
                }
            }
        }
    }
    private static async Task WatchDisconnectAsync(Stream pipe, CancellationTokenSource cancellation)
    {
        try { _ = await pipe.ReadAsync(new byte[1], cancellation.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { await cancellation.CancelAsync().ConfigureAwait(false); }
    }
    private static async Task<CliResponse?> WriteResponsesAsync(Stream pipe, ChannelReader<CliResponse> reader, CancellationTokenSource cancellation)
    {
        try
        {
            await foreach (var response in reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
            {
                // Watch/progress can continue indefinitely, so the final-only
                // deadline is insufficient. Bound each actual frame delivery
                // and cancel just this connection, its queue and business work.
                using var delivery = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                delivery.CancelAfter(TimeSpan.FromSeconds(10));
                await CliProtocol.WriteAsync(pipe, response, CliJsonContext.Default.CliResponse, delivery.Token).ConfigureAwait(false);
                // The final frame terminates this wire exchange. Do not wait
                // for another queue read that can be cancelled by the client
                // closing immediately after it has consumed this response.
                if (response.Type == "result") return response;
            }
            return null;
        }
        catch { await cancellation.CancelAsync().ConfigureAwait(false); throw; }
    }
}
