using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SettingsCardElement;
using static Tonarink.Controls.SettingsExpanderElement;

using static Tonarink.Pages.Settings.SettingsSectionVisuals;

namespace Tonarink.Pages.Settings;

sealed record ReceiveSettingsSectionProps(
    AppSettings Settings,
    SettingsInputDraft Draft,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    Action<Func<SettingsInputDraft, SettingsInputDraft>> UpdateDraft,
    Action<string?> ReportError);

sealed class ReceiveSettingsSection : Component<ReceiveSettingsSectionProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var storagePicker = new StoragePicker(window?.NativeWindow, t.Message(new("App", "WindowUnavailable")));
        var setStatusMessage = Props.ReportError;
        var draft = Props.Draft;
        var UpdateDraft = Props.UpdateDraft;
        return SettingsGroup(
            t.Message(new("App", "SettingsReceive")),
            SettingsCard(
                header: t.Message(new("App", "SettingsSaveLocation")),
                description: Props.Settings.DownloadDirectory,
                headerIcon: HeaderGlyph(AppIcons.Folder),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                AnimatedButtons.OpenFolder(
                    t.Message(new("App", "ChangeSaveLocation")),
                    () => _ = PickDownloadDirectoryAsync(),
                    label: t.Message(new("App", "Change")))),
            SettingsCard(
                header: t.Message(new("App", "SettingsAutoReturnAfterReceive")),
                description: t.Message(new("App", "SettingsAutoReturnAfterReceiveDescription")),
                headerIcon: AnimatedButtons.SettingsAutoReturnIcon(Props.Settings.AutoReturnAfterReceive)
                    .WithKey("settings-auto-return-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.AutoReturnAfterReceive, value =>
                        Props.UpdateSettings(settings => settings with { AutoReturnAfterReceive = value }))
                    .AutomationName(t.Message(new("App", "SettingsAutoReturnAfterReceive")))
                    .AutomationId("Settings.Receive.AutoReturn")
                    .HelpText(t.Message(new("App", "SettingsAutoReturnAfterReceiveDescription")))),
            SettingsCard(
                header: t.Message(new("App", "SettingsShowReceiveBadge")),
                description: t.Message(new("App", "SettingsShowReceiveBadgeDescription")),
                headerIcon: AnimatedButtons.SettingsReceiveBadgeIcon(Props.Settings.ShowReceiveBadge)
                    .WithKey("settings-receive-badge-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.ShowReceiveBadge, value =>
                        Props.UpdateSettings(settings => settings with { ShowReceiveBadge = value }))
                    .AutomationName(t.Message(new("App", "SettingsShowReceiveBadge")))
                    .AutomationId("Settings.Receive.Badge")
                    .HelpText(t.Message(new("App", "SettingsShowReceiveBadgeDescription")))
                    .IsEnabled(AppPlatform.HasPackageIdentity())),
            SettingsCard(
                header: t.Message(new("App", "SettingsSaveReceiveHistory")),
                description: t.Message(new("App", "SettingsSaveReceiveHistoryDescription")),
                headerIcon: AnimatedButtons.SettingsHistoryIcon(Props.Settings.SaveReceiveHistory)
                    .WithKey("settings-history-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.SaveReceiveHistory, value =>
                        Props.UpdateSettings(settings => settings with { SaveReceiveHistory = value }))
                    .AutomationName(t.Message(new("App", "SettingsSaveReceiveHistory")))
                    .HelpText(t.Message(new("App", "SettingsSaveReceiveHistoryDescription")))),
            SettingsCard(
                header: t.Message(new("App", "SettingsVerifyChecksumsOnReceive")),
                description: t.Message(new("App", "SettingsVerifyChecksumsOnReceiveDescription")),
                headerIcon: AnimatedButtons.SettingsChecksumIcon(Props.Settings.VerifyChecksumsOnReceive)
                    .WithKey("settings-receive-checksum-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.VerifyChecksumsOnReceive, value =>
                        Props.UpdateSettings(settings => settings with { VerifyChecksumsOnReceive = value }))
                    .AutomationName(t.Message(new("App", "SettingsVerifyChecksumsOnReceive")))
                    .HelpText(t.Message(new("App", "SettingsVerifyChecksumsOnReceiveDescription")))),
            SettingsExpander(
                        content:
                        ToggleSwitch(
                                Props.Settings.ReceivePinEnabled,
                                value => Props.UpdateSettings(settings => settings with { ReceivePinEnabled = value }))
                            .AutomationName(t.Message(new("App", "SettingsReceivePinEnabled")))
                            .HelpText(t.Message(new("App", "SettingsReceivePinEnabledDescription"))),
                        headerIcon: AnimatedButtons.SettingsPinIcon(Props.Settings.ReceivePinEnabled)
                            .WithKey("settings-pin-icon"),
                        items:
                        [
                            SettingsCard(
                                header: t.Message(new("App", "SettingsReceivePin")),
                                description: t.Message(new("App", "SettingsReceivePinDescription")),
                                isClickEnabled: false,
                                isActionIconVisible: false,
                                content:
                                PasswordBox(
                                        draft.ReceivePin,
                                        value => UpdateDraft(current => current with { ReceivePin = value }),
                                        placeholderText: t.Message(new("App", "PinPlaceholder")))
                                    .MaxLength(32)
                                    .Required()
                                    .AutomationName(t.Message(new("App", "SettingsReceivePin")))
                                    .MinWidth(180)
                                    .IsEnabled(Props.Settings.ReceivePinEnabled)),
                        ])
                    .AutomationId("Settings.Pin.Expander") with
                {
                    Header = t.Message(new("App", "SettingsReceivePinProtection")),
                    Description = t.Message(new("App", "SettingsReceivePinProtectionDescription")),
                });

        async Task PickDownloadDirectoryAsync()
        {
            try
            {
                var folder = await storagePicker.PickFolderAsync(t.Message(new("App", "Change")));
                if (folder is null)
                    return;

                Props.UpdateSettings(settings => settings with { DownloadDirectory = folder.Path });
                setStatusMessage(null);
            }
            catch (Exception exception)
            {
                setStatusMessage(t.Message(
                    new("App", "PickFolderFailed"),
                    ("error", exception.Message)));
            }
        }
    }
}
