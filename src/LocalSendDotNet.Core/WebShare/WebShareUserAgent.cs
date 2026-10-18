namespace LocalSendDotNet;

internal static class WebShareUserAgent
{
    public static string Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return CoreText.UnknownDevice;

        var browser =
            userAgent.Contains("Edg/", StringComparison.Ordinal) ? "Edge" :
            userAgent.Contains("Chrome/", StringComparison.Ordinal) ? "Chrome" :
            userAgent.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox" :
            userAgent.Contains("Safari/", StringComparison.Ordinal) ? "Safari" :
            CoreText.Browser;
        var os =
            userAgent.Contains("Windows", StringComparison.Ordinal) ? "Windows" :
            userAgent.Contains("Android", StringComparison.Ordinal) ? "Android" :
            userAgent.Contains("iPhone", StringComparison.Ordinal) || userAgent.Contains("iPad", StringComparison.Ordinal) ? "iOS" :
            userAgent.Contains("Mac OS", StringComparison.Ordinal) ? "macOS" :
            userAgent.Contains("Linux", StringComparison.Ordinal) ? "Linux" :
            null;
        return os is null ? browser : $"{browser} ({os})";
    }
}
