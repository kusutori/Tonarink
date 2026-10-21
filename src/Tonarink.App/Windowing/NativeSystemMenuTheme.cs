using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

namespace Tonarink.Windowing;

static partial class NativeSystemMenuTheme
{
    public static void Apply(int themeIndex)
    {
        try
        {
            if (new AccessibilitySettings().HighContrast)
            {
                _ = SetPreferredAppMode(PreferredAppMode.Default);
                FlushMenuThemes();
                return;
            }

            _ = SetPreferredAppMode(themeIndex switch
            {
                1 => PreferredAppMode.ForceLight,
                2 => PreferredAppMode.ForceDark,
                _ => PreferredAppMode.AllowDark,
            });
            FlushMenuThemes();
        }
        catch (DllNotFoundException exception)
        {
            AppDiagnostics.Report("Native menu theming is unavailable", exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            AppDiagnostics.Report("Native menu theme exports are unavailable", exception);
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
    // The return value is a theme mode, not an HRESULT; callers intentionally discard it.
    [LibraryImport("uxtheme.dll", EntryPoint = "#135")]
    private static partial PreferredAppMode SetPreferredAppMode(PreferredAppMode preferredAppMode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#136")]
    private static partial void FlushMenuThemes();
}
