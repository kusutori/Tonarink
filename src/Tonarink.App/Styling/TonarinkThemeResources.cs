using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Tonarink.Styling;

static class TonarinkThemeResources
{
    public const string CriticalFill = "#C50F1F";
    public const string CriticalFillPointerOver = "#B10E1C";
    public const string CriticalFillPressed = "#960B18";
    public const string CriticalForeground = "#FFFFFF";

    public const string TrayMenuDarkFill = "#2C2C2C";
    public const string TrayMenuLightFill = "#F3F3F3";
    public const string TrayMenuDarkForeground = "#FFFFFF";
    public const string TrayMenuLightForeground = "#1B1B1B";
    public const string TrayMenuDarkBorder = "#3D3D3D";
    public const string TrayMenuLightBorder = "#E5E5E5";
    public const double TrayMenuAcrylicTintOpacity = 0.72;
    public const double TrayMenuAcrylicLuminosity = 0.85;

    public static Style? TrayMenuPresenterStyle(ElementTheme theme)
    {
        if (theme == ElementTheme.Default)
            return null;

        var dark = theme == ElementTheme.Dark;
        var fill = ColorFromHex(dark ? TrayMenuDarkFill : TrayMenuLightFill);
        var style = new Style(typeof(MenuFlyoutPresenter));
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new AcrylicBrush
        {
            TintColor = fill,
            FallbackColor = fill,
            TintOpacity = TrayMenuAcrylicTintOpacity,
            TintLuminosityOpacity = TrayMenuAcrylicLuminosity,
        }));
        style.Setters.Add(new Setter(Control.ForegroundProperty,
            new SolidColorBrush(ColorFromHex(dark ? TrayMenuDarkForeground : TrayMenuLightForeground))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(ColorFromHex(dark ? TrayMenuDarkBorder : TrayMenuLightBorder))));
        return style;
    }

    public static Color ColorFromHex(string hex)
    {
        var value = hex.AsSpan().Trim();
        if (value.StartsWith('#'))
            value = value[1..];

        if (value.Length != 6 && value.Length != 8)
            throw new ArgumentOutOfRangeException(nameof(hex), hex, "Expected #RRGGBB or #AARRGGBB.");

        var packed = Convert.ToUInt32(value.ToString(), 16);
        return value.Length == 6
            ? Color.FromArgb(255, (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed)
            : Color.FromArgb((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }
}
