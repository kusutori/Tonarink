using Tonarink.Application;
using Windows.System.UserProfile;

namespace Tonarink.Services;

static class AppLocale
{
    public static string Resolve(int languageIndex) =>
        AppLanguages.Resolve(languageIndex, SystemUiCulture());

    public static string SystemUiCulture()
    {
        try
        {
            return AppLanguages.Match(GlobalizationPreferences.Languages.FirstOrDefault());
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.Runtime.InteropServices.COMException)
        {
            AppDiagnostics.Report("Could not resolve the system UI language", exception);
            return AppLanguages.DefaultCulture;
        }
    }
}
