using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SettingsCardElement;
using static Tonarink.Controls.SettingsExpanderElement;

using static Tonarink.Pages.Settings.SettingsSectionVisuals;

namespace Tonarink.Pages.Settings;

sealed record ExperimentalSettingsSectionProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    Action<string?> ReportError);

sealed class ExperimentalSettingsSection : Component<ExperimentalSettingsSectionProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var (aliasAnimationVersion, setAliasAnimationVersion) = UseState(0);
        var reduceMotion = UseReducedMotion();
        var setStatusMessage = Props.ReportError;
        string[] filePreviewProviderOptions =
        [
            t.Message(new("App", "SettingsFilePreviewProviderPowerToysPeek")),
            t.Message(new("App", "SettingsFilePreviewProviderQuickLook")),
        ];
        var previewProviders = FilePreviewSettings.Providers(Props.Settings);
        return SettingsGroup(
            t.Message(new("App", "SettingsExperimental")),
            SettingsCard(
                    header: t.Message(new("App", "SettingsAppExecutionAliases")),
                    description: t.Message(new("App", "SettingsAppExecutionAliasesDescription")),
                    headerIcon: AnimatedButtons.SettingsExecutionAliasIcon(aliasAnimationVersion)
                        .WithKey("settings-execution-alias-icon"),
                    isClickEnabled: true,
                    isActionIconVisible: true,
                    onClick: () => _ = OpenExecutionAliasSettingsAsync())
                .AutomationName(t.Message(new("App", "SettingsAppExecutionAliases")))
                .HelpText(t.Message(new("App", "SettingsAppExecutionAliasesDescription")))
                .IsEnabled(AppPlatform.HasPackageIdentity()),
            SettingsCard(
                header: t.Message(new("App", "SettingsWindowsShareSuggestions")),
                description: t.Message(new("App", "SettingsWindowsShareSuggestionsDescription")),
                headerIcon: AnimatedButtons.SettingsContactIcon(Props.Settings.ShowFavoriteDevicesInWindowsShare)
                    .WithKey("settings-contact-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.ShowFavoriteDevicesInWindowsShare, value =>
                        Props.UpdateSettings(settings => settings with
                        {
                            ShowFavoriteDevicesInWindowsShare = value,
                        }))
                    .AutomationName(t.Message(new("App", "SettingsWindowsShareSuggestions")))
                    .HelpText(t.Message(new("App", "SettingsWindowsShareSuggestionsDescription")))
                    .IsEnabled(AppPlatform.HasPackageIdentity())),
            SettingsExpander(
                        content:
                        ToggleSwitch(Props.Settings.FilePreviewEnabled, value =>
                                Props.UpdateSettings(settings => settings with { FilePreviewEnabled = value }))
                            .AutomationName(t.Message(new("App", "SettingsFilePreviewEnabled")))
                            .AutomationId("Settings.Preview.Enabled")
                            .HelpText(t.Message(new("App", "SettingsFilePreviewEnabledDescription"))),
                        headerIcon: AnimatedButtons.SettingsPreviewIcon(Props.Settings.FilePreviewEnabled),
                        items:
                        [
                            SettingsCard(
                                header: t.Message(new("App", "SettingsFilePreviewProvider")),
                                description: t.Message(new("App", "SettingsFilePreviewProviderDescription")),
                                isClickEnabled: false,
                                isActionIconVisible: false,
                                content:
                                HStack(8,
                                        AnimatedButtons.Add(
                                                t.Message(new("App", "SettingsFilePreviewAddTool")),
                                                AddPreviewProvider,
                                                isEnabled: Props.Settings.FilePreviewEnabled
                                                           && !previewProviders.Contains(Props.Settings
                                                               .PreviewProvider),
                                                buttonSize: 32,
                                                automationId: "Settings.Preview.Add")
                                            .Size(32, 32)
                                            .VAlign(VerticalAlignment.Center),
                                        ComboBox(filePreviewProviderOptions, (int)Props.Settings.PreviewProvider,
                                                index =>
                                                {
                                                    if (Enum.IsDefined(typeof(FilePreviewProvider), index))
                                                    {
                                                        Props.UpdateSettings(settings => settings with
                                                        {
                                                            PreviewProvider = (FilePreviewProvider)index,
                                                        });
                                                    }
                                                })
                                            .AutomationName(t.Message(new("App", "SettingsFilePreviewProvider")))
                                            .AutomationId("Settings.Preview.Provider")
                                            .HelpText(t.Message(new("App", "SettingsFilePreviewProviderDescription")))
                                            .MinWidth(180)
                                            .VAlign(VerticalAlignment.Center)
                                            .IsEnabled(Props.Settings.FilePreviewEnabled))
                                    .VAlign(VerticalAlignment.Center)),
                            .. previewProviders.Select(provider =>
                                FilePreviewProviderContent.CreateCard(t, new(
                                        provider,
                                        FilePreviewSettings.ExecutablePath(Props.Settings, provider),
                                        path => Props.UpdateSettings(settings =>
                                            FilePreviewSettings.SetExecutablePath(settings, provider, path)),
                                        () => Props.UpdateSettings(settings =>
                                            FilePreviewSettings.Remove(settings, provider))))
                                    .WithKey($"preview-provider-{provider}")),
                        ])
                    .AutomationId("Settings.Preview.Expander") with
            {
                Header = t.Message(new("App", "SettingsFilePreview")),
                Description = t.Message(new("App", "SettingsFilePreviewDescription")),
            });

        async Task OpenExecutionAliasSettingsAsync()
        {
            if (!AppPlatform.HasPackageIdentity())
                return;

            if (!reduceMotion)
                setAliasAnimationVersion(aliasAnimationVersion + 1);

            try
            {
                var opened = await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:"));
                setStatusMessage(opened
                    ? null
                    : t.Message(new("App", "SettingsAppExecutionAliasesOpenFailed")));
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report("Could not open Windows Settings for app execution aliases", exception);
                setStatusMessage(t.Message(new("App", "SettingsAppExecutionAliasesOpenFailed")));
            }
        }

        void AddPreviewProvider()
        {
            var provider = Props.Settings.PreviewProvider;
            Props.UpdateSettings(settings => FilePreviewSettings.Add(settings, provider));
        }
    }
}
