using System.Text.Json;
using Tonarink.Services.Settings;

namespace Tonarink.App.Settings.Tests;

public sealed class FilePreviewSettingsTests
{
    private static AppSettings Empty => AppSettings.Default with { PreviewProviders = Array.Empty<FilePreviewProvider>() };

    [Fact]
    public void AddingSameToolTwiceKeepsOneCardAndOriginalPath()
    {
        var added = FilePreviewSettings.SetExecutablePath(
            FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook),
            FilePreviewProvider.QuickLook, "QuickLook.exe");
        var duplicate = FilePreviewSettings.Add(added, FilePreviewProvider.QuickLook);

        Assert.Same(added, duplicate);
        Assert.Single(FilePreviewSettings.Providers(duplicate));
        Assert.Equal("QuickLook.exe", duplicate.QuickLookExecutablePath);
    }

    [Fact]
    public void EmptyPathCardSurvivesSerializationAndRestart()
    {
        var added = FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook);
        var restored = RoundTrip(added);

        Assert.Single(FilePreviewSettings.Providers(restored));
        Assert.Contains(FilePreviewProvider.QuickLook, FilePreviewSettings.Providers(restored));
        Assert.Equal("", restored.QuickLookExecutablePath);
    }

    [Fact]
    public void DeletedToolsAreNotAutomaticallyReAddedAfterRestart()
    {
        var added = FilePreviewSettings.Add(Empty, FilePreviewProvider.PowerToysPeek);
        var deleted = FilePreviewSettings.Remove(added, FilePreviewProvider.PowerToysPeek);
        var restored = RoundTrip(deleted);
        var migrated = FilePreviewSettings.MigrateDetectedTools(restored, "installed-peek.exe", "installed-quicklook.exe");

        Assert.Same(restored, migrated);
        Assert.Empty(FilePreviewSettings.Providers(migrated));
        Assert.Equal("", migrated.PowerToysPeekExecutablePath);
        Assert.Equal("", migrated.QuickLookExecutablePath);
    }

    [Fact]
    public void RemovingActiveToolSelectsRemainingToolWithoutChangingOtherSettings()
    {
        var both = FilePreviewSettings.Add(
            FilePreviewSettings.Add(Empty, FilePreviewProvider.PowerToysPeek),
            FilePreviewProvider.QuickLook) with
        {
            PreviewProvider = FilePreviewProvider.QuickLook, NotificationsEnabled = false,
        };
        both = FilePreviewSettings.SetExecutablePath(both, FilePreviewProvider.PowerToysPeek, "peek.exe");
        both = FilePreviewSettings.SetExecutablePath(both, FilePreviewProvider.QuickLook, "quicklook.exe");
        var removed = FilePreviewSettings.Remove(both, FilePreviewProvider.QuickLook);

        Assert.Equal(FilePreviewProvider.PowerToysPeek, removed.PreviewProvider);
        Assert.Equal("peek.exe", removed.PowerToysPeekExecutablePath);
        Assert.Equal("", removed.QuickLookExecutablePath);
        Assert.False(removed.NotificationsEnabled);
    }

    [Fact]
    public void StaleSaveCannotRestoreDeletedPath()
    {
        var added = FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook);
        var removed = FilePreviewSettings.Remove(added, FilePreviewProvider.QuickLook);
        var lateSave = FilePreviewSettings.SetExecutablePath(removed, FilePreviewProvider.QuickLook, "old.exe");

        Assert.Same(removed, lateSave);
        Assert.Empty(FilePreviewSettings.Providers(lateSave));
    }

    [Fact]
    public void SavingPathUsesCardTypeNotFileNameAndPreservesSelectedTool()
    {
        var added = FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook);
        var saved = FilePreviewSettings.SetExecutablePath(added, FilePreviewProvider.QuickLook, " \"custom-name.exe\" ");

        Assert.Equal("custom-name.exe", saved.QuickLookExecutablePath);
        Assert.Equal(added.PreviewProvider, saved.PreviewProvider);
        Assert.Equal("", saved.PowerToysPeekExecutablePath);
    }

    [Fact]
    public void ValidationOnlyChecksFileExistenceAndDoesNotMutateSettings()
    {
        var file = Path.GetTempFileName(); // A .tmp file, deliberately not a named preview executable.
        try
        {
            var added = FilePreviewSettings.SetExecutablePath(
                FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook),
                FilePreviewProvider.QuickLook, file);
            Assert.True(FilePreviewSettings.PathExists($" \"{file}\" "));
            Assert.False(FilePreviewSettings.PathExists(file + ".missing"));
            Assert.False(FilePreviewSettings.PathExists(Path.GetDirectoryName(file)!));
            Assert.False(FilePreviewSettings.PathExists(""));
            Assert.Equal(file, added.QuickLookExecutablePath);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void LegacyPathsAndSelectedRunningQuickLookBecomeCards()
    {
        var legacy = AppSettings.Default with
        {
            FilePreviewEnabled = true, PreviewProvider = FilePreviewProvider.QuickLook,
            PowerToysPeekExecutablePath = "custom-peek.exe",
        };
        var migrated = FilePreviewSettings.MigrateDetectedTools(legacy, "", "");

        Assert.Equal(2, FilePreviewSettings.Providers(migrated).Count);
        Assert.Equal("custom-peek.exe", migrated.PowerToysPeekExecutablePath);
        Assert.Contains(FilePreviewProvider.QuickLook, FilePreviewSettings.Providers(migrated));
    }

    [Fact]
    public void LegacySettingsWithNoToolsCanStillDiscoverInstalledOnes()
    {
        var restored = new AppSettingsFile().ToSettings();
        Assert.Null(restored.PreviewProviders);
        var migrated = FilePreviewSettings.MigrateDetectedTools(restored, "peek.exe", "");

        Assert.Equal(new[] { FilePreviewProvider.PowerToysPeek }, FilePreviewSettings.Providers(migrated));
        Assert.Equal("", migrated.PowerToysPeekExecutablePath);
    }

    [Fact]
    public void MigrationClearsDetectedPathsButPreservesCustomOverrides()
    {
        var legacy = AppSettings.Default with
        {
            PowerToysPeekExecutablePath = "installed-peek.exe",
            QuickLookExecutablePath = "custom-quicklook.exe",
        };
        var migrated = FilePreviewSettings.MigrateDetectedTools(legacy, "installed-peek.exe", "installed-quicklook.exe");

        Assert.Equal("", migrated.PowerToysPeekExecutablePath);
        Assert.Equal("custom-quicklook.exe", migrated.QuickLookExecutablePath);
        Assert.Equal(2, FilePreviewSettings.Providers(migrated).Count);
    }

    [Fact]
    public void ExplicitOverridesMatchingDetectedPathsArePreserved()
    {
        var settings = FilePreviewSettings.SetExecutablePath(
            FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook),
            FilePreviewProvider.QuickLook, "installed.exe");
        var migrated = FilePreviewSettings.MigrateDetectedTools(settings, "", "installed.exe", clearDetectedPaths: false);

        Assert.Same(settings, migrated);
        Assert.True(AppSettingsFile.FromSettings(settings).FilePreviewPathsAreOverrides);
    }

    [Fact]
    public void BlankOverrideIsValidAndRestoresAutomaticDetection()
    {
        Assert.True(FilePreviewSettings.TryValidateOverride(" \"\" ", out var normalized));
        Assert.Equal("", normalized);
        var detected = false;
        Assert.Equal("detected.exe", FilePreviewSettings.ResolveExecutablePath(normalized, () =>
        {
            detected = true;
            return "detected.exe";
        }));
        Assert.True(detected);
    }

    [Fact]
    public void ValidOverrideIsNormalizedAndTakesPrecedenceOverDetection()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.True(FilePreviewSettings.TryValidateOverride($" \"{file}\" ", out var normalized));
            Assert.Equal(file, normalized);
            Assert.Equal(file, FilePreviewSettings.ResolveExecutablePath(normalized,
                () => throw new InvalidOperationException("An override must not invoke detection.")));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void MissingOverrideCannotBeSavedOrSilentlyReplacedByDetection()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing");
        Assert.False(FilePreviewSettings.TryValidateOverride(missing, out var normalized));
        Assert.Equal(missing, normalized);
        Assert.Null(FilePreviewSettings.ResolveExecutablePath(missing,
            () => throw new InvalidOperationException("An override must not invoke detection.")));
    }

    [Fact]
    public void StoredProviderListFiltersUnknownTypesAndDuplicates()
    {
        var restored = new AppSettingsFile
        {
            FilePreviewProviders = ["QuickLook", "quicklook", "Unknown", "99", "PowerToysPeek"],
        }.ToSettings();

        Assert.Equal(new[] { FilePreviewProvider.QuickLook, FilePreviewProvider.PowerToysPeek },
            FilePreviewSettings.Providers(restored));
    }

    private static AppSettings RoundTrip(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(AppSettingsFile.FromSettings(settings), AppSettingsJsonContext.Default.AppSettingsFile);
        return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettingsFile)!.ToSettings();
    }
}
