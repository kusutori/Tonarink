using System.Web;
using Microsoft.UI.Reactor.Navigation;

namespace Tonarink.Services.Activation;

static class AppDeepLinks
{
    // Keep page identity as AppRoute. The existing activation request carries
    // parameters; Reactor resolves URI patterns and their synthetic ancestors.
    private static readonly DeepLinkMap<AppNavigationRequest> Routes = new DeepLinkMap<AppNavigationRequest>()
        .Map("/open", _ => new())
        .Map("/receive", _ => new(AppRoute.Receive))
        .Map("/send", ReadSend)
        .Map("/settings", _ => new(AppRoute.Settings))
        .Map("/history", ReadHistory, () => [new(AppRoute.Receive)])
        .Map("/receive/web", _ => new(AppRoute.WebReceive), () => [new(AppRoute.Receive)])
        .Map("/send/web", _ => new(AppRoute.WebShare), () => [new(AppRoute.Send)])
        .Map("/settings/network-interfaces", _ => new(AppRoute.NetworkInterfaces), () => [new(AppRoute.Settings)]);

    // Jump-list arguments are Launch activation, not Protocol activation. Both
    // boundaries call this parser; recognized but invalid links never become CLI.
    public static bool TryHandle(string? value)
    {
        if (value is null) return false;
        var text = value.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
        if (!text.StartsWith("tonarink:", StringComparison.OrdinalIgnoreCase)) return false;
        if (TryResolve(text, out var request)) AppNavigationService.Open(request!);
        else AppDiagnostics.Report("Invalid Tonarink deep link", new ArgumentException("Unknown route or invalid navigation parameters."));
        return true;
    }

    public static string ForFavorite(string fingerprint) =>
        "tonarink://send?favorite=" + Uri.EscapeDataString(fingerprint);

    public static string ForHistory(Guid id) => "tonarink://history?id=" + id.ToString("D");

    public static AppNavigationRequest Resolve(string value) =>
        TryResolve(value, out var request) ? request!
            : throw new ArgumentException("Unknown Tonarink route or invalid navigation parameters.", nameof(value));

    private static bool TryResolve(string text, out AppNavigationRequest? request)
    {
        request = null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("tonarink", StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Fragment.Length != 0) return false;

        // Existing jump lists use tonarink://history (history is the authority).
        // Also accept path-form tonarink:///history; Reactor matches paths, not hosts.
        var path = uri.Host.Length == 0 ? uri.AbsolutePath
            : "/" + uri.Host + (uri.AbsolutePath == "/" ? "" : uri.AbsolutePath);
        try
        {
            var result = Routes.Resolve(new UriBuilder(uri) { Host = "app", Path = path }.Uri);
            if (!result.Matched) return false;
            var target = result.Routes[^1];
            if (!HasValidQuery(uri, target.Route)) return false;
            request = target.Route is { } route ? target with
            {
                Navigation = new NavigationState<AppRoute>(
                    result.Routes[..^1].Select(parent => parent.Route!.Value).ToArray(), route, []),
            } : target;
            return true;
        }
        catch (FormatException) { return false; }
    }

    private static AppNavigationRequest ReadSend(RouteArgs args)
    {
        var fingerprint = args.QueryString("favorite");
        if (fingerprint is not null && (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 1024))
            throw new FormatException("Invalid favorite fingerprint.");
        return new(AppRoute.Send, Favorite: fingerprint);
    }

    private static AppNavigationRequest ReadHistory(RouteArgs args)
    {
        if (args.QueryString("id") is null) return new(AppRoute.History);
        var id = args.Query<Guid>("id");
        if (id == Guid.Empty) throw new FormatException("Invalid history identifier.");
        return new(AppRoute.History, History: id);
    }

    private static bool HasValidQuery(Uri uri, AppRoute? route)
    {
        // Keep the activation boundary strict without implementing another URI
        // parser: unknown or repeated parameters must not turn into a different action.
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (query.Count == 0) return true;
        var allowed = route switch
        {
            AppRoute.Send => "favorite",
            AppRoute.History => "id",
            null or AppRoute.Receive or AppRoute.Settings or AppRoute.NetworkInterfaces
                or AppRoute.WebShare or AppRoute.WebReceive or AppRoute.DeviceDetails => null,
        };
        return allowed is not null && query.Count == 1 && query.AllKeys[0] == allowed
            && query.GetValues(allowed) is { Length: 1 };
    }
}
