using Tonarink.Application;

namespace Tonarink.Application.Tests;

public sealed class AppLanguagesTests
{
    [Theory]
    [InlineData(0, "en-US", "en-US")]
    [InlineData(0, "zh-CN", "zh-CN")]
    [InlineData(0, "zh-Hans-CN", "zh-CN")]
    [InlineData(1, "en-US", "zh-CN")]
    [InlineData(2, "zh-CN", "en-US")]
    public void ResolveMapsIndexAndSystemCulture(int index, string system, string expected) =>
        Assert.Equal(expected, AppLanguages.Resolve(index, system));

    [Fact]
    public void MatchFallsBackToEnglish() =>
        Assert.Equal(AppLanguages.English, AppLanguages.Match("ja-JP"));

    [Fact]
    public void FormatReplacesNamedPlaceholders() =>
        Assert.Equal("3 nearby", AppLanguages.Format("{count} nearby", ("count", 3)));
}
