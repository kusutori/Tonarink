using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

        Color color;
        if (critical && Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("SystemFillColorCritical", out var resource)
            && resource is Color criticalColor)
            color = criticalColor;
        else if (isChecked)
            // The overlay is not the template's ContentPresenter, so the native
            // button's Foreground can still be its unchecked color in light mode.
            color = ThemeRef.Resolve("ToggleButtonForegroundChecked", isDark) is SolidColorBrush selected
                ? selected.Color : isDark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        else if (owner?.Foreground is SolidColorBrush foreground)
            color = foreground.Color;
        else
            color = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;

        source.SetColorProperty("Foreground", color);
    }
}
