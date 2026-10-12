using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Primitives;
using Tonarink.Application;

namespace Tonarink.Web;

// Keep the framework's query/cookie/header parsing and precedence, but map
// regional variants (fr-CA, es-MX, etc.) to the resources this app ships.
internal sealed class SupportedLocaleRequestCultureProvider(IRequestCultureProvider inner) : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var result = await inner.DetermineProviderCultureResult(httpContext);
        if (result is null) return null;
        var cultures = Normalize(result.Cultures);
        var uiCultures = Normalize(result.UICultures);
        if (cultures.Count == 0 && uiCultures.Count == 0) return null;
        return new ProviderCultureResult(cultures.Count > 0 ? cultures : uiCultures,
            uiCultures.Count > 0 ? uiCultures : cultures);
    }

    private static List<StringSegment> Normalize(IList<StringSegment> values)
    {
        var result = new List<StringSegment>();
        foreach (var value in values)
        {
            var tag = value.ToString();
            var dash = tag.IndexOf('-');
            var language = dash < 0 ? tag : tag[..dash];
            if (!AppLanguages.ExplicitCultures.Any(locale =>
                    locale.Split('-')[0].Equals(language, StringComparison.OrdinalIgnoreCase))) continue;
            var matched = AppLanguages.Match(tag);
            if (!result.Any(existing => existing.Equals(matched))) result.Add(matched);
        }
        return result;
    }
}
