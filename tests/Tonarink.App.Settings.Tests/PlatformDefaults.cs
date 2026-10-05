// The source-linked settings model only needs these platform defaults, not WinUI.
global using Tonarink.Services.Platform;

namespace Tonarink.Services.Platform;

static class AppPlatform
{
    public static string DefaultDownloadDirectory => Path.GetTempPath();
}

enum FilePreviewProvider
{
    PowerToysPeek,
    QuickLook,
}
