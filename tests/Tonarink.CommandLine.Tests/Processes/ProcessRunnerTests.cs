using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tonarink.TestSupport;

namespace Tonarink.CommandLine.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class ProcessRunnerTests
{
    [TestMethod]
    public async Task Utf8PipesPreserveTextEvenWhenTheParentConsoleUsesAscii()
    {
        using var directory = new TemporaryDirectory();
        var assembly = Path.Combine(AppContext.BaseDirectory, "ProcessProbe", "Tonarink.TestSupport.ProcessProbe.dll");
        const string text = "\t 开始 \"quote\" 🌏\r\nline two\n结束 \t\r\n";
        var inputEncoding = Console.InputEncoding;
        var outputEncoding = Console.OutputEncoding;
        // This assembly is non-parallel. CI's Windows console code page must
        // not silently corrupt a UTF-8 CLI fixture before the app reads it.
        try
        {
            Console.InputEncoding = Encoding.ASCII;
            Console.OutputEncoding = Encoding.ASCII;
            var result = await ProcessRunner.RunAsync("dotnet", [assembly, "echo-stdin", Guid.NewGuid().ToString("D")],
                directory.Path, TimeSpan.FromSeconds(15), input: text);
            Assert.AreEqual(0, result.ExitCode, result.StandardError);
            Assert.AreEqual(text, result.StandardOutput);
            Assert.AreEqual(string.Empty, result.StandardError);
        }
        finally
        {
            Console.InputEncoding = inputEncoding;
            Console.OutputEncoding = outputEncoding;
        }
    }

    [TestMethod]
    public async Task SuccessfulProcessKeepsStandardOutputAndErrorSeparate()
    {
        using var probe = new Probe();
        var result = await probe.RunAsync("success");
        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("probe stdout", result.StandardOutput, StringComparison.Ordinal);
        Assert.AreEqual($"probe stderr{Environment.NewLine}", result.StandardError);
        await probe.AssertExitedAsync();
    }

    [TestMethod]
    public async Task NonzeroExitIsReturnedWithDiagnosticsInsteadOfReportedAsSuccess()
    {
        using var probe = new Probe();
        var result = await probe.RunAsync("fail");
        Assert.AreEqual(17, result.ExitCode);
        Assert.Contains("intentional exit 17", result.StandardError, StringComparison.Ordinal);
        await probe.AssertExitedAsync();
    }

    [TestMethod]
    public async Task AbruptProcessTerminationIsNotReportedAsSuccess()
    {
        using var probe = new Probe();
        var result = await probe.RunAsync("kill");
        Assert.AreNotEqual(0, result.ExitCode);
        await probe.AssertExitedAsync();
    }

    [TestMethod]
    public async Task HangingOwnedChildTimesOutAndIsActuallyGone()
    {
        using var probe = new Probe();
        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() => probe.RunAsync("hang", TimeSpan.FromSeconds(3)));
        Assert.Contains("exceeded", exception.Message, StringComparison.Ordinal);
        await probe.AssertExitedAsync();
    }

    [TestMethod]
    public async Task OutputObserverFailureIsPropagatedAndKillsOnlyTheOwnedChild()
    {
        using var probe = new Probe();
        using var witness = new Probe();
        var witnessReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var witnessRun = witness.RunAsync("hang", observer: () => witnessReady.TrySetResult());
        try
        {
            await witnessReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var expected = new InvalidDataException("intentional observer failure");
            var actual = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => probe.RunAsync("hang", observer: () => throw expected));
            Assert.AreSame(expected, actual);
            await probe.AssertExitedAsync();
            Assert.IsTrue(witness.IsAlive, "A separate owned process must remain alive when the failed child is cleaned up.");
        }
        finally
        {
            witness.Stop();
            var stopped = await witnessRun.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreNotEqual(0, stopped.ExitCode);
        }
    }

    [TestMethod]
    public async Task ObserverCancellationIsNotMisreportedAsAProcessDeadline()
    {
        using var probe = new Probe();
        var expected = new OperationCanceledException("intentional observer cancellation");
        var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => probe.RunAsync("hang", observer: () => throw expected));
        Assert.AreSame(expected, actual);
        await probe.AssertExitedAsync();
    }

    private sealed class Probe : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();
        private readonly Guid _nonce = Guid.NewGuid();
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Process? _child;
        private int? _pid;
        private bool _entered;
        public bool IsAlive => _child is not null && !_exited.Task.IsCompleted && !_child.HasExited;

        public Task<ProcessResult> RunAsync(string mode, TimeSpan? timeout = null, Action? observer = null)
        {
            var assembly = Path.Combine(AppContext.BaseDirectory, "ProcessProbe", "Tonarink.TestSupport.ProcessProbe.dll");
            Assert.IsTrue(File.Exists(assembly), $"This build's probe is required: {assembly}");
            return ProcessRunner.RunAsync("dotnet", [assembly, mode, _nonce.ToString("D")],
                _directory.Path, timeout ?? TimeSpan.FromSeconds(15), outputLine: line =>
                {
                    if (line.StartsWith($"ready:{_nonce:D}:", StringComparison.Ordinal))
                    {
                        var parts = line.Split(':');
                        Assert.AreEqual(_pid, int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                        _entered = true;
                        observer?.Invoke();
                    }
                }, processStarted: child =>
                {
                    // This is the actual object returned by Process.Start in
                    // the runner, not a process found/adopted by name or PID.
                    _child = child;
                    _pid = child.Id;
                    child.Exited += (_, _) => _exited.TrySetResult();
                    child.EnableRaisingEvents = true;
                    if (child.HasExited)
                        _exited.TrySetResult();
                });
        }

        public async Task AssertExitedAsync()
        {
            Assert.IsNotNull(_pid, "The exact child must have entered the probe before termination is asserted.");
            Assert.IsTrue(_entered, "The child must report this fixture's nonce before termination is asserted.");
            await _exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Stop()
        {
            // The witness is intentionally stopped while RunAsync still owns
            // this exact Process object. Completed probes already signalled
            // their OS exit before the runner disposed its object.
            if (IsAlive)
            {
                _child!.Kill();
                if (!_child.WaitForExit(5000))
                    throw new TimeoutException("The owned probe did not exit during fixture cleanup.");
            }
        }

        public void Dispose()
        {
            Stop();
            _directory.Dispose();
        }
    }
}
