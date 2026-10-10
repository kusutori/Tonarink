using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tonarink.TestSupport;

namespace Tonarink.CommandLine.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class MtpControllerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("Passed")]
    [DataRow("AssertionFailure")]
    [DataRow("Killed")]
    [DataRow("Hanging")]
    [DataRow("UncooperativeHang")]
    [DataRow("NoSuchTest")]
    [DataRow("BelowMinimum")]
    [DataRow("Skipped")]
    public async Task ActualMtpRunCannotTurnFaultsOrMissingTestsIntoSuccess(string scenario)
    {
        await using var directory = new TemporaryDirectory();
        var nonce = Guid.NewGuid();
        File.WriteAllText(Path.Combine(directory.Path, "mtp-probe.nonce"), nonce.ToString("D"));
        var module = Path.Combine(AppContext.BaseDirectory, "MtpProbe", "Tonarink.MtpProbe.dll");
        Assert.IsTrue(File.Exists(module), $"This build's actual MTP fixture is required: {module}");
        var results = Path.Combine(directory.Path, "results");
        var selected = scenario == "BelowMinimum" ? "NoSuchTest" : scenario;
        var arguments = new List<string>
        {
            module, "--filter", $"FullyQualifiedName=Tonarink.MtpProbe.ProbeTests.{selected}",
            "--test-parameter", $"probeLease={Path.Combine(directory.Path, "mtp-probe.nonce")}",
            "--report-trx", "--report-trx-filename", "probe.trx", "--results-directory", results,
            "--diagnostic", "--diagnostic-synchronous-write", "--diagnostic-output-directory", results,
            "--zero-tests-policy", "strict", "--timeout", scenario is "Hanging" or "UncooperativeHang" ? "3s" : "15s",
        };
        if (scenario == "BelowMinimum")
            arguments.AddRange(["--minimum-expected-tests", "1"]);
        int? pid = null;
        int? hostPid = null;
        var entered = false;
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ProcessResult? result = null;
        TimeoutException? hardDeadline = null;
        try
        {
            // Cooperative MTP timeout and hard controller cleanup are distinct
            // cases. The hard deadline may not substitute for the former.
            var run = ProcessRunner.RunAsync("dotnet", arguments, directory.Path,
                TimeSpan.FromSeconds(scenario == "UncooperativeHang" ? 10 : 25),
                outputLine: line =>
                {
                    if (!line.StartsWith($"entered:{nonce:D}:", StringComparison.Ordinal))
                        return;
                    var parts = line.Split(':');
                    Assert.HasCount(4, parts);
                    Assert.AreEqual(selected, parts[3]);
                    hostPid = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    Assert.IsGreaterThan(0, hostPid.Value);
                    entered = true;
                }, processStarted: child =>
                {
                    pid = child.Id;
                    child.Exited += (_, _) => exited.TrySetResult();
                    child.EnableRaisingEvents = true;
                    if (child.HasExited)
                        exited.TrySetResult();
                });
            try { result = await run; }
            catch (TimeoutException exception) when (scenario == "UncooperativeHang")
            {
                hardDeadline = exception;
            }
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsNotNull(pid, "The exact owned MTP process must have been launched.");
            Assert.AreEqual(scenario is not ("NoSuchTest" or "BelowMinimum"), entered);
            Assert.IsTrue(Directory.GetFiles(results, "*.diag").Any(file => new FileInfo(file).Length > 0),
                "Actual MTP diagnostic logs are required, including abrupt termination.");
            var diagnostics = string.Join('\n', Directory.GetFiles(results, "*.diag").Select(File.ReadAllText));
            if (entered)
            {
                // MTP owns a separate test-host child. Prove the nonce's PID
                // belongs to the host it actually started and observed exiting;
                // never adopt/kill a process looked up by that numeric PID.
                Assert.Contains($"Started test host process. PID: '{hostPid}'", diagnostics, StringComparison.Ordinal);
                if (hardDeadline is null)
                    Assert.Contains($"Test host process exited. PID: '{hostPid}'", diagnostics, StringComparison.Ordinal);
                using var releasedLease = await ProbeLease.WaitForReleaseAsync(Path.Combine(directory.Path, "mtp-probe.nonce"));
                using var reader = new StreamReader(releasedLease);
                Assert.AreEqual(nonce.ToString("D"), reader.ReadToEnd());
            }

            var trx = Path.Combine(results, "probe.trx");
            if (scenario == "Killed")
            {
                Assert.IsNotNull(result);
                Assert.AreNotEqual(0, result.ExitCode);
                if (File.Exists(trx))
                    Assert.IsFalse(XDocument.Load(trx).Descendants().Any(element =>
                        element.Name.LocalName == "UnitTestResult" && (string?)element.Attribute("outcome") == "Passed"));
                return;
            }
            if (scenario is "Hanging" or "UncooperativeHang")
            {
                if (scenario == "Hanging")
                {
                    Assert.IsNull(hardDeadline, "Cooperative timeout must finish inside MTP, not at the controller cap.");
                    Assert.AreEqual($"cancelled:{nonce:D}:{hostPid}:Hanging",
                        File.ReadAllText(Path.Combine(directory.Path, "probe-cancelled.txt")),
                        "The actual test body's token must have been cancelled; a startup error is not timeout evidence.");
                    Assert.Contains("ended with exit code '3'", diagnostics, StringComparison.Ordinal);
                }
                if (hardDeadline is null)
                {
                    Assert.IsNotNull(result);
                    Assert.AreNotEqual(0, result.ExitCode, "The timed-out child must not return success.");
                }
                return;
            }

            Assert.IsNotNull(result);

            Assert.IsTrue(File.Exists(trx), "A completed MTP run must produce its real TRX.");
            var report = XDocument.Load(trx);
            var counters = report.Descendants().Single(element => element.Name.LocalName == "Counters");
            var cases = report.Descendants().Where(element => element.Name.LocalName == "UnitTestResult").ToArray();
            int Count(string name) => int.Parse((string)counters.Attribute(name)!, CultureInfo.InvariantCulture);
            if (scenario is "NoSuchTest" or "BelowMinimum")
            {
                Assert.AreEqual(scenario == "BelowMinimum" ? 9 : 8, result.ExitCode);
                Assert.AreEqual(0, Count("executed"));
                Assert.HasCount(0, cases);
            }
            else
            {
                Assert.HasCount(1, cases);
                Assert.Contains(selected, (string?)cases[0].Attribute("testName") ?? string.Empty,
                    StringComparison.Ordinal);
                Assert.AreEqual(scenario switch { "Passed" => 0, "AssertionFailure" => 2, _ => 8 }, result.ExitCode);
                Assert.AreEqual(scenario == "Skipped" ? 0 : 1, Count("executed"));
                Assert.AreEqual(scenario == "AssertionFailure" ? 1 : 0, Count("failed"));
                Assert.AreEqual(scenario == "Passed" ? "Passed" : scenario == "AssertionFailure" ? "Failed" : "NotExecuted",
                    (string?)cases[0].Attribute("outcome"));
                Assert.IsTrue(report.Descendants().Any(element => element.Name.LocalName == "ResultFile"),
                    "MTP must retain the fixture's diagnostic attachment in the TRX.");
                if (scenario == "AssertionFailure")
                    Assert.Contains("intentional MTP assertion failure", report.ToString(), StringComparison.Ordinal);
            }
        }
        finally
        {
            // Keep child reports after the owned working directory is removed.
            // These files are ordinary MSTest attachments, not a second runner.
            var archive = Path.Combine(TestContext.TestResultsDirectory!, "mtp-audit", nonce.ToString("N"));
            Directory.CreateDirectory(archive);
            if (Directory.Exists(results))
                foreach (var source in Directory.EnumerateFiles(results, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(archive, Path.GetRelativePath(results, source));
                    // Intentional child failures are evidence attachments,
                    // not top-level suite results for dotnet test to merge.
                    if (Path.GetExtension(target).Equals(".trx", StringComparison.OrdinalIgnoreCase))
                        target += ".txt";
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target);
                    TestContext.AddResultFile(target);
                }
            foreach (var name in new[] { "probe-entry.txt", "probe-cancelled.txt" })
            {
                var evidence = Path.Combine(directory.Path, name);
                if (!File.Exists(evidence))
                    continue;
                var target = Path.Combine(archive, name);
                File.Copy(evidence, target);
                TestContext.AddResultFile(target);
            }
            var summary = Path.Combine(archive, "controller.json");
            File.WriteAllText(summary, JsonSerializer.Serialize(new
            {
                scenario,
                module,
                moduleSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),
                pid,
                hostPid,
                entered,
                exited = exited.Task.IsCompleted,
                hardDeadline = hardDeadline?.Message,
                result,
            }, new JsonSerializerOptions { WriteIndented = true }));
            TestContext.AddResultFile(summary);
        }
    }
}
