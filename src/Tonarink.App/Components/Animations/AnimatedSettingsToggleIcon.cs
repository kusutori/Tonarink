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
}

static class AnimatedSettingsToggleIcon
{
    public static Element Create(
        SettingsToggleIconKind kind,
        bool isOn,
        double iconSize = 24)
    {
        _ = iconSize;
        var state = isOn ? "On" : "Off";

        return AnimatedIcon()
            .Set(icon =>
            {
                icon.Source ??= CreateSource(kind);
                icon.FallbackIconSource ??= new FontIconSource
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = kind == SettingsToggleIconKind.History ? "\uE81C" : "\uEC4A",
                };

                Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(icon, state);
            });
    }

    private static IAnimatedVisualSource2 CreateSource(SettingsToggleIconKind kind) => kind switch
    {
        SettingsToggleIconKind.History => new Tonarink.SettingsHistoryToggleIcon(),
        SettingsToggleIconKind.Startup => new Tonarink.SettingsStartupToggleIcon(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
