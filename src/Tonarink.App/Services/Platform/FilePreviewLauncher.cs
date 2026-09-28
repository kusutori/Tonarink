using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace Tonarink.Services.Platform;

enum FilePreviewProvider
{
    PowerToysPeek,
    QuickLook,
}

static class FilePreviewLauncher
{
    private const string QuickLookPipeMessage = "QuickLook.App.PipeMessages.Toggle";

    private static readonly string[] PowerToysPeekExecutableCandidates =
    [
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerToys",
            "WinUI3Apps",
            "PowerToys.Peek.UI.exe"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerToys",
            "WinUI3Apps",
            "PowerToys.Peek.UI.exe"),
    ];

    private static readonly string[] QuickLookExecutableCandidates =
    [
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "QuickLook",
            "QuickLook.exe"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "QuickLook",
            "QuickLook.exe"),
    ];

    public static string DefaultPowerToysPeekExecutablePath { get; } =
        FindDefaultExecutable(PowerToysPeekExecutableCandidates);

    public static string DefaultQuickLookExecutablePath { get; } =
        FindDefaultExecutable(QuickLookExecutableCandidates);

    public static bool IsAvailable(
        FilePreviewProvider provider,
        string? executablePath) =>
        provider switch
        {
            FilePreviewProvider.PowerToysPeek => ResolveExecutable(executablePath) is not null,
            FilePreviewProvider.QuickLook => ResolveExecutable(executablePath) is not null || IsQuickLookRunning(),
            _ => false,
        };

    public static bool CanPreview(string? path) => IsLocalFile(path);

    public static bool TryPreview(
        FilePreviewProvider provider,
        string? path,
        string? executablePath)
    {
        if (!IsLocalFile(path))
            return false;

        var fullPath = Path.GetFullPath(path!);
        return provider switch
        {
            FilePreviewProvider.PowerToysPeek => TryStartExecutable(
                executablePath,
                fullPath,
                "Could not preview a file with PowerToys Peek"),
            FilePreviewProvider.QuickLook => TryQuickLook(executablePath, fullPath),
            _ => false,
        };
    }

    private static bool TryQuickLook(string? executablePath, string fullPath)
    {
        if (ResolveExecutable(executablePath) is not null)
        {
            return TryStartExecutable(
                executablePath,
                fullPath,
                "Could not preview a file with QuickLook");
        }

        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrWhiteSpace(sid))
                return false;

            using var client = new NamedPipeClientStream(
                ".",
                $"QuickLook.App.Pipe.{sid}",
                PipeDirection.Out);
            client.Connect(1000);
            using var writer = new StreamWriter(client);
            writer.WriteLine($"{QuickLookPipeMessage}|{fullPath}");
            writer.Flush();
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or TimeoutException
            or UnauthorizedAccessException)
        {
            AppDiagnostics.Report("Could not reach QuickLook through its command bridge", exception);
            return false;
        }
    }

    private static bool TryStartExecutable(string? executablePath, string fullPath, string diagnosticMessage)
    {
        var executable = ResolveExecutable(executablePath);
        if (executable is null)
            return false;

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(fullPath);
            Process.Start(startInfo);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            AppDiagnostics.Report(diagnosticMessage, exception);
            return false;
        }
    }

    private static bool IsQuickLookRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("QuickLook");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            AppDiagnostics.Report("Could not determine whether QuickLook is running", exception);
            return false;
        }
    }

    private static bool IsLocalFile(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path)
        && File.Exists(path);

    private static string FindDefaultExecutable(IReadOnlyList<string> candidates) =>
        candidates.FirstOrDefault(File.Exists) ?? candidates[0];

    private static string? ResolveExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return File.Exists(expanded) ? expanded : null;
    }
}
