using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Tonarink.TestSupport;

namespace Tonarink.Cli.E2E.Tests;

[TestClass]
[TestCategory("Distribution")]
public sealed class NativeArtifactTests
{
    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new AssertFailedException($"Distribution runs require {name}; never fall back to an installed release.");

    [TestMethod]
    public void ExecutableMatchesCurrentPackageHashAndNativeRunnerArchitecture()
    {
        var executable = Required("TONARINK_CLI_TEST_EXECUTABLE");
        var rid = Required("TONARINK_CLI_TEST_RID").Split('-');
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        Assert.AreEqual(os, rid[0]);
        Assert.AreEqual(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), rid[1]);
        var bytes = File.ReadAllBytes(executable);
        Assert.AreEqual(Required("TONARINK_CLI_EXPECTED_SHA256"), Convert.ToHexString(SHA256.HashData(bytes)), ignoreCase: true);
        if (os == "win")
        {
            Assert.AreEqual((ushort)0x5A4D, BitConverter.ToUInt16(bytes, 0));
            var pe = BitConverter.ToInt32(bytes, 0x3C);
            Assert.AreEqual(0x4550u, BitConverter.ToUInt32(bytes, pe));
            Assert.AreEqual((ushort)(rid[1] == "x64" ? 0x8664 : 0xAA64), BitConverter.ToUInt16(bytes, pe + 4));
            Assert.AreEqual((ushort)0x20B, BitConverter.ToUInt16(bytes, pe + 24));
            // PE32+ data-directory entry 14 is the CLR header. A native AOT
            // executable has no managed entry point/CLR dependency here.
            Assert.AreEqual(0u, BitConverter.ToUInt32(bytes, pe + 24 + 112 + 14 * 8));
        }
        else if (os == "linux")
        {
            Assert.AreEqual(0x464C457Fu, BitConverter.ToUInt32(bytes, 0));
            Assert.AreEqual((byte)2, bytes[4]);
            Assert.AreEqual((ushort)(rid[1] == "x64" ? 62 : 183), BitConverter.ToUInt16(bytes, 18));
        }
        else
        {
            Assert.AreEqual(0xFEEDFACFu, BitConverter.ToUInt32(bytes, 0));
            Assert.AreEqual(rid[1] == "x64" ? 0x01000007u : 0x0100000Cu, BitConverter.ToUInt32(bytes, 4));
            Assert.AreEqual(2u, BitConverter.ToUInt32(bytes, 12));
        }
    }

    [TestMethod]
    [DataRow("en-US", "Independent configuration/data directory")]
    [DataRow("zh-CN", "独立配置与数据目录")]
    public async Task DistributedVersionAndLocalizedHelpWorkWithoutProfileSideEffects(string language, string text)
    {
        using var directory = new TemporaryDirectory();
        var executable = Required("TONARINK_CLI_TEST_EXECUTABLE");
        var version = await ProcessRunner.RunAsync(executable, ["--version"], directory.Path, TimeSpan.FromSeconds(15));
        Assert.AreEqual(0, version.ExitCode, version.StandardError);
        Assert.Contains(Required("TONARINK_CLI_TEST_VERSION"), version.StandardOutput, StringComparison.Ordinal);
        var help = await ProcessRunner.RunAsync(executable, ["--language", language, "--help"], directory.Path, TimeSpan.FromSeconds(15));
        Assert.AreEqual(0, help.ExitCode, help.StandardError);
        Assert.Contains(text, help.StandardOutput, StringComparison.Ordinal);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(directory.Path));
    }
}
