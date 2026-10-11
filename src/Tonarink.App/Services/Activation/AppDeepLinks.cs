namespace Tonarink.Services.Activation;

static class AppDeepLinks
{
    // Jump-list arguments are Launch activation, not Protocol activation. Both
    // boundaries call this parser; recognized but invalid links never become CLI.
    public static bool TryHandle(string? value)
    {
        if (value is null) return false;
        var text = value.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
        if (!text.StartsWith("tonarink:", StringComparison.OrdinalIgnoreCase)) return false;
        if (TryParse(text, out var request)) AppNavigationService.Open(request!);
        else AppDiagnostics.Report("Invalid Tonarink deep link", new ArgumentException("Unknown route or invalid navigation parameters."));
        return true;
    }

    public static string ForFavorite(string fingerprint) =>
        "tonarink://send?favorite=" + Uri.EscapeDataString(fingerprint);

    public static string ForHistory(Guid id) => "tonarink://history?id=" + id.ToString("D");

    private static bool TryParse(string text, out AppNavigationRequest? request)
    {
        request = null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("tonarink", StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Fragment.Length != 0
            || (uri.AbsolutePath.Length != 0 && uri.AbsolutePath != "/")) return false;

        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !query.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1])))
                return false;
        }
        switch (uri.Host.ToLowerInvariant())
        {
            case "open" when query.Count == 0: request = new(); break;
            case "receive" when query.Count == 0: request = new(AppRoute.Receive); break;
            case "settings" when query.Count == 0: request = new(AppRoute.Settings); break;
            case "send" when query.Count == 0: request = new(AppRoute.Send); break;
            case "send" when query.Count == 1 && query.TryGetValue("favorite", out var fingerprint)
                && !string.IsNullOrWhiteSpace(fingerprint) && fingerprint.Length <= 1024:
                request = AppNavigationRequest.ForFavorite(fingerprint); break;
            case "history" when query.Count == 0: request = new(AppRoute.History); break;
            case "history" when query.Count == 1 && query.TryGetValue("id", out var value)
                && Guid.TryParse(value, out var id) && id != Guid.Empty:
                request = AppNavigationRequest.ForHistory(id); break;
        }
        return request is not null;
    }
}
