using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SettingsCardElement;
using static Tonarink.Controls.SettingsExpanderElement;

using static Tonarink.Pages.Settings.SettingsSectionVisuals;

namespace Tonarink.Pages.Settings;

sealed record GeneralSettingsSectionProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    Action<string?> ReportError);

sealed class GeneralSettingsSection : Component<GeneralSettingsSectionProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var (languageAnimationVersion, setLanguageAnimationVersion) = UseState(0);
        var setStatusMessage = Props.ReportError;
        string[] themeOptions =
        [
            t.Message(new("App", "OptionSystem")),
            t.Message(new("App", "ThemeLight")),
            t.Message(new("App", "ThemeDark")),
        ];
        string[] languageOptions =
        [
            .. AppLanguages.Choices.Select(choice => t.Message(new("App", choice.NameKey))),
        ];
        string[] trayClickActionOptions =
        [
            t.Message(new("App", "SettingsTrayClickOpenPanel")),
            t.Message(new("App", "SettingsTrayClickOpenWindow")),
        ];
        string[] notificationDefaultActionOptions =
        [
            t.Message(new("App", "SettingsNotificationsDefaultOpenFile")),
            t.Message(new("App", "SettingsNotificationsDefaultShowInFolder")),
        ];
        return SettingsGroup(
            t.Message(new("App", "SettingsGeneral")),
            SettingsCard(
                header: t.Message(new("App", "SettingsTheme")),
                description: t.Message(new("App", "SettingsThemeDescription")),
                headerIcon: AnimatedButtons.SettingsThemeIcon(Props.Settings.ThemeIndex)
                    .WithKey("settings-theme-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ComboBox(themeOptions, Props.Settings.ThemeIndex, index =>
                    {
                        if (index is >= 0 and <= 2 && index != Props.Settings.ThemeIndex)
                            Props.UpdateSettings(settings => settings with { ThemeIndex = index });
                    })
                    .AutomationName(t.Message(new("App", "SettingsTheme")))
                    .HelpText(t.Message(new("App", "SettingsThemeDescription")))
                    .MinWidth(180)),
            SettingsCard(
                header: t.Message(new("App", "SettingsLanguage")),
                description: t.Message(new("App", "SettingsLanguageDescription")),
                headerIcon: AnimatedButtons.SettingsLanguageIcon(languageAnimationVersion)
                    .WithKey("settings-language-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ComboBox(languageOptions, Props.Settings.LanguageIndex, index =>
                    {
                        if (AppLanguages.IsValidIndex(index) && index != Props.Settings.LanguageIndex)
                            Props.UpdateSettings(settings => settings with { LanguageIndex = index });
                    })
                    .AutomationName(t.Message(new("App", "SettingsLanguage")))
                    .HelpText(t.Message(new("App", "SettingsLanguageDescription")))
                    .DropDownOpened(() =>
                        setLanguageAnimationVersion(languageAnimationVersion + 1))
                    .MinWidth(180)),
            SettingsCard(
                header: t.Message(new("App", "SettingsUseTopNavigationWhenNarrow")),
                description: t.Message(new("App", "SettingsUseTopNavigationWhenNarrowDescription")),
                headerIcon: HeaderGlyph("\uE8A4"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.UseTopNavigationWhenNarrow, value =>
                        Props.UpdateSettings(settings => settings with { UseTopNavigationWhenNarrow = value }))
                    .AutomationName(t.Message(new("App", "SettingsUseTopNavigationWhenNarrow")))
                    .AutomationId("Settings.Navigation.TopWhenNarrow")
                    .HelpText(t.Message(new("App", "SettingsUseTopNavigationWhenNarrowDescription")))),
            SettingsExpander(
                        content:
                        ToggleSwitch(
                                Props.Settings.MinimizeToTray,
                                value => Props.UpdateSettings(settings => settings with { MinimizeToTray = value }))
                            .AutomationName(t.Message(new("App", "SettingsMinimizeToTray")))
                            .HelpText(t.Message(new("App", "SettingsMinimizeToTrayDescription"))),
                        headerIcon: AnimatedButtons.SettingsMinimizeToTrayIcon(Props.Settings.MinimizeToTray)
                            .WithKey("settings-minimize-to-tray-icon"),
                        items:
                        [
                            SettingsCard(
                                header: t.Message(new("App", "SettingsTrayClickOpensFlyout")),
                                description: t.Message(new("App", "SettingsTrayClickOpensFlyoutDescription")),
                                isClickEnabled: false,
                                isActionIconVisible: false,
                                content:
                                ComboBox(trayClickActionOptions, Props.Settings.TrayClickOpensFlyout ? 0 : 1, index =>
                                    {
                                        if (index is >= 0 and <= 1
                                            && (index == 0) != Props.Settings.TrayClickOpensFlyout)
                                        {
                                            Props.UpdateSettings(settings => settings with
                                            {
                                                TrayClickOpensFlyout = index == 0,
                                            });
                                        }
                                    })
                                    .AutomationName(t.Message(new("App", "SettingsTrayClickOpensFlyout")))
                                    .HelpText(t.Message(new("App", "SettingsTrayClickOpensFlyoutDescription")))
                                    .IsEnabled(Props.Settings.MinimizeToTray)
                                    .MinWidth(180)),
                        ])
                    .AutomationId("Settings.Tray.Expander") with
            {
                Header = t.Message(new("App", "SettingsMinimizeToTray")),
                Description = t.Message(new("App", "SettingsMinimizeToTrayDescription")),
            },
            SettingsCard(
                header: t.Message(new("App", "SettingsStartWithWindows")),
                description: t.Message(new("App", "SettingsStartWithWindowsDescription")),
                headerIcon: AnimatedButtons.SettingsStartupIcon(Props.Settings.StartWithWindows)
                    .WithKey("settings-startup-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.StartWithWindows, value =>
                        _ = SetStartupAsync(value))
                    .AutomationName(t.Message(new("App", "SettingsStartWithWindows")))
                    .HelpText(t.Message(new("App", "SettingsStartWithWindowsDescription")))),
            SettingsExpander(
                        content:
                        ToggleSwitch(
                                Props.Settings.NotificationsEnabled,
                                value =>
                                {
                                    Props.UpdateSettings(settings => settings with { NotificationsEnabled = value });
                                    AppNotificationService.SetEnabled(value);
                                })
                            .AutomationName(t.Message(new("App", "SettingsNotificationsEnabled")))
                            .AutomationId("Settings.Notifications.Enabled")
                            .HelpText(t.Message(new("App", "SettingsNotificationsEnabledDescription"))),
                        headerIcon: AnimatedButtons.SettingsNotificationIcon(Props.Settings.NotificationsEnabled)
                            .WithKey("settings-notification-icon"),
                        items:
                        [
                            SettingsCard(
                                header: t.Message(new("App", "SettingsNotificationsDefaultAction")),
                                description: t.Message(new("App", "SettingsNotificationsDefaultActionDescription")),
                                isClickEnabled: false,
                                isActionIconVisible: false,
                                content:
                                ComboBox(
                                        notificationDefaultActionOptions,
                                        (int)Props.Settings.NotificationDefaultAction,
                                        index =>
                                        {
                                            if (Enum.IsDefined(typeof(NotificationDefaultAction), index))
                                            {
                                                Props.UpdateSettings(settings => settings with
                                                {
                                                    NotificationDefaultAction = (NotificationDefaultAction)index,
                                                });
                                            }
                                        })
                                    .AutomationName(t.Message(new("App", "SettingsNotificationsDefaultAction")))
                                    .AutomationId("Settings.Notifications.Action")
                                    .HelpText(t.Message(new("App", "SettingsNotificationsDefaultActionDescription")))
                                    .IsEnabled(Props.Settings.NotificationsEnabled)
                                    .MinWidth(180)),
                            SettingsCard(
                                header: t.Message(new("App", "SettingsNotificationsTest")),
                                description: t.Message(new("App", "SettingsNotificationsTestDescription")),
                                isClickEnabled: false,
                                isActionIconVisible: false,
                                content:
                                Button(t.Message(new("App", "SettingsNotificationsSendTest")), () =>
                                    {
                                        var shown = AppNotificationService.TryShow(
                                            t.Message(new("App", "SettingsNotificationsTestTitle")),
                                            t.Message(new("App", "SettingsNotificationsTestMessage")),
                                            "test");
                                        setStatusMessage(shown
                                            ? null
                                            : t.Message(new("App", "SettingsNotificationsTestFailed")));
                                    })
                                    .AutomationName(t.Message(new("App", "SettingsNotificationsSendTest")))
                                    .AutomationId("Settings.Notifications.Test")
                                    .IsEnabled(Props.Settings.NotificationsEnabled)),
                        ])
                    .AutomationId("Settings.Notifications.Expander") with
            {
                Header = t.Message(new("App", "SettingsNotifications")),
                Description = t.Message(new("App", "SettingsNotificationsDescription")),
            },
            SettingsExpander(
                        headerIcon: AnimatedButtons.SettingsContextMenuIcon(Props.Settings.ShowExplorerContextMenu)
                            .WithKey("settings-context-menu-icon"),
                        content:
                        ToggleSwitch(Props.Settings.ShowExplorerContextMenu, value =>
                                Props.UpdateSettings(settings => settings with { ShowExplorerContextMenu = value }))
                            .AutomationName(t.Message(new("App", "SettingsExplorerContextMenu")))
                            .HelpText(t.Message(new("App", "SettingsExplorerContextMenuDescription")))
                            .IsEnabled(AppPlatform.HasPackageIdentity()),
                        items:
                        [
                            SettingsCard(
                                header: t.Message(new("App", "SettingsExplorerFavorites")),
                                description: t.Message(new("App", "SettingsExplorerFavoritesDescription")),
                                isClickEnabled: false,
                                isActionIconVisible: false,
                                content:
                                ToggleSwitch(Props.Settings.ShowFavoriteDevicesInExplorerMenu, value =>
                                        Props.UpdateSettings(settings =>
                                            settings with { ShowFavoriteDevicesInExplorerMenu = value }))
                                    .AutomationName(t.Message(new("App", "SettingsExplorerFavorites")))
                                    .AutomationId("Settings.Explorer.Favorites")
                                    .HelpText(t.Message(new("App", "SettingsExplorerFavoritesDescription")))
                                    .IsEnabled(AppPlatform.HasPackageIdentity() &&
                                               Props.Settings.ShowExplorerContextMenu)),
                        ])
                    .AutomationId("Settings.Explorer.Expander") with
            {
                Header = t.Message(new("App", "SettingsExplorerContextMenu")),
                Description = t.Message(new("App", "SettingsExplorerContextMenuDescription")),
            },
            SettingsCard(
                header: t.Message(new("App", "SettingsSendTo")),
                description: t.Message(new("App", "SettingsSendToDescription")),
                headerIcon: HeaderGlyph(AppIcons.Send),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.ShowInSendToMenu, SetSendToEnabled)
                    .AutomationName(t.Message(new("App", "SettingsSendTo")))
                    .HelpText(t.Message(new("App", "SettingsSendToDescription")))
                    .IsEnabled(!AppPlatform.HasPackageIdentity())));

        async Task SetStartupAsync(bool enabled)
        {
            try
            {
                await WindowsStartup.SetEnabledAsync(enabled, Props.Settings.MinimizeToTray);
                Props.UpdateSettings(settings => settings with { StartWithWindows = enabled });
                setStatusMessage(null);
            }
            catch (StartupDisabledException exception)
            {
                setStatusMessage(t.Message(new("App", exception.ResourceKey)));
            }
            catch (Exception exception)
            {
                setStatusMessage(t.Message(
                    new("App", "StartupFailed"),
                    ("error", exception.Message)));
            }
        }

        void SetSendToEnabled(bool enabled)
        {
            try
            {
                var next = Props.Settings with { ShowInSendToMenu = enabled };
                AppSettingsService.Save(next);
                Props.UpdateSettings(_ => next);
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report("Could not update the Send To shortcut", exception);
                setStatusMessage(t.Message(new("App", "SettingsSendToFailed"), ("error", exception.Message)));
            }
        }
    }
}
