using System.Diagnostics;
using System.Text;

namespace Tonarink.TestSupport;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
        string workingDirectory, TimeSpan timeout, string? input = null, Action<string>? outputLine = null,
        Action<Process>? processStarted = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {executable}.");
        Task<string>? output = null, error = null;
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            processStarted?.Invoke(process);
            output = outputLine is null ? process.StandardOutput.ReadToEndAsync() : ReadLinesAsync();
            error = process.StandardError.ReadToEndAsync();
            if (input is not null)
                await process.StandardInput.WriteAsync(input.AsMemory(), cancellation.Token);
            process.StandardInput.Close();
            // Observe output-reader/observer failures before they can leave a
            // still-running child blocked on a full redirected pipe.
            var pending = new List<Task> { process.WaitForExitAsync(cancellation.Token), output, error };
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).WaitAsync(cancellation.Token);
                pending.Remove(completed);
                await completed;
            }
            // A detached host must not keep inherited stdout/stderr open.
            return new(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            throw new TimeoutException($"Process {process.Id} ({executable}) exceeded {timeout}. "
                + $"stdout: {(output is { IsCompletedSuccessfully: true } ? output.Result : "<open>")}; "
                + $"stderr: {(error is { IsCompletedSuccessfully: true } ? error.Result : "<open>")}");
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            throw;
        }

        async Task<string> ReadLinesAsync()
        {
            var result = new StringBuilder();
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                result.AppendLine(line);
                outputLine!(line);
            }
            return result.ToString();
        }
    }
}
