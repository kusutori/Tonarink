using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;

namespace Tonarink.ExplorerCommand;

internal static partial class ExplorerStrings
{
    public const string TitleKey = "ExplorerCommandTitle";
    public const string DefaultTitle = "Send with Tonarink";
    public const string DefaultLocale = "en-US";
    private const string LanguageTagSetting = "Language";

    public static string Title() => Get(TitleKey) ?? DefaultTitle;

    public static string? Get(string name)
    {
        foreach (var locale in LocaleCandidates())
        {
            foreach (var root in StringRoots())
            {
                var path = Path.Combine(root, locale, "App.resw");
                if (TryReadResw(path, name, out var value))
                    return value;
            }
        }

        return null;
    }

    private static IEnumerable<string> LocaleCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var locale in Expand(PreferredLocale()))
        {
            if (seen.Add(locale))
                yield return locale;
        }

        if (seen.Add(DefaultLocale))
            yield return DefaultLocale;
    }

    private static IEnumerable<string> Expand(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            yield break;

        yield return locale;
        var dash = locale.IndexOf('-');
        if (dash > 0)
            yield return locale[..dash];
    }

    private static string PreferredLocale()
    {
        try
        {
            foreach (var directory in AppPaths.SettingsDirectories())
            {
                var path = Path.Combine(directory, AppPaths.SettingsFileName);
                if (!File.Exists(path))
                    continue;

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (root.TryGetProperty(LanguageTagSetting, out var tag)
                    && tag.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(tag.GetString()))
                {
                    return tag.GetString()!;
                }

                break;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            AppPaths.Report("Could not read the preferred Explorer command language", exception);
        }

        return UserLocale();
    }

    private static IEnumerable<string> StringRoots()
    {
        if (AppPaths.TryGetCurrentPackagePath(out var packagePath))
            yield return Path.Combine(packagePath, "Strings");

        var modulePath = ComServer.GetModuleFilePath();
        if (modulePath is not null)
        {
            var directory = Path.GetDirectoryName(modulePath);
            if (!string.IsNullOrWhiteSpace(directory))
                yield return Path.Combine(directory, "Strings");
        }
    }

    private static bool TryReadResw(string path, string name, out string value)
    {
        value = "";
        if (!File.Exists(path))
            return false;

        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element
                    || !string.Equals(reader.Name, "data", StringComparison.Ordinal)
                    || !string.Equals(reader.GetAttribute("name"), name, StringComparison.Ordinal))
                {
                    continue;
                }

                using var subtree = reader.ReadSubtree();
                while (subtree.Read())
                {
                    if (subtree.NodeType == XmlNodeType.Element
                        && string.Equals(subtree.Name, "value", StringComparison.Ordinal))
                    {
                        var text = subtree.ReadElementContentAsString();
                        if (string.IsNullOrWhiteSpace(text))
                            return false;

                        value = text;
                        return true;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            AppPaths.Report($"Could not read Explorer resource '{path}'", exception);
        }

        return false;
    }

    private static string UserLocale()
    {
        Span<char> buffer = stackalloc char[85];
        int length;
        unsafe
        {
            fixed (char* pointer = buffer)
                length = GetUserDefaultLocaleName(pointer, buffer.Length);
        }

        if (length <= 1)
            return DefaultLocale;

        return buffer[..(length - 1)].ToString();
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetUserDefaultLocaleName(char* lpLocaleName, int cchLocaleName);
}
