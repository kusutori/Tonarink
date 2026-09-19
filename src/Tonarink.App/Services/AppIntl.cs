using Microsoft.UI.Reactor.Localization;

namespace Tonarink.Services;

static class AppIntl
{
    private static readonly MessageCache Cache = new();

    public static IntlAccessor For(AppSettings settings) =>
        new(AppLocale.Resolve(settings.LanguageIndex), AppShell.Resources, Cache);
}
