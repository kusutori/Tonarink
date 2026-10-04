using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

enum SettingsToggleIconKind
{
    History,
    Startup,
    Pin,
    Notification,
    Contact,
    ContextMenu,
    Checksum,
    DragDrop,
    Language,
}

static class AnimatedSettingsToggleIcon
{
    public static Element Create(
        SettingsToggleIconKind kind,
        bool isOn,
        double iconSize = 24) =>
        Create(kind, isOn ? "On" : "Off", iconSize);

    public static Element Create(
        SettingsToggleIconKind kind,
        string state,
        double iconSize = 24)
    {
        _ = iconSize;

        return AnimatedIcon()
            .Set(icon =>
            {
                icon.Source ??= CreateSource(kind);
                icon.FallbackIconSource ??= new FontIconSource
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = kind switch
                    {
                        SettingsToggleIconKind.History => "\uE81C",
                        SettingsToggleIconKind.Startup => "\uEC4A",
                        SettingsToggleIconKind.Pin => "\uE72E",
                        SettingsToggleIconKind.Notification => "\uEA8F",
                        SettingsToggleIconKind.Contact => "\uE716",
                        SettingsToggleIconKind.ContextMenu => "\uE7AC",
                        SettingsToggleIconKind.Checksum => "\uF32A",
                        SettingsToggleIconKind.DragDrop => "\uF413",
                        SettingsToggleIconKind.Language => "\uF2B7",
                        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                    },
                };

                Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(icon, state);
            });
    }

    private static IAnimatedVisualSource2 CreateSource(SettingsToggleIconKind kind) => kind switch
    {
        SettingsToggleIconKind.History => new Tonarink.SettingsHistoryToggleIcon(),
        SettingsToggleIconKind.Startup => new Tonarink.SettingsStartupToggleIcon(),
        SettingsToggleIconKind.Pin => new Tonarink.SettingsPinToggleIcon(),
        SettingsToggleIconKind.Notification => new Tonarink.SettingsNotificationToggleIcon(),
        SettingsToggleIconKind.Contact => new Tonarink.SettingsContactToggleIcon(),
        SettingsToggleIconKind.ContextMenu => new Tonarink.SettingsContextMenuToggleIcon(),
        SettingsToggleIconKind.Checksum => new Tonarink.SettingsChecksumToggleIcon(),
        SettingsToggleIconKind.DragDrop => new Tonarink.SettingsDragDropToggleIcon(),
        SettingsToggleIconKind.Language => new Tonarink.SettingsLanguageIcon(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
