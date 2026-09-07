using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;

static class ButtonStyleExtensions
{
    public static ButtonElement CriticalButton(this ButtonElement button) =>
        button.Resources(static resources => resources
            .Set("ButtonBackground", TonarinkThemeResources.CriticalFill)
            .Set("ButtonBackgroundPointerOver", TonarinkThemeResources.CriticalFillPointerOver)
            .Set("ButtonBackgroundPressed", TonarinkThemeResources.CriticalFillPressed)
            .Set("ButtonBackgroundDisabled", Theme.ControlFillDisabled)
            .Set("ButtonForeground", TonarinkThemeResources.CriticalForeground)
            .Set("ButtonForegroundPointerOver", TonarinkThemeResources.CriticalForeground)
            .Set("ButtonForegroundPressed", TonarinkThemeResources.CriticalForeground)
            .Set("ButtonForegroundDisabled", Theme.DisabledText)
            .Set("ButtonBorderBrush", TonarinkThemeResources.CriticalFill)
            .Set("ButtonBorderBrushPointerOver", TonarinkThemeResources.CriticalFillPointerOver)
            .Set("ButtonBorderBrushPressed", TonarinkThemeResources.CriticalFillPressed)
            .Set("ButtonBorderBrushDisabled", Theme.Ref("ControlFillColorTransparentBrush")));
}
