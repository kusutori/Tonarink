using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Tonarink.Components.Animations;

// LottieGen sources share this WinUI interface; no reflection, generated-code
// changes or per-icon adapters are needed to update their theme property.
static class AnimatedIconForeground
{
    public static void Apply(FrameworkElement element, IAnimatedVisualSource2 source,
        Control? owner = null, bool isChecked = false, bool critical = false)
    {
        var theme = owner?.ActualTheme ?? element.ActualTheme;
        if (theme == ElementTheme.Default && element.XamlRoot?.Content is FrameworkElement root)
            theme = root.ActualTheme;
        var isDark = theme == ElementTheme.Dark;

        if (owner is ToggleButton)
        {
            // The animation overlays the button instead of living in its
            // ContentPresenter. Resolve the same state resource on the native
            // button, where WinUI honors local themes and resource overrides.
            // Bind both states so unchecking cannot retain the accent foreground.
            owner.SetThemeResourceBinding(Control.ForegroundProperty,
                isChecked ? "ToggleButtonForegroundChecked" : "ToggleButtonForeground");
        }

        Color color;
        if (critical && ThemeRef.Resolve("SystemFillColorCriticalBrush", isDark) is SolidColorBrush criticalBrush)
            color = criticalBrush.Color;
        else if (owner?.Foreground is SolidColorBrush foreground && (!isChecked || owner is ToggleButton))
            color = foreground.Color;
        else if (isChecked)
            // Before the button is mounted, fall back to the semantic accent
            // text brush. Loaded reapplies the element-scoped native resolution.
            color = ThemeRef.Resolve("TextOnAccentFillColorPrimaryBrush", isDark) is SolidColorBrush selected
                ? selected.Color : isDark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        else
            // Match regular WinUI text instead of assuming every theme uses
            // pure black/white. Keep a fallback for pre-mount resource lookup.
            color = ThemeRef.Resolve("TextFillColorPrimaryBrush", isDark) is SolidColorBrush primary
                ? primary.Color : isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;

        source.SetColorProperty("Foreground", color);
    }
}
