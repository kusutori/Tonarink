namespace Tonarink.Application;

public sealed record LanguageChoice(int Index, TonarinkLanguage Language, string? Culture, string NameKey);

public static class AppLanguages
{
    public const string English = "en-US";
    public const string SimplifiedChinese = "zh-CN";
    public const string DefaultCulture = English;

    public static readonly IReadOnlyList<LanguageChoice> Choices =
    [
        new(0, TonarinkLanguage.System, null, "LanguageSystem"),
        new(1, TonarinkLanguage.SimplifiedChinese, SimplifiedChinese, "LanguageChinese"),
        new(2, TonarinkLanguage.English, English, "LanguageEnglish"),
    ];

    public static int MaxIndex => Choices.Count - 1;

    public static IReadOnlyList<string> ExplicitCultures { get; } =
        [.. Choices.Select(static choice => choice.Culture).OfType<string>()];

    public static bool IsValidIndex(int index) => index >= 0 && index <= MaxIndex;

    public static string Resolve(int languageIndex, string? systemCulture) =>
        languageIndex > 0 && languageIndex < Choices.Count && Choices[languageIndex].Culture is { } culture
            ? culture
            : Match(systemCulture);

    public static string Resolve(TonarinkLanguage language, string? systemCulture) =>
        Resolve((int)language, systemCulture);

    public static string? ToStoredCulture(int languageIndex) =>
        languageIndex > 0 && languageIndex < Choices.Count ? Choices[languageIndex].Culture : null;

    public static string Match(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return DefaultCulture;

        foreach (var choice in Choices)
        {
            if (choice.Culture is not null
                && culture.Equals(choice.Culture, StringComparison.OrdinalIgnoreCase))
                return choice.Culture;
        }

        var separator = culture.IndexOf('-');
        var prefix = separator < 0 ? culture.AsSpan() : culture.AsSpan(0, separator);
        foreach (var choice in Choices)
        {
            if (choice.Culture is null)
                continue;

            var choiceSeparator = choice.Culture.IndexOf('-');
            var choicePrefix = choiceSeparator < 0
                ? choice.Culture.AsSpan()
                : choice.Culture.AsSpan(0, choiceSeparator);
            if (prefix.Equals(choicePrefix, StringComparison.OrdinalIgnoreCase))
                return choice.Culture;
        }

        return DefaultCulture;
    }

    public static string Format(string template, params (string Name, object? Value)[] args)
    {
        foreach (var (name, value) in args)
            template = template.Replace("{" + name + "}", value?.ToString() ?? "", StringComparison.Ordinal);
        return template;
    }
}
