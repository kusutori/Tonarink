using System.Diagnostics;

static class ShellLauncher
{
    public static bool Open(string? path) => Launch(path, reveal: false);

    public static bool Reveal(string? path) => Launch(path, reveal: true);

    private static bool Launch(string? path, bool reveal)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            return (Directory.Exists(fullPath), File.Exists(fullPath), reveal) switch
            {
                (true, _, _) => Start(fullPath),
                (false, true, false) => Start(fullPath),
                (false, true, true) => Start("explorer.exe", $"/select,\"{fullPath}\""),
                _ => false,
            };
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            AppDiagnostics.Report(reveal ? "Could not reveal a shell item" : "Could not open a shell item", exception);
            return false;
        }
    }

    private static bool Start(string fileName, string? arguments = null)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,
        });
        return true;
    }
}
