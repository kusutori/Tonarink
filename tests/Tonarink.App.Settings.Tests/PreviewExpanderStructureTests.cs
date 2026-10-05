namespace Tonarink.App.Settings.Tests;

public sealed class PreviewExpanderStructureTests
{
    [Fact]
    public void CompactButtonsSetNativePaddingAndContentAlignment()
    {
        // These are source guards, not a substitute for native WinUI layout testing.
        foreach (var file in new[] { "AnimatedAddButton.cs", "AnimatedDeleteButton.cs", "AnimatedVerifyButton.cs" })
        {
            var source = ReadSource(file);
            Assert.Contains(".Padding(0, 0)", source);
            Assert.Contains(".HorizontalContentAlignment(HorizontalAlignment.Center)", source);
            Assert.Contains(".VerticalContentAlignment(VerticalAlignment.Center)", source);
        }

        var page = ReadSource("SettingsPage.cs");
        var preview = page[page.IndexOf("var experimentalCards", StringComparison.Ordinal)..];
        Assert.Contains("buttonSize: 32", preview);
        Assert.DoesNotContain(".Height(40)", preview);
        var content = ReadSource("FilePreviewProviderContent.cs");
        Assert.Contains("AnimatedButtons.VerifyFeedback(successVersion", content);
        Assert.Contains("AnimatedButtons.Delete(removeName, Props.Remove, subtle: true, iconSize: 20, buttonSize: 40)", content);
    }

    [Fact]
    public void VerificationFeedbackMatchesCopyKeyframeTimingsAndEasing()
    {
        static string[] Timeline(string source) => System.Text.RegularExpressions.Regex.Matches(source,
                @"\.Duration\(\d+\)|\.At\(\d\.\d{3}f,[^\r\n]+|easing: Easing\.[^\r\n]+")
            .Select(match => match.Value).ToArray();

        Assert.Equal(Timeline(ReadSource("AnimatedCopyButton.cs")), Timeline(ReadSource("AnimatedVerifyButton.cs")));
        var source = ReadSource("AnimatedVerifyButton.cs");
        Assert.Contains("UseReducedMotion()", source);
        Assert.Contains("Props.SuccessVersion", source);
        var content = ReadSource("FilePreviewProviderContent.cs");
        Assert.True(content.IndexOf("Props.SavePath(normalized)", StringComparison.Ordinal)
                    < content.IndexOf("setSuccessVersion(++successVersionRef.Current)", StringComparison.Ordinal));
    }

    private static string ReadSource(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UiSources", name));

    [Fact]
    public void OverrideInputIsBoundedAndOnlyExplicitValidationSaves()
    {
        // Source guard only: native layout still requires an interactive WinUI check.
        var content = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UiSources", "FilePreviewProviderContent.cs"));

        Assert.DoesNotContain("OnLostFocus", content);
        Assert.Contains(".Width(300)", content);
        Assert.Contains(".Width(396)", content);
        Assert.Contains("FilePreviewSettings.TryValidateOverride(pathRef.Current, out var normalized)", content);
        Assert.Contains("Props.SavePath(normalized)", content);
        Assert.Contains("SettingsFilePreviewAutoPathPlaceholder", content);
    }

    [Fact]
    public void PreviewItemsUseNativeCardFactoryAndOnlyWrapCardContent()
    {
        // A Component item mounts as a Border. SettingsExpander applies its
        // SettingsCard style to that Border and terminates the app on expansion.
        // Keep a source guard in these headless tests; WinUI realization itself
        // still needs an interactive application test.
        var page = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UiSources", "SettingsPage.cs"));
        var content = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UiSources", "FilePreviewProviderContent.cs"));

        Assert.Contains("FilePreviewProviderContent.CreateCard(t, new(", page);
        Assert.DoesNotContain("Component<FilePreviewProviderCard", page);
        Assert.DoesNotContain("Component<FilePreviewProviderContent", page);
        Assert.Contains("public static SettingsCardElement CreateCard(", content);
        Assert.Contains("content: Component<FilePreviewProviderContent, FilePreviewProviderContentProps>", content);
    }
}
