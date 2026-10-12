using System.Globalization;
using System.Resources;

namespace Tonarink.Application;

// Shared user-facing runtime text. Protocol identifiers and diagnostic logs
// are deliberately not translated. Explicit language avoids async/global
// culture changes leaking between runtimes, browser requests and CLI calls.
public static class AppText
{
    private const string ResourcePrefix = "Tonarink.Application.Resources.UserMessages";
    private static readonly ResourceManager English = new(ResourcePrefix, typeof(AppText).Assembly);
    private static readonly IReadOnlyDictionary<string, ResourceManager> Translations =
        AppLanguages.ExplicitCultures.Where(locale => locale != AppLanguages.English)
            .ToDictionary(locale => locale,
                locale => new ResourceManager($"{ResourcePrefix}.{locale}", typeof(AppText).Assembly),
                StringComparer.OrdinalIgnoreCase);

    public static string Get(string key, params object?[] arguments) =>
        Get(key, TonarinkLanguage.System, arguments);

    public static string Get(string key, TonarinkLanguage language, params object?[] arguments)
    {
        var locale = AppLanguages.Resolve(language, CultureInfo.CurrentUICulture.Name);
        var resources = Translations.TryGetValue(locale, out var translated) ? translated : English;
        var template = resources.GetString(key, CultureInfo.InvariantCulture)
                       ?? English.GetString(key, CultureInfo.InvariantCulture) ?? key;
        return arguments.Length == 0 ? template
            : string.Format(CultureInfo.GetCultureInfo(locale), template, arguments);
    }
}
