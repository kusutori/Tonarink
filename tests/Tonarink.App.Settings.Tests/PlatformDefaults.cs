// The source-linked settings model only needs these platform defaults, not WinUI.
global using Tonarink.Services.Platform;
global using Tonarink.Models;

namespace Tonarink.Services.Platform;

static class AppPlatform
{
    public static string DefaultDownloadDirectory => Path.GetTempPath();
    public static string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), $"tonarink-settings-test-{Guid.NewGuid():N}");
}

enum FilePreviewProvider
{
    PowerToysPeek,
    QuickLook,
}

// The serialization tests do not use discovery or the diagnostic sink.
static class FilePreviewLauncher
{
    public static (string PowerToysPeek, string QuickLook) DetectInstalledExecutables(string? peek, string? quickLook) =>
        (peek ?? "", quickLook ?? "");
}

static class AppDiagnostics
{
    public static void Report(string message, Exception exception) { }
}
