using System.Globalization;
using System.Resources;

namespace Tonarink.Cli;

// Source text is the resource key, keeping the English fallback at the call
// site. Only CLI-owned text is translated; protocol identifiers stay invariant.
static class CliText
{
    private static readonly ResourceManager Resources = new("Tonarink.CommandLine.Resources.CliStrings", typeof(CliText).Assembly);
    private static readonly ResourceManager Chinese = new("Tonarink.CommandLine.Resources.CliStrings.zh-CN", typeof(CliText).Assembly);

    public static string Get(string source) =>
        (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? Chinese : Resources)
            .GetString(source, CultureInfo.InvariantCulture) ?? source;
    public static string Get(string source, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, Get(source), arguments);

    // System.CommandLine 2.0's Simplified Chinese resources leave these help
    // labels in English. Translate labels in its generated output, not layout.
    public static string LibraryHelp(string help) => help
        .Replace("Description:", Get("Description:"), StringComparison.Ordinal)
        .Replace("[command]", Get("[command]"), StringComparison.Ordinal)
        .Replace("[options]", Get("[options]"), StringComparison.Ordinal);

    public static bool IsSupported(string? language) => language is null
        || language.Equals("system", StringComparison.OrdinalIgnoreCase)
        || language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
        || language.Equals("en-US", StringComparison.OrdinalIgnoreCase);

    public static IDisposable UseLanguage(string? language)
    {
        var culture = language?.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) == true ? "zh-CN"
            : language?.Equals("en-US", StringComparison.OrdinalIgnoreCase) == true ? "en-US"
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en-US";
        return UseCulture(CultureInfo.GetCultureInfo(culture));
    }

    // Current(Ui)Culture flows through async execution, not process-wide
    // defaults. Restore on exit, including when called on the GUI dispatcher.
    public static IDisposable UseCulture(CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return new CliScope(() =>
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        });
    }
}
