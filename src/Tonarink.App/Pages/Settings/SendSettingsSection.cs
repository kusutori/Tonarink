using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SettingsCardElement;
using static Tonarink.Controls.SettingsExpanderElement;

using static Tonarink.Pages.Settings.SettingsSectionVisuals;

namespace Tonarink.Pages.Settings;

sealed record SendSettingsSectionProps(AppSettings Settings, Action<Func<AppSettings, AppSettings>> UpdateSettings);

sealed class SendSettingsSection : Component<SendSettingsSectionProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        return SettingsGroup(
            t.Message(new("App", "SettingsSend")),
            SettingsCard(
                header: t.Message(new("App", "SettingsExpandDragDrop")),
                description: t.Message(new("App", "SettingsExpandDragDropDescription")),
                headerIcon: AnimatedButtons.SettingsDragDropIcon(Props.Settings.ExpandDragDropToEntireApp)
                    .WithKey("settings-drag-drop-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.ExpandDragDropToEntireApp, value =>
                        Props.UpdateSettings(settings => settings with { ExpandDragDropToEntireApp = value }))
                    .AutomationName(t.Message(new("App", "SettingsExpandDragDrop")))
                    .HelpText(t.Message(new("App", "SettingsExpandDragDropDescription")))),
            SettingsCard(
                header: t.Message(new("App", "SettingsVerifyChecksumsOnSend")),
                description: t.Message(new("App", "SettingsVerifyChecksumsOnSendDescription")),
                headerIcon: AnimatedButtons.SettingsChecksumIcon(Props.Settings.VerifyChecksumsOnSend)
                    .WithKey("settings-send-checksum-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.VerifyChecksumsOnSend, value =>
                        Props.UpdateSettings(settings => settings with { VerifyChecksumsOnSend = value }))
                    .AutomationName(t.Message(new("App", "SettingsVerifyChecksumsOnSend")))
                    .HelpText(t.Message(new("App", "SettingsVerifyChecksumsOnSendDescription")))));
    }
}
