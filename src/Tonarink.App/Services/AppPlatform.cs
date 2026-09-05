using System.Runtime.InteropServices;
using Microsoft.UI.Reactor;
using Microsoft.Windows.AppLifecycle;
using Package = Windows.ApplicationModel.Package;
using WinAppStorage = Microsoft.Windows.Storage;
using WinRtStorage = Windows.Storage;

static class AppPlatform
{
    public const string MinimizedArgument = "--minimized";
    public const string StartupTaskId = "TonarinkStartup";
    private const string UnpackagedPublisher = "kusutori";
    private const string UnpackagedProduct = "Tonarink";

    private static readonly Lazy<string> DataDirectoryValue = new(ResolveDataDirectory);
    private static readonly Lazy<string> DefaultDownloadDirectoryValue = new(ResolveDefaultDownloadDirectory);
    private static readonly Lazy<bool> StartHiddenValue = new(DetectStartHidden);

    public static string DataDirectory => DataDirectoryValue.Value;

    public static string SharedDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UnpackagedPublisher,
            UnpackagedProduct);

    public static string ExplorerShareDirectory
    {
        get
        {
            var directory = Path.Combine(SharedDataDirectory, "explorer-share");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string DefaultDownloadDirectory => DefaultDownloadDirectoryValue.Value;

    public static bool StartHidden => StartHiddenValue.Value;

    public static bool HasPackageIdentity()
    {
        try
        {
            _ = Package.Current.Id.Name;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public static string ExecutablePath =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("The current process path is unavailable.");

    public static WindowIcon AppWindowIcon
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            return File.Exists(path)
                ? WindowIcon.FromPath(path)
                : WindowIcon.FromPath(ExecutablePath);
        }
    }

    private static string ResolveDefaultDownloadDirectory()
    {
        try
        {
            var path = WinRtStorage.UserDataPaths.GetDefault().Downloads;
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }
        catch
        {
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }

    private static string ResolveDataDirectory()
    {
        var directory = HasPackageIdentity()
            ? WinRtStorage.ApplicationData.Current.LocalFolder.Path
            : WinAppStorage.ApplicationData.GetForUnpackaged(
                UnpackagedPublisher,
                UnpackagedProduct).LocalPath;
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static bool DetectStartHidden()
    {
        if (Environment.GetCommandLineArgs().Any(static argument =>
                string.Equals(argument, MinimizedArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!HasPackageIdentity())
            return false;

        try
        {
            return AppInstance.GetCurrent().GetActivatedEventArgs()?.Kind
                == ExtendedActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }
}
