using Tonarink.Models;
using Tonarink.Services.Platform;

namespace Tonarink.Services.Settings;

static class FilePreviewSettings
{
    public static IReadOnlyList<FilePreviewProvider> Providers(AppSettings settings) =>
        settings.PreviewProviders ?? InferProviders(settings);

    public static AppSettings MigrateDetectedTools(
        AppSettings settings, string peekPath, string quickLookPath, bool clearDetectedPaths = true)
    {
        // Cards configure overrides only; selecting a tool never needs a card.
        // Older versions persisted detected paths; clear those while preserving custom paths.
        var providers = settings.PreviewProviders ?? (FilePreviewProvider[])
        [
            .. Enum.GetValues<FilePreviewProvider>().Where(provider =>
                !string.IsNullOrWhiteSpace(ExecutablePath(settings, provider))
                && (!clearDetectedPaths || !SamePath(ExecutablePath(settings, provider),
                    provider == FilePreviewProvider.QuickLook ? quickLookPath : peekPath))),
        ];
        var peekOverride = clearDetectedPaths && SamePath(settings.PowerToysPeekExecutablePath, peekPath)
            ? "" : settings.PowerToysPeekExecutablePath;
        var quickLookOverride = clearDetectedPaths && SamePath(settings.QuickLookExecutablePath, quickLookPath)
            ? "" : settings.QuickLookExecutablePath;
        if (ReferenceEquals(providers, settings.PreviewProviders)
            && peekOverride == settings.PowerToysPeekExecutablePath
            && quickLookOverride == settings.QuickLookExecutablePath)
            return settings;

        return settings with
        {
            PreviewProviders = providers,
            PowerToysPeekExecutablePath = peekOverride,
            QuickLookExecutablePath = quickLookOverride,
        };
    }

    public static AppSettings Add(AppSettings settings, FilePreviewProvider provider)
    {
        var providers = Providers(settings);
        if (!Enum.IsDefined(provider) || providers.Contains(provider))
            return settings;

        return settings with { PreviewProviders = (FilePreviewProvider[])[.. providers, provider] };
    }

    public static AppSettings Remove(AppSettings settings, FilePreviewProvider provider)
    {
        if (!Providers(settings).Contains(provider))
            return settings;

        FilePreviewProvider[] remaining = [.. Providers(settings).Where(item => item != provider)];
        var next = settings with
        {
            PreviewProviders = remaining,
        };
        return provider switch
        {
            FilePreviewProvider.QuickLook => next with { QuickLookExecutablePath = "" },
            _ => next with { PowerToysPeekExecutablePath = "" },
        };
    }

    public static string ExecutablePath(AppSettings settings, FilePreviewProvider provider) => provider switch
    {
        FilePreviewProvider.QuickLook => settings.QuickLookExecutablePath,
        _ => settings.PowerToysPeekExecutablePath,
    };

    public static AppSettings SetExecutablePath(AppSettings settings, FilePreviewProvider provider, string path)
    {
        // A stale callback must not restore a deleted card.
        if (!Providers(settings).Contains(provider))
            return settings;

        var normalized = NormalizePath(path);
        return provider switch
        {
            FilePreviewProvider.QuickLook => settings with { QuickLookExecutablePath = normalized },
            _ => settings with { PowerToysPeekExecutablePath = normalized },
        };
    }

    public static string NormalizePath(string path) => path.Trim().Trim('"').Trim();

    public static bool PathExists(string path) =>
        File.Exists(Environment.ExpandEnvironmentVariables(NormalizePath(path)));

    public static bool TryValidateOverride(string path, out string normalized)
    {
        normalized = NormalizePath(path);
        // Blank explicitly restores automatic discovery and needs no file check.
        return normalized.Length == 0 || PathExists(normalized);
    }

    public static string? ResolveExecutablePath(string? overridePath, Func<string?> detectInstalled)
    {
        if (string.IsNullOrWhiteSpace(overridePath))
            return detectInstalled();

        var expanded = Environment.ExpandEnvironmentVariables(NormalizePath(overridePath));
        // A configured override takes precedence, even if it later disappears.
        return File.Exists(expanded) ? expanded : null;
    }

    private static bool SamePath(string path, string detected) =>
        !string.IsNullOrWhiteSpace(detected)
        && string.Equals(Environment.ExpandEnvironmentVariables(NormalizePath(path)),
            NormalizePath(detected), StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<FilePreviewProvider>? ParseProviders(string[]? values) => values is null
        ? null
        : values.Select(value => Enum.TryParse<FilePreviewProvider>(value, true, out var provider)
                                 && Enum.IsDefined(provider) ? provider : (FilePreviewProvider)(-1))
            .Where(Enum.IsDefined)
            .Distinct()
            .ToArray();

    private static FilePreviewProvider[] InferProviders(AppSettings settings) =>
    [
        .. Enum.GetValues<FilePreviewProvider>().Where(provider =>
            !string.IsNullOrWhiteSpace(ExecutablePath(settings, provider))),
    ];
}
