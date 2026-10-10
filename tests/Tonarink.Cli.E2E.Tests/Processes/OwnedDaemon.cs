using System.Diagnostics;

namespace Tonarink.Cli.E2E.Tests;

// An explicit test-created child, not a process found by name or PID search.
// Used only where a real crash/competing startup is the behavior under test.
internal sealed class OwnedDaemon : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _output, _error;
    public bool HasExited => _process.HasExited;

    public OwnedDaemon(string executable, string profile)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            WorkingDirectory = profile,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        start.ArgumentList.Add("--cli-daemon");
        start.ArgumentList.Add(profile);
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the fixture-owned daemon.");
        _process.StandardInput.Close();
        _output = _process.StandardOutput.ReadToEndAsync();
        _error = _process.StandardError.ReadToEndAsync();
    }

    public async Task<int> WaitForExitAsync()
    {
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsEmpty(await _output.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsEmpty(await _error.WaitAsync(TimeSpan.FromSeconds(10)));
        return _process.ExitCode;
    }

    public async Task KillAsync()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: false);
        _ = await WaitForExitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try { await KillAsync(); }
        finally { _process.Dispose(); }
    }
}
