using Microsoft.UI.Xaml;

namespace Tonarink.Styling;

static class AppTheme
{
    public static ElementTheme ToElementTheme(int themeIndex) => themeIndex switch
    {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
