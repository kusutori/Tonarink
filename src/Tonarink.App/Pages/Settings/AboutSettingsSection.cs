using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SettingsCardElement;
using static Tonarink.Controls.SettingsExpanderElement;

using static Tonarink.Pages.Settings.SettingsSectionVisuals;

namespace Tonarink.Pages.Settings;

sealed class AboutSettingsSection : Component
{
    public override Element Render()
    {
        var t = UseIntl();
        var version = typeof(SettingsPage).Assembly.GetName().Version is { } assemblyVersion
            ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
            : "dev";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        var aboutLinks = VStack(12,
            TextBlock(t.Message(new("App", "SettingsAboutRelationship")))
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.WrapWholeWords),
            VStack(0,
                HyperlinkButton(
                    t.Message(new("App", "SettingsAboutGitHub")),
                    AppIdentity.RepositoryUri),
                HyperlinkButton(
                    t.Message(new("App", "SettingsAboutLocalSend")),
                    new Uri("https://localsend.org")),
                HyperlinkButton(
                    t.Message(new("App", "SettingsAboutIssues")),
                    AppIdentity.IssuesUri),
                HyperlinkButton(
                    t.Message(new("App", "SettingsAboutThirdPartyLicenses")),
                    AppIdentity.NoticesUri)));
        return VStack(4,
            Subtitle(t.Message(new("App", "SettingsAbout")))
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .Margin(bottom: 8),
            SettingsExpander(
                    header: AppPlatform.DisplayName,
                    description: t.Message(
                        new("App", "SettingsAboutCopyright"),
                        ("year", DateTime.Now.Year)),
                    headerIcon: Icon(ImageIcon(new Uri(iconPath, UriKind.Absolute))).AccessibilityHidden(),
                    items:
                    [
                        SettingsCard(
                                contentAlignment: ContentAlignment.Left,
                                isClickEnabled: false,
                                isActionIconVisible: false,
                                content: aboutLinks)
                            .HAlign(HorizontalAlignment.Stretch),
                    ])
                .AutomationId("Settings.About.Expander")
                .Set(expander =>
                {
                    expander.Content = t.Message(
                        new("App", "SettingsAboutVersion"),
                        ("version", version));
                })
                .HAlign(HorizontalAlignment.Stretch),
            HStack(8,
                    HyperlinkButton(
                        t.Message(new("App", "SettingsAboutFeedback")),
                        AppIdentity.IssuesUri),
                    HyperlinkButton(
                        t.Message(new("App", "SettingsAboutViewLogs")),
                        onClick: () =>
                        {
                            AppDiagnostics.EnsureLogFile();
                            ShellLauncher.Reveal(AppDiagnostics.LogFilePath);
                        })
                )
                .HAlign(HorizontalAlignment.Left)
                .Margin(top: 8));
    }
}
