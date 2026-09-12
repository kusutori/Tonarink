using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Tonarink.ExplorerCommand;

internal static partial class AppPaths
{
    public const string Clsid = "8840B22A-3B9C-4C12-A7D5-3019438A5F1F";
    public const string ShareFolderName = "explorer-share";
    public const string SettingsFileName = "settings.json";
    public const string MenuSettingName = "ShowExplorerContextMenu";
    public const string UnpackagedPublisher = "kusutori";
    public const string UnpackagedProduct = "Tonarink";
    public const string AppExeName = "Tonarink.exe";
    public const string Protocol = "tonarink:explorer-share";
    public const string ShareEventName = @"Local\Tonarink.ExplorerShare";
    public const string ApplicationId = "App";
    public const string PackageFolderPrefix = "Tonarink.App_";

    public static string SharedDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UnpackagedPublisher,
            UnpackagedProduct);

    public static string ShareDirectory => Path.Combine(SharedDataDirectory, ShareFolderName);

    public static bool IsMenuEnabled()
    {
        try
        {
            foreach (var directory in SettingsDirectories())
            {
                var path = Path.Combine(directory, SettingsFileName);
                if (!File.Exists(path))
                    continue;

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (!document.RootElement.TryGetProperty(MenuSettingName, out var value))
                    return true;

                return value.ValueKind != JsonValueKind.False;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            Report("Could not read the Explorer menu setting", exception);
        }

        return true;
    }

    public static string MenuTitle() => ExplorerStrings.Title();

    public static string? IconResource()
    {
        try
        {
            Directory.CreateDirectory(SharedDataDirectory);
            var publicIcon = Path.Combine(SharedDataDirectory, "AppIcon.ico");
            foreach (var source in IconSources())
            {
                if (!File.Exists(source))
                    continue;
                if (!File.Exists(publicIcon)
                    || File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(publicIcon)
                    || new FileInfo(publicIcon).Length == 0)
                {
                    File.Copy(source, publicIcon, overwrite: true);
                }

                break;
            }

            if (File.Exists(publicIcon) && new FileInfo(publicIcon).Length > 0)
                return publicIcon + ",0";

            var modulePath = ComServer.GetModuleFilePath();
            if (modulePath is not null && File.Exists(modulePath))
                return modulePath + ",0";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Report("Could not prepare the Explorer command icon", exception);
        }

        return null;
    }

    private static IEnumerable<string> IconSources()
    {
        if (TryGetCurrentPackagePath(out var packagePath))
            yield return Path.Combine(packagePath, "Assets", "AppIcon.ico");

        var modulePath = ComServer.GetModuleFilePath();
        if (modulePath is not null)
        {
            var directory = Path.GetDirectoryName(modulePath);
            if (!string.IsNullOrWhiteSpace(directory))
                yield return Path.Combine(directory, "Assets", "AppIcon.ico");
        }
    }

    public static void WriteShareRequest(IReadOnlyList<string> paths)
    {
        Directory.CreateDirectory(ShareDirectory);
        var file = Path.Combine(ShareDirectory, $"{Guid.NewGuid():N}.txt");
        File.WriteAllLines(file, paths);
        try
        {
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShareEventName);
            signal.Set();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            Report("Could not signal the running app about an Explorer share", exception);
        }
    }

    public static void LaunchApp()
    {
        try
        {
            if (TryActivatePackagedApp())
                return;

            var exe = Path.Combine(AppContext.BaseDirectory, AppExeName);
            if (!File.Exists(exe))
                return;

            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            Report("Could not launch Tonarink from Explorer", exception);
        }
    }

    public static nint Dup(string value) => Marshal.StringToCoTaskMemUni(value);

    private static bool TryActivatePackagedApp()
    {
        if (TryGetAppUserModelId(out var aumid) && TryActivateApplication(aumid))
            return true;

        var shell = ShellExecuteW(0, "open", Protocol, null, null, 1);
        return shell > 32;
    }

    private static bool TryActivateApplication(string aumid)
    {
        var clsid = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");
        var iid = new Guid("2e941141-7f97-4756-ba1d-9decde894a3d");
        if (CoCreateInstance(in clsid, 0, 5, in iid, out var manager) != 0 || manager == 0)
            return false;

        try
        {
            unsafe
            {
                var activate = (delegate* unmanaged[Stdcall]<nint, char*, char*, int, uint*, int>)
                    ComVtbl.Function(manager, 3);
                uint processId = 0;
                fixed (char* aumidPointer = aumid)
                fixed (char* argumentPointer = "explorer-share")
                {
                    return activate(manager, aumidPointer, argumentPointer, 0, &processId) == 0;
                }
            }
        }
        finally
        {
            ComVtbl.Release(manager);
        }
    }

    private static bool TryGetAppUserModelId(out string aumid)
    {
        aumid = "";
        if (TryGetPackageFamilyName(out var family) || TryFindInstalledPackageFamilyName(out family))
        {
            aumid = family + "!" + ApplicationId;
            return true;
        }

        return false;
    }

    internal static IEnumerable<string> SettingsDirectories()
    {
        if (TryGetPackageFamilyName(out var family))
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                family,
                "LocalState");
        }

        if (TryFindInstalledPackageFamilyName(out family))
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                family,
                "LocalState");
        }

        yield return SharedDataDirectory;
    }

    private static bool TryFindInstalledPackageFamilyName(out string family)
    {
        family = "";
        try
        {
            var packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages");
            if (!Directory.Exists(packages))
                return false;

            var match = Directory.GetDirectories(packages, PackageFolderPrefix + "*")
                .Select(Path.GetFileName)
                .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name));
            if (match is null)
                return false;

            family = match;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Report("Could not resolve the Tonarink package family", exception);
            return false;
        }
    }

    private static bool TryGetPackageFamilyName(out string family)
    {
        family = "";
        uint length = 128;
        Span<char> buffer = stackalloc char[(int)length];
        int status;
        unsafe
        {
            fixed (char* pointer = buffer)
                status = GetCurrentPackageFamilyName(ref length, pointer);
        }

        if (status == 122)
        {
            buffer = new char[length];
            unsafe
            {
                fixed (char* pointer = buffer)
                    status = GetCurrentPackageFamilyName(ref length, pointer);
            }
        }

        if (status != 0 || length == 0)
            return false;

        family = buffer[..Math.Max(0, (int)length - 1)].ToString();
        return family.Length > 0;
    }

    internal static bool TryGetCurrentPackagePath(out string path)
    {
        path = "";
        uint length = 32768;
        Span<char> buffer = stackalloc char[(int)length];
        int status;
        unsafe
        {
            fixed (char* pointer = buffer)
                status = GetCurrentPackagePath(ref length, pointer);
        }

        if (status == 122)
        {
            buffer = new char[length];
            unsafe
            {
                fixed (char* pointer = buffer)
                    status = GetCurrentPackagePath(ref length, pointer);
            }
        }

        if (status != 0 || length == 0)
            return false;

        path = buffer[..Math.Max(0, (int)length - 1)].ToString();
        return path.Length > 0;
    }

    internal static void Report(string operation, Exception exception) =>
        Trace.WriteLine($"[Tonarink.ExplorerCommand] {operation}: {exception.GetType().Name}: {exception.Message}");

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, char* packageFamilyName);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackagePath(ref uint pathLength, char* path);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint ShellExecuteW(nint hwnd, string lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);
}
