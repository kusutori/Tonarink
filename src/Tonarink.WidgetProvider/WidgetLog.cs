using System.Diagnostics;

namespace Tonarink.WidgetProvider;

internal static class WidgetLog
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData",
        "Local",
        "kusutori",
        "Tonarink",
        "widget-provider.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[widget-provider] Could not write log: {exception.Message}");
        }
    }
}
