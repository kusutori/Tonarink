using System.Globalization;
using System.Resources;
using Tonarink.Application;

namespace Tonarink.Cli;

// Source text is the resource key, keeping the English fallback at the call
// site. Only CLI-owned text is translated; protocol identifiers stay invariant.
static class CliText
{
    private static readonly ResourceManager Resources = new("Tonarink.CommandLine.Resources.CliStrings", typeof(CliText).Assembly);
    private static readonly IReadOnlyDictionary<string, ResourceManager> Translations =
        AppLanguages.ExplicitCultures.Where(locale => locale != AppLanguages.English)
            .ToDictionary(locale => locale,
                locale => new ResourceManager($"Tonarink.CommandLine.Resources.CliStrings.{locale}", typeof(CliText).Assembly),
                StringComparer.OrdinalIgnoreCase);

    public static string SupportedLanguages => "system, " + string.Join(", ", AppLanguages.ExplicitCultures);

    public static string Get(string source)
    {
        var locale = AppLanguages.Match(CultureInfo.CurrentUICulture.Name);
        var resources = Translations.TryGetValue(locale, out var translation) ? translation : Resources;
        return resources.GetString(source, CultureInfo.InvariantCulture)
               ?? Resources.GetString(source, CultureInfo.InvariantCulture) ?? source;
    }
    public static string Get(string source, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, Get(source), arguments);

    // Fill untranslated labels in library-generated help without replacing
    // System.CommandLine's help layout or translating command identifiers.
    public static string LibraryHelp(string help) => help
        .Replace("Description:", Get("Description:"), StringComparison.Ordinal)
        .Replace("[command]", Get("[command]"), StringComparison.Ordinal)
        .Replace("[options]", Get("[options]"), StringComparison.Ordinal);

    public static bool IsSupported(string? language) => language is null
        || language.Equals("system", StringComparison.OrdinalIgnoreCase)
        || AppLanguages.ExplicitCultures.Contains(language, StringComparer.OrdinalIgnoreCase);

    public static IDisposable UseLanguage(string? language)
    {
        var culture = AppLanguages.Match(string.IsNullOrWhiteSpace(language)
            || language.Equals("system", StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.CurrentUICulture.Name : language);
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
