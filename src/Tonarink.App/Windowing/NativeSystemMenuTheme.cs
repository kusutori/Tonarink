using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

static class NativeSystemMenuTheme
{
    public static void Apply(int themeIndex)
    {
        try
        {
            if (new AccessibilitySettings().HighContrast)
            {
                SetPreferredAppMode((int)PreferredAppMode.Default);
                FlushMenuThemes();
                return;
            }

            SetPreferredAppMode(themeIndex switch
            {
                1 => (int)PreferredAppMode.ForceLight,
                2 => (int)PreferredAppMode.ForceDark,
                _ => (int)PreferredAppMode.AllowDark,
            });
            FlushMenuThemes();
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private enum PreferredAppMode
    {
        Default = 0,
        AllowDark = 1,
        ForceDark = 2,
        ForceLight = 3,
    }

    // Undocumented uxtheme exports used by Explorer to theme Win32 menus.
    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();
}
