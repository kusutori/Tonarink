using LocalSendDotNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tonarink.Cli;

namespace Tonarink.CommandLine.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class StandaloneSettingsTests
{
    [TestMethod]
    [DataRow("alias", "  测试 device  ", "测试 device")]
    [DataRow("port", "65535", "65535")]
    [DataRow("port", "1", "1")]
    [DataRow("discovery-timeout", "60000", "60000")]
    [DataRow("discovery-timeout", "1", "1")]
    [DataRow("encryption", "false", "false")]
    [DataRow("multicast", " 224.0.0.168 ", "224.0.0.168")]
    [DataRow("device-model", "  Model  ", "Model")]
    [DataRow("device-type", "mobile", "Mobile")]
    [DataRow("network-allowlist", " 127.0.0.*, ,10.0.0.* ", "127.0.0.*,10.0.0.*")]
    [DataRow("network-blocklist", " 10.0.0.* ", "10.0.0.*")]
    [DataRow("send-checksums", "off", "false")]
    [DataRow("receive-checksums", "on", "true")]
    [DataRow("receive-history", "0", "false")]
    [DataRow("auto-save", "FAVORITES", "favorites")]
    [DataRow("pin-enabled", "false", "false")]
    [DataRow("receive-pin", " 2468 ", "2468")]
    [DataRow("language", "ZH-cn", "zh-CN")]
    public void SetNormalizesSupportedValuesWithoutMutatingOriginal(string key, string value, string expected)
    {
        var original = new StandaloneSettings();
        var next = original.Set(key.ToUpperInvariant(), value, Path.GetTempPath());
        next.Validate();
        Assert.AreEqual(expected, Assert.ContainsSingle(next.List(true).Where(setting => setting.Key == key)).Value);
        Assert.AreEqual(new StandaloneSettings(), original);
    }

    [TestMethod]
    [DataRow("port", "0")]
    [DataRow("port", "65536")]
    [DataRow("port", "+1")]
    [DataRow("port", "abc")]
    [DataRow("discovery-timeout", "0")]
    [DataRow("discovery-timeout", "60001")]
    [DataRow("discovery-timeout", "-1")]
    [DataRow("multicast", "127.0.0.1")]
    [DataRow("multicast", "ff02::1")]
    [DataRow("multicast", "not-an-ip")]
    [DataRow("pin-enabled", "true")]
    [DataRow("receive-pin", "123456789012345678901234567890123")]
    [DataRow("auto-save", "all")]
    [DataRow("language", "system")]
    [DataRow("device-type", "999")]
    [DataRow("device-type", "unknown")]
    [DataRow("alias", " ")]
    [DataRow("encryption", "yes")]
    [DataRow("theme", "dark")]
    public void InvalidInputIsRejectedAsUsageErrorAndLeavesOriginalUnchanged(string key, string value)
    {
        var original = new StandaloneSettings();
        var failure = Assert.ThrowsExactly<CliException>(() => original.Set(key, value, Path.GetTempPath()));
        Assert.AreEqual(2, failure.ExitCode);
        Assert.AreEqual(new StandaloneSettings(), original);
    }

    [TestMethod]
    [DataRow("true", true)]
    [DataRow("on", true)]
    [DataRow("1", true)]
    [DataRow("false", false)]
    [DataRow("off", false)]
    [DataRow("0", false)]
    public void BooleanAliasesAreNormalized(string value, bool expected)
    {
        Assert.AreEqual(expected, new StandaloneSettings().Set("encryption", value, Path.GetTempPath()).Encryption);
    }

    [TestMethod]
    public void RelativeDownloadDirectoryUsesCallingDirectory()
    {
        var caller = Path.Combine(Path.GetTempPath(), "Tonarink.Tests", "synthetic-caller");
        var next = new StandaloneSettings().Set("download-directory", "downloads/子目录", caller);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(caller, "downloads", "子目录")), next.DownloadDirectory);
    }

    [TestMethod]
    public void NetworkListsAreExclusiveAndWhitespaceCanClearThem()
    {
        var allowed = new StandaloneSettings { NetworkBlocklist = ["10.0.0.*"] }
            .Set("network-allowlist", "127.0.0.*", Path.GetTempPath());
        Assert.IsNull(allowed.NetworkBlocklist);
        Assert.AreSequenceEqual(new[] { "127.0.0.*" }, allowed.NetworkAllowlist!);
        var blocked = allowed.Set("network-blocklist", "192.168.1.*", Path.GetTempPath());
        Assert.IsNull(blocked.NetworkAllowlist);
        Assert.AreSequenceEqual(new[] { "192.168.1.*" }, blocked.NetworkBlocklist!);
        Assert.IsNull(blocked.Set("network-blocklist", "  ", Path.GetTempPath()).NetworkBlocklist);
    }

    [TestMethod]
    public void PinDisclosureIsExplicitAndNodeUsesOnlyEnabledPin()
    {
        var settings = new StandaloneSettings().Set("receive-pin", "2468", Path.GetTempPath());
        Assert.AreEqual("<redacted>", Assert.ContainsSingle(settings.List(false).Where(setting => setting.Key == "receive-pin")).Value);
        Assert.AreEqual("2468", Assert.ContainsSingle(settings.List(true).Where(setting => setting.Key == "receive-pin")).Value);
        Assert.IsNull(settings.NodeOptions(Path.GetTempPath()).ReceivePin);
        var enabled = settings.Set("pin-enabled", "on", Path.GetTempPath());
        Assert.AreEqual("2468", enabled.NodeOptions(Path.GetTempPath()).ReceivePin);
    }
}
