using System.Globalization;
using Tonarink.Application;

namespace Tonarink.Hybrid;

internal static class MauiLanguage
{
    public static TonarinkLanguage Current =>
        IPlatformApplication.Current?.Services.GetService<TonarinkAppState>()?.Settings.Language
        ?? TonarinkLanguage.System;

    public static string Locale => AppLanguages.Resolve(Current, CultureInfo.CurrentUICulture.Name);

    public static string Text(string key, params object?[] arguments) =>
        AppText.Get(key, Current, arguments);
}
