using System.Diagnostics;

namespace Tonarink.Services.Platform;

static class PowerToysPeekLauncher
{
    private static readonly string[] ExecutableCandidates =
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
    public static string DefaultExecutablePath { get; } =
        ExecutableCandidates.FirstOrDefault(File.Exists) ?? ExecutableCandidates[0];

    public static bool CanPreview(string? path, string? executablePath) =>
        ResolveExecutable(executablePath) is not null && IsLocalFile(path);

    public static bool TryPreview(string? path, string? executablePath)
    {
        var executable = ResolveExecutable(executablePath);
        if (executable is null || !IsLocalFile(path))
            return false;

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(Path.GetFullPath(path!));
            Process.Start(startInfo);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            AppDiagnostics.Report("Could not preview a file with PowerToys Peek", exception);
            return false;
        }
    }

    private static bool IsLocalFile(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path)
        && File.Exists(path);

    private static string? ResolveExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return File.Exists(expanded) ? expanded : null;
    }
}
