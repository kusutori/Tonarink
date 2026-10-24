using Microsoft.UI.Xaml.Media;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace Tonarink.Styling;

static class AppFonts
{
    // Text-based glyphs need an explicit icon font; FontIcon/FontIconSource
    // already use SymbolThemeFontFamily by default.
    public static FontFamily Symbols
    {
        get
        {
            if (XamlApplication.Current?.Resources is { } resources
                && resources.TryGetValue("SymbolThemeFontFamily", out var value)
                && value is FontFamily fontFamily)
                return fontFamily;

            return field;
        }
    } = new("Segoe Fluent Icons, Segoe MDL2 Assets");
}
