namespace Tonarink.Services.Platform;

static class WindowsSendTo
{
    private const string OwnershipMarker = AppIntegrationIds.SendToOwnershipMarker;

    public static void SetEnabled(bool enabled)
    {
        // MSIX needs explicit virtualization exclusions before Explorer can see
        // new AppData files. Do not silently create a package-private shortcut.
        if (AppPlatform.HasPackageIdentity())
            throw new InvalidOperationException("Send To integration is only available in unpackaged builds.");
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        if (string.IsNullOrWhiteSpace(folder))
            throw new InvalidOperationException("The Windows Send To folder is unavailable.");
        var shortcutPath = Path.Combine(folder, AppIntegrationIds.SendToShortcutName);
        if (File.Exists(shortcutPath))
        {
            using var existing = new WindowsShellLink();
            existing.Load(shortcutPath);
            if (existing.Description != OwnershipMarker)
            {
                // A user-created shortcut with the same name is not ours to
                // replace or delete, even when this setting is switched off.
                if (enabled) throw new InvalidOperationException($"{AppIntegrationIds.SendToShortcutName} already exists and is not managed by {AppIdentity.Name}.");
                return;
            }
        }
        if (!enabled)
        {
            if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
            return;
        }

        Directory.CreateDirectory(folder);
        using var link = new WindowsShellLink();
        link.SetTarget(AppPlatform.ExecutablePath);
        // Explorer appends each selected path, quoted as needed. The delimiter
        // also makes names beginning with '-' positional rather than options.
        link.SetArguments("app open --");
        link.SetDescription(OwnershipMarker);
        link.SetWorkingDirectory(Path.GetDirectoryName(AppPlatform.ExecutablePath)!);
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        link.SetIcon(File.Exists(icon) ? icon : AppPlatform.ExecutablePath);
        link.Save(shortcutPath);
    }
}
