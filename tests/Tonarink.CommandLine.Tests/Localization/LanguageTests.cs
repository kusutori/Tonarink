using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tonarink.Cli;

namespace Tonarink.CommandLine.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class LanguageTests
{
    [TestMethod]
    [DataRow("en-US", "Transfer cancelled")]
    [DataRow("zh-CN", "已取消传输")]
    public async Task LanguageScope_TranslatesAcrossAwaitAndRestoresCaller(string language, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        using (CliText.UseLanguage(language))
        {
            await Task.Yield();
            Assert.AreEqual(expected, CliText.Get("Transfer cancelled"));
            Assert.AreEqual("missing resource fallback", CliText.Get("missing resource fallback"));
        }
        Assert.AreSame(previous, CultureInfo.CurrentCulture);
        Assert.AreSame(previousUi, CultureInfo.CurrentUICulture);
    }

    [TestMethod]
    public void SystemLanguage_UnsupportedCultureFallsBackToEnglish()
    {
        using var caller = CliText.UseCulture(CultureInfo.GetCultureInfo("ja-JP"));
        using var language = CliText.UseLanguage("system");
        Assert.AreEqual("en-US", CultureInfo.CurrentUICulture.Name);
        Assert.AreEqual("Transfer cancelled", CliText.Get("Transfer cancelled"));
    }
}
