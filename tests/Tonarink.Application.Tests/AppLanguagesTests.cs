using Tonarink.Application;

namespace Tonarink.Application.Tests;

[TestClass]
public sealed class AppLanguagesTests
{
    [TestMethod]
    [DataRow(0, "en-US", "en-US")]
    [DataRow(0, "zh-CN", "zh-CN")]
    [DataRow(0, "zh-Hans-CN", "zh-CN")]
    [DataRow(1, "en-US", "zh-CN")]
    [DataRow(2, "zh-CN", "en-US")]
    [DataRow(3, "en-US", "ja-JP")]
    [DataRow(4, "en-US", "ko-KR")]
    [DataRow(5, "en-US", "fr-FR")]
    [DataRow(6, "en-US", "es-ES")]
    public void ResolveMapsIndexAndSystemCulture(int index, string system, string expected) =>
        Assert.AreEqual(expected, AppLanguages.Resolve(index, system));

    [TestMethod]
    public void MatchFallsBackToEnglish() =>
        Assert.AreEqual(AppLanguages.English, AppLanguages.Match("de-DE"));

    [TestMethod]
    public void FormatReplacesNamedPlaceholders() =>
        Assert.AreEqual("3 nearby", AppLanguages.Format("{count} nearby", ("count", 3)));
}
