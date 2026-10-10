using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Tonarink.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tonarink.CommandLine.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class SuiteProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("Passed", 0)]
    [DataRow("AssertionFailure", 2)]
    [DataRow("Killed", 7)]
    [DataRow("UncooperativeHang", 124)]
    [DataRow("NoSuchTest", 8)]
    [DataRow("Skipped", 8)]
    public async Task ProductionSuiteControllerPreservesFailureAndBoundsActualMtpHost(string scenario, int expectedCode)
    {
        await using var directory = new TemporaryDirectory();
        var nonce = Guid.NewGuid().ToString("D");
        var lease = Path.Combine(directory.Path, "mtp-probe.nonce");
        File.WriteAllText(lease, nonce);
        var module = Path.Combine(AppContext.BaseDirectory, "MtpProbe", "Tonarink.MtpProbe.dll");
        var script = Path.Combine(AppContext.BaseDirectory, "TestTools", "Invoke-TestProcess.ps1");
        Assert.IsTrue(File.Exists(module));
        Assert.IsTrue(File.Exists(script));
        var results = Path.Combine(directory.Path, "results");
        var arguments = new[]
        {
            module, "--filter", $"FullyQualifiedName=Tonarink.MtpProbe.ProbeTests.{scenario}",
            "--test-parameter", $"probeLease={lease}", "--report-trx", "--report-trx-filename", "probe.trx",
            "--results-directory", results, "--diagnostic", "--diagnostic-synchronous-write",
            "--diagnostic-output-directory", results, "--zero-tests-policy", "strict", "--timeout",
            scenario == "UncooperativeHang" ? "3s" : "15s",
        };
        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var command = $". {Quote(script)}; $run = Invoke-TestProcess -FilePath 'dotnet' "
            + $"-ArgumentList @({string.Join(',', arguments.Select(Quote))}) -WorkingDirectory {Quote(directory.Path)} "
            + $"-ResultsDirectory {Quote(results)} -TimeoutSeconds {(scenario == "UncooperativeHang" ? 10 : 25)}; exit $run.exitCode";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        try
        {
            // The generous parent cap audits the production helper's own
            // shorter deadline; it must never be the layer that ends the run.
            var result = await ProcessRunner.RunAsync(Environment.GetEnvironmentVariable("TONARINK_TEST_PWSH") ?? "pwsh",
                ["-NoProfile", "-EncodedCommand", encoded], directory.Path, TimeSpan.FromSeconds(50));
            Assert.AreEqual(expectedCode, result.ExitCode, result.StandardError);
            using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(results, "controller.json")));
            var root = record.RootElement;
            Assert.AreEqual(expectedCode, root.GetProperty("exitCode").GetInt32());
            Assert.AreEqual(scenario == "UncooperativeHang" ? "hard-deadline" : "exited",
                root.GetProperty("outcome").GetString(), root.ToString());
            Assert.IsTrue(root.GetProperty("streamsDrained").GetBoolean());
            Assert.IsLessThan(30d, root.GetProperty("elapsedSeconds").GetDouble());
            var diagnostics = string.Join('\n', Directory.GetFiles(results, "*.diag").Select(File.ReadAllText));
            Assert.IsNotEmpty(diagnostics, "MTP must really start; an invalid helper invocation is not fault evidence.");
            if (scenario != "NoSuchTest")
            {
                var entry = File.ReadAllText(Path.Combine(directory.Path, "probe-entry.txt")).Split(':');
                Assert.HasCount(4, entry);
                Assert.AreEqual(nonce, entry[1]);
                Assert.AreEqual(scenario, entry[3]);
                var hostPid = int.Parse(entry[2], CultureInfo.InvariantCulture);
                Assert.Contains($"Started test host process. PID: '{hostPid}'", diagnostics, StringComparison.Ordinal);
                using var released = await ProbeLease.WaitForReleaseAsync(lease);
                using var reader = new StreamReader(released);
                Assert.AreEqual(nonce, reader.ReadToEnd(), "The actual host must release its lease, including hard termination.");
            }
            if (scenario is "Passed" or "AssertionFailure" or "NoSuchTest" or "Skipped")
            {
                var report = XDocument.Load(Path.Combine(results, "probe.trx"));
                var outcomes = report.Descendants().Where(value => value.Name.LocalName == "UnitTestResult").ToArray();
                Assert.HasCount(scenario == "NoSuchTest" ? 0 : 1, outcomes);
                if (outcomes.Length == 1)
                    Assert.AreEqual(scenario switch { "Passed" => "Passed", "AssertionFailure" => "Failed", _ => "NotExecuted" },
                        (string?)outcomes[0].Attribute("outcome"));
            }
            if (scenario == "UncooperativeHang")
                Assert.AreNotEqual(0, root.GetProperty("nativeExitCode").GetInt32());
        }
        finally
        {
            var archive = Path.Combine(TestContext.TestResultsDirectory!, "suite-controller-audit", nonce);
            Directory.CreateDirectory(archive);
            if (Directory.Exists(results))
                foreach (var source in Directory.EnumerateFiles(results, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(archive, Path.GetRelativePath(results, source));
                    if (Path.GetExtension(target).Equals(".trx", StringComparison.OrdinalIgnoreCase))
                        target += ".txt";
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target);
                    TestContext.AddResultFile(target);
                }
            var identity = Path.Combine(archive, "source-sha256.txt");
            File.WriteAllText(identity, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(script))));
            TestContext.AddResultFile(identity);
        }
    }
}
