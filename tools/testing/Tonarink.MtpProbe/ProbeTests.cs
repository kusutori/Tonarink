using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace Tonarink.MtpProbe;

// Deliberately faulty tests, selected only by the owning controller. This
// project is not a solution test suite and contributes no passing test count.
[TestClass]
public sealed class ProbeTests
{
    private FileStream? _lease;
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void EnterOwnedProbe()
    {
        var lease = TestContext.Properties["probeLease"] as string;
        var nonce = Guid.Empty;
        if (lease is null || !Path.IsPathFullyQualified(lease) || !File.Exists(lease)
            || !Guid.TryParse(File.ReadAllText(lease), out nonce))
            Assert.Inconclusive("Only the owning audit controller may execute the fault fixture.");

        _lease = new FileStream(lease!, FileMode.Open, FileAccess.Read, FileShare.None);

        var marker = $"entered:{nonce:D}:{Environment.ProcessId}:{TestContext.TestName}";
        var evidence = Path.Combine(Path.GetDirectoryName(lease!)!, "probe-entry.txt");
        File.WriteAllText(evidence, marker);
        TestContext.AddResultFile(evidence);
        // Bypass MSTest's buffered Console capture: crash/hang evidence must
        // reach the parent even when the test cannot return a result.
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), leaveOpen: true);
        output.WriteLine(marker);
        output.Flush();
    }

    [TestCleanup]
    public void ReleaseOwnedProbe() => _lease?.Dispose();

    [TestMethod]
    public void Passed() => Assert.IsNotNull(_lease);

    [TestMethod]
    public void AssertionFailure() => Assert.Fail("intentional MTP assertion failure");

    [TestMethod]
    public void Killed() => Process.GetCurrentProcess().Kill();

    [TestMethod]
    public async Task UncooperativeHang() => await Task.Delay(Timeout.InfiniteTimeSpan);

    [TestMethod]
    public void Skipped() => Assert.Inconclusive("intentional MTP skipped test");
}
