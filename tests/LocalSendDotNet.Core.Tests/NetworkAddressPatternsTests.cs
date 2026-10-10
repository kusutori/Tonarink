using LocalSendDotNet;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
public sealed class NetworkAddressPatternsTests
{
    [TestMethod]
    public void NullListsDoNotIgnoreInterfaces()
    {
        Assert.IsFalse(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], null, null));
    }

    [TestMethod]
    public void EmptyWhitelistIgnoresEveryInterface()
    {
        Assert.IsTrue(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], [""], null));
        Assert.IsTrue(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], [], null));
    }

    [TestMethod]
    public void WhitelistKeepsMatchingInterface()
    {
        Assert.IsFalse(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], ["192.168.1.*"], null));
        Assert.IsTrue(NetworkAddressPatterns.IsInterfaceIgnored(["10.0.0.8"], ["192.168.1.*"], null));
    }

    [TestMethod]
    public void BlacklistIgnoresMatchingInterface()
    {
        Assert.IsTrue(NetworkAddressPatterns.IsInterfaceIgnored(["10.0.0.8"], null, ["10.0.0.*"]));
        Assert.IsFalse(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], null, ["10.0.0.*"]));
    }

    [TestMethod]
    public void BlacklistIgnoresWholeInterfaceWhenAnyAddressMatches()
    {
        Assert.IsTrue(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20", "10.0.0.8"], null, ["10.0.0.8"]));
    }

    [TestMethod]
    public void WhitelistTakesPrecedenceOverBlacklist()
    {
        Assert.IsFalse(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], ["192.168.1.20"], ["192.168.1.20"]));
        Assert.IsTrue(NetworkAddressPatterns.IsInterfaceIgnored(["10.0.0.8"], ["192.168.1.20"], ["10.0.0.8"]));
    }

    [TestMethod]
    [DataRow("192.168.1.20", "192.168.1.20", true)]
    [DataRow("192.168.1.*", "192.168.1.20", true)]
    [DataRow("192.168.*.20", "192.168.9.20", true)]
    [DataRow("192.168.1.*", "192.168.2.20", false)]
    [DataRow("192.168.1.20", "192.168.1.21", false)]
    [DataRow("", "192.168.1.20", false)]
    [DataRow("192.168.1", "192.168.1.20", false)]
    [DataRow("not-an-ip", "192.168.1.20", false)]
    public void MatchesDottedQuadPatterns(string pattern, string address, bool expected) =>
        Assert.AreEqual(expected, NetworkAddressPatterns.Matches(pattern, address));
}
