using System.Collections.Concurrent;
using System.CommandLine;
using Tonarink.Cli;

namespace Tonarink.Services.Activation;

// Windows can deliver launch arguments before the UI runtime exists. Keep the
// library's parsed invocation until mounting, then invoke the shared handlers.
static class AppCommandActivation
{
    private static readonly ConcurrentQueue<Func<Task>> Pending = new();
    public static event EventHandler? Queued;
    public static bool HasPending => !Pending.IsEmpty;
    public static bool TryDequeue(out Func<Task>? invocation) => Pending.TryDequeue(out invocation);

    // Compatibility is confined to the activation boundary. New jump-list
    // entries and execution aliases use the same System.CommandLine syntax.
    public static string[] NormalizeLegacyArguments(string[] arguments)
    {
        if (arguments.Length != 1) return arguments;
        const string favorite = "tonarink-jump:favorite:";
        const string history = "tonarink-jump:history:";
        if (arguments[0].StartsWith(favorite, StringComparison.Ordinal))
            return ["app", "open", "--favorite", arguments[0][favorite.Length..]];
        if (arguments[0].StartsWith(history, StringComparison.Ordinal))
            return ["app", "open", "--history", arguments[0][history.Length..]];
        return arguments;
    }

    public static void EnqueueLaunch(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return;
        var runtime = new AppCliRuntime(CliBridge.Get);
        var model = new CliCommandLine(integrated: true, runtime: runtime,
            request: new([], Environment.CurrentDirectory, null), emit: response =>
            {
                if (response.ExitCode != 0)
                    AppDiagnostics.Report("Launch command failed", new CliException(response.Message ?? "Command failed", response.ExitCode));
            });
        var legacy = NormalizeLegacyArguments([arguments]);
        var parsed = legacy.Length == 1 ? model.Root.Parse(arguments) : model.Root.Parse(legacy);
        // Internal startup flags and platform activation protocols are not CLI commands.
        if (!model.IsBusinessInvocation(parsed)) return;
        if (parsed.Errors.Count > 0)
        {
            AppDiagnostics.Report("Invalid launch command", new CliException(string.Join('\n', parsed.Errors.Select(e => e.Message)), 2));
            return;
        }
        Pending.Enqueue(async () =>
        {
            try
            {
                using var cancellation = new CancellationTokenSource();
                if (parsed.GetValue(model.Timeout) is { } seconds) cancellation.CancelAfter(TimeSpan.FromSeconds(seconds));
                var exit = await parsed.InvokeAsync(new InvocationConfiguration
                { EnableDefaultExceptionHandler = false, ProcessTerminationTimeout = null,
                  Output = TextWriter.Null, Error = TextWriter.Null }, cancellation.Token).ConfigureAwait(false);
                if (exit == 0 && model.IsQuit(parsed)) await runtime.QuitAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception exception) { AppDiagnostics.Report("Launch command failed", exception); }
        });
        Queued?.Invoke(null, EventArgs.Empty);
    }
}
