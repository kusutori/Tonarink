using System.Runtime.InteropServices;

namespace Tonarink.WidgetProvider;

internal static class WidgetPaths
{
    public const string MutexName = @"Local\Tonarink.App.Running";
    public const string CommandEventName = @"Local\Tonarink.WidgetCommand";
    public const string SnapshotFileName = "widget-snapshot.json";
    public const string CommandFileName = "widget-command.json";
    public const string HistoryFileName = "receive-history.json";
    private const string UnpackagedPublisher = "kusutori";
    private const string UnpackagedProduct = "Tonarink";
    private const string AssetRoot = "ms-appx:///WidgetProvider/WidgetAssets/";

    public static string StatusOnIcon => AssetRoot + "status-on.png";
    public static string StatusOffIcon => AssetRoot + "status-off.png";

    public static string DeviceIcon(string? type) => type?.ToLowerInvariant() switch
    {
        "mobile" => AssetRoot + "device-mobile.png",
        "web" => AssetRoot + "device-web.png",
        "headless" => AssetRoot + "device-headless.png",
        "server" => AssetRoot + "device-server.png",
        _ => AssetRoot + "device-desktop.png",
    };

    public static string DataDirectory()
    {
        foreach (var directory in Candidates())
        {
            if (Directory.Exists(directory))
                return directory;
        }

        var fallback = UnpackagedDirectory();
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    public static string SnapshotPath() => Path.Combine(DataDirectory(), SnapshotFileName);

    public static string CommandPath() => Path.Combine(DataDirectory(), CommandFileName);

    public static string HistoryPath() => Path.Combine(DataDirectory(), HistoryFileName);

    private static IEnumerable<string> Candidates()
    {
        string? packaged = null;
        try
        {
            var package = Windows.ApplicationModel.Package.Current;
            packaged = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                package.Id.FamilyName,
                "LocalState");
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            // The widget provider can also run without package identity.
            WidgetLog.Write($"Package data path is unavailable; using unpackaged storage: {exception.Message}");
        }

        if (!string.IsNullOrWhiteSpace(packaged))
            yield return packaged;

        yield return UnpackagedDirectory();
    }

    private static string UnpackagedDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UnpackagedPublisher,
            UnpackagedProduct);
}

internal static partial class WidgetNative
{
    public static string UserLocale()
    {
        Span<char> buffer = stackalloc char[85];
        int length;
        unsafe
        {
            fixed (char* pointer = buffer)
                length = GetUserDefaultLocaleName(pointer, buffer.Length);
        }

        return length <= 1 ? "en-US" : buffer[..(length - 1)].ToString();
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetUserDefaultLocaleName(char* lpLocaleName, int cchLocaleName);
}
