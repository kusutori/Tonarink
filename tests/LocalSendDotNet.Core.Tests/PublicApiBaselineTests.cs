using LocalSendDotNet.ApiSurface;
using System.Security.Cryptography;
using System.Text;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
public sealed class PublicApiBaselineTests
{
    [TestMethod]
    public void ExportedApiMatchesApprovedBaseline()
    {
        var actual = PublicApiSurface.Create(typeof(LocalSendNode).Assembly);
        var baseline = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PublicApiBaseline.txt")).ReplaceLineEndings("\n").Trim();
        var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
        Assert.AreEqual(baseline, actualHash);
    }
}
