using System.Text.Json;
using Tonarink.Services.Settings;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class FilePreviewSettingsTests
{
    private static AppSettings Empty => AppSettings.Default with { PreviewProviders = Array.Empty<FilePreviewProvider>() };

    [TestMethod]
    public void AddingSameToolTwiceKeepsOneCardAndOriginalPath()
    {
        var added = FilePreviewSettings.SetExecutablePath(
            FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook),
            FilePreviewProvider.QuickLook, "QuickLook.exe");
        var duplicate = FilePreviewSettings.Add(added, FilePreviewProvider.QuickLook);

        Assert.AreSame(added, duplicate);
        Assert.ContainsSingle(FilePreviewSettings.Providers(duplicate));
        Assert.AreEqual("QuickLook.exe", duplicate.QuickLookExecutablePath);
    }

    [TestMethod]
    public void EmptyPathCardSurvivesSerializationAndRestart()
    {
        var added = FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook);
        var restored = RoundTrip(added);

        Assert.ContainsSingle(FilePreviewSettings.Providers(restored));
        Assert.Contains(FilePreviewProvider.QuickLook, FilePreviewSettings.Providers(restored));
        Assert.AreEqual("", restored.QuickLookExecutablePath);
    }

    [TestMethod]
    public void DeletedToolsAreNotAutomaticallyReAddedAfterRestart()
    {
        var added = FilePreviewSettings.Add(Empty, FilePreviewProvider.PowerToysPeek);
        var deleted = FilePreviewSettings.Remove(added, FilePreviewProvider.PowerToysPeek);
        var restored = RoundTrip(deleted);
        var migrated = FilePreviewSettings.MigrateDetectedTools(restored, "installed-peek.exe", "installed-quicklook.exe");

        Assert.AreSame(restored, migrated);
        Assert.IsEmpty(FilePreviewSettings.Providers(migrated));
        Assert.AreEqual("", migrated.PowerToysPeekExecutablePath);
        Assert.AreEqual("", migrated.QuickLookExecutablePath);
    }

    [TestMethod]
    public void RemovingOverrideKeepsSelectedToolAndOtherSettings()
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

        Assert.AreEqual(FilePreviewProvider.QuickLook, removed.PreviewProvider);
        Assert.AreEqual("peek.exe", removed.PowerToysPeekExecutablePath);
        Assert.AreEqual("", removed.QuickLookExecutablePath);
        Assert.IsFalse(removed.NotificationsEnabled);
    }

    [TestMethod]
    public void StaleSaveCannotRestoreDeletedPath()
    {
        var added = FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook);
        var removed = FilePreviewSettings.Remove(added, FilePreviewProvider.QuickLook);
        var lateSave = FilePreviewSettings.SetExecutablePath(removed, FilePreviewProvider.QuickLook, "old.exe");

        Assert.AreSame(removed, lateSave);
        Assert.IsEmpty(FilePreviewSettings.Providers(lateSave));
    }

    [TestMethod]
    public void SavingPathUsesCardTypeNotFileNameAndPreservesSelectedTool()
    {
        var added = FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook);
        var saved = FilePreviewSettings.SetExecutablePath(added, FilePreviewProvider.QuickLook, " \"custom-name.exe\" ");

        Assert.AreEqual("custom-name.exe", saved.QuickLookExecutablePath);
        Assert.AreEqual(added.PreviewProvider, saved.PreviewProvider);
        Assert.AreEqual("", saved.PowerToysPeekExecutablePath);
    }

    [TestMethod]
    public void ValidationOnlyChecksFileExistenceAndDoesNotMutateSettings()
    {
        var file = Path.GetTempFileName(); // A .tmp file, deliberately not a named preview executable.
        try
        {
            var added = FilePreviewSettings.SetExecutablePath(
                FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook),
                FilePreviewProvider.QuickLook, file);
            Assert.IsTrue(FilePreviewSettings.PathExists($" \"{file}\" "));
            Assert.IsFalse(FilePreviewSettings.PathExists(file + ".missing"));
            Assert.IsFalse(FilePreviewSettings.PathExists(Path.GetDirectoryName(file)!));
            Assert.IsFalse(FilePreviewSettings.PathExists(""));
            Assert.AreEqual(file, added.QuickLookExecutablePath);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void MigrationAddsOnlyCustomOverridesNotSelectedTool()
    {
        var legacy = AppSettings.Default with
        {
            FilePreviewEnabled = true, PreviewProvider = FilePreviewProvider.QuickLook,
            PowerToysPeekExecutablePath = "custom-peek.exe",
        };
        var migrated = FilePreviewSettings.MigrateDetectedTools(legacy, "", "");

        Assert.ContainsSingle(FilePreviewSettings.Providers(migrated));
        Assert.AreEqual("custom-peek.exe", migrated.PowerToysPeekExecutablePath);
        Assert.Contains(FilePreviewProvider.PowerToysPeek, FilePreviewSettings.Providers(migrated));
        Assert.DoesNotContain(FilePreviewProvider.QuickLook, FilePreviewSettings.Providers(migrated));
    }

    [TestMethod]
    public void DetectedToolsDoNotCreateUnnecessaryOverrideCards()
    {
        var restored = new AppSettingsFile().ToSettings();
        Assert.IsNull(restored.PreviewProviders);
        var migrated = FilePreviewSettings.MigrateDetectedTools(restored, "peek.exe", "");

        Assert.IsEmpty(FilePreviewSettings.Providers(migrated));
        Assert.AreEqual(restored.PreviewProvider, migrated.PreviewProvider);
        Assert.AreEqual("", migrated.PowerToysPeekExecutablePath);
    }

    [TestMethod]
    public void MigrationClearsDetectedPathsButPreservesCustomOverrides()
    {
        var legacy = AppSettings.Default with
        {
            PowerToysPeekExecutablePath = "installed-peek.exe",
            QuickLookExecutablePath = "custom-quicklook.exe",
        };
        var migrated = FilePreviewSettings.MigrateDetectedTools(legacy, "installed-peek.exe", "installed-quicklook.exe");

        Assert.AreEqual("", migrated.PowerToysPeekExecutablePath);
        Assert.AreEqual("custom-quicklook.exe", migrated.QuickLookExecutablePath);
        Assert.AreSequenceEqual(new[] { FilePreviewProvider.QuickLook }, FilePreviewSettings.Providers(migrated));
    }

    [TestMethod]
    public void ExplicitOverridesMatchingDetectedPathsArePreserved()
    {
        var settings = FilePreviewSettings.SetExecutablePath(
            FilePreviewSettings.Add(Empty, FilePreviewProvider.QuickLook),
            FilePreviewProvider.QuickLook, "installed.exe");
        var migrated = FilePreviewSettings.MigrateDetectedTools(settings, "", "installed.exe", clearDetectedPaths: false);

        Assert.AreSame(settings, migrated);
        Assert.IsTrue(AppSettingsFile.FromSettings(settings).FilePreviewPathsAreOverrides);
    }

    [TestMethod]
    public void BlankOverrideIsValidAndRestoresAutomaticDetection()
    {
        Assert.IsTrue(FilePreviewSettings.TryValidateOverride(" \"\" ", out var normalized));
        Assert.AreEqual("", normalized);
        var detected = false;
        Assert.AreEqual("detected.exe", FilePreviewSettings.ResolveExecutablePath(normalized, () =>
        {
            detected = true;
            return "detected.exe";
        }));
        Assert.IsTrue(detected);
    }

    [TestMethod]
    public void ValidOverrideIsNormalizedAndTakesPrecedenceOverDetection()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.IsTrue(FilePreviewSettings.TryValidateOverride($" \"{file}\" ", out var normalized));
            Assert.AreEqual(file, normalized);
            Assert.AreEqual(file, FilePreviewSettings.ResolveExecutablePath(normalized,
                () => throw new InvalidOperationException("An override must not invoke detection.")));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void MissingOverrideCannotBeSavedOrSilentlyReplacedByDetection()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing");
        Assert.IsFalse(FilePreviewSettings.TryValidateOverride(missing, out var normalized));
        Assert.AreEqual(missing, normalized);
        Assert.IsNull(FilePreviewSettings.ResolveExecutablePath(missing,
            () => throw new InvalidOperationException("An override must not invoke detection.")));
    }

    [TestMethod]
    public void StoredProviderListFiltersUnknownTypesAndDuplicates()
    {
        var restored = new AppSettingsFile
        {
            FilePreviewProviders = ["QuickLook", "quicklook", "Unknown", "99", "PowerToysPeek"],
        }.ToSettings();

        Assert.AreSequenceEqual(new[] { FilePreviewProvider.QuickLook, FilePreviewProvider.PowerToysPeek },
            FilePreviewSettings.Providers(restored));
    }

    private static AppSettings RoundTrip(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(AppSettingsFile.FromSettings(settings), AppSettingsJsonContext.Default.AppSettingsFile);
        return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettingsFile)!.ToSettings();
    }
}
