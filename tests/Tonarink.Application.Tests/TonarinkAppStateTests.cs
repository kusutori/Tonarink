using System.Net;
using System.Text.Json;
using Tonarink.Application;

namespace Tonarink.Application.Tests;

[TestClass]
public sealed class TonarinkAppStateTests
{
    [TestMethod]
    public async Task InitializeRestoresAndNormalizesStoredSettings()
    {
        using var platform = new TestPlatform
        {
            Stored = new TonarinkSettings("  Phone  ", string.Empty, TonarinkTheme.Dark, TonarinkLanguage.English, true, "  2468  ", 0),
        };
        var state = new TonarinkAppState(platform);

        await state.InitializeAsync();

        Assert.AreEqual("Phone", state.Settings.Alias);
        Assert.AreEqual(platform.DownloadDirectory, state.Settings.DownloadDirectory);
        Assert.AreEqual(TonarinkTheme.Dark, state.Settings.Theme);
        Assert.AreEqual(TonarinkLanguage.English, state.Settings.Language);
        Assert.IsTrue(state.Settings.AutoAccept);
        Assert.AreEqual("2468", state.Settings.ReceivePin);
        Assert.AreEqual("2468", state.Settings.ResolvedReceivePin);
        Assert.AreEqual(DeviceAddress.DefaultPort, state.Settings.Port);
    }

    [TestMethod]
    public async Task UpdatePersistsNormalizedSettingsAndRaisesChange()
    {
        using var platform = new TestPlatform();
        var state = new TonarinkAppState(platform);
        var changes = 0;
        state.Changed += () => changes++;

        await state.UpdateSettingsAsync(settings => settings with
        {
            Alias = "  Tablet  ",
            Language = TonarinkLanguage.English,
            ReceivePin = "  1234  ",
            AutoAccept = true,
        });

        Assert.AreEqual("Tablet", state.Settings.Alias);
        Assert.AreEqual("1234", state.Settings.ReceivePin);
        Assert.IsTrue(state.Settings.AutoAccept);
        Assert.AreEqual(state.Settings, platform.Stored);
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ConcurrentInitializationLoadsOnceAndPublishesOneChange()
    {
        using var platform = new TestPlatform { Stored = TonarinkSettings.CreateDefault("downloads", "Restored") };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        platform.LoadAction = async cancellationToken =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return platform.Stored;
        };
        var state = new TonarinkAppState(platform);
        var changes = 0;
        state.Changed += () => Interlocked.Increment(ref changes);

        var first = state.InitializeAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = state.InitializeAsync();
        try
        {
            Assert.AreEqual(1, platform.LoadCalls);
            Assert.IsFalse(second.IsCompleted);
        }
        finally { release.TrySetResult(); }

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
        await state.InitializeAsync();
        Assert.AreEqual(1, platform.LoadCalls);
        Assert.AreEqual(1, changes);
        Assert.AreEqual("Restored", state.Settings.Alias);
    }

    [TestMethod]
    public async Task LoadFailurePropagatesAndInitializationCanRetry()
    {
        using var platform = new TestPlatform();
        var failure = new IOException("settings read failed");
        platform.LoadAction = _ => Task.FromException<TonarinkSettings?>(failure);
        var state = new TonarinkAppState(platform);
        var changes = 0;
        state.Changed += () => changes++;

        var observed = await Assert.ThrowsAsync<IOException>(() => state.InitializeAsync());
        Assert.AreSame(failure, observed);
        Assert.AreEqual(platform.DefaultAlias, state.Settings.Alias);
        Assert.AreEqual(0, changes);

        platform.LoadAction = null;
        platform.Stored = TonarinkSettings.CreateDefault(platform.DownloadDirectory, "Retry");
        await state.InitializeAsync();
        Assert.AreEqual("Retry", state.Settings.Alias);
        Assert.AreEqual(2, platform.LoadCalls);
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task CancelledQueuedInitializationDoesNotCancelOwnerOrPoisonGate()
    {
        using var platform = new TestPlatform { Stored = TonarinkSettings.CreateDefault("downloads", "Owner") };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        platform.LoadAction = async cancellationToken =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return platform.Stored;
        };
        var state = new TonarinkAppState(platform);
        var owner = state.InitializeAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var cancellation = new CancellationTokenSource();
        var queued = state.InitializeAsync(cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => queued);
            Assert.IsFalse(owner.IsCompleted);
            Assert.AreEqual(1, platform.LoadCalls);
        }
        finally { release.TrySetResult(); }

        await owner.WaitAsync(TimeSpan.FromSeconds(3));
        await state.InitializeAsync();
        Assert.AreEqual(1, platform.LoadCalls);
        Assert.AreEqual("Owner", state.Settings.Alias);
    }

    [TestMethod]
    public async Task SaveFailurePropagatesAndSubsequentSaveCanRecover()
    {
        using var platform = new TestPlatform();
        var failure = new IOException("settings write failed");
        platform.SaveAction = (_, _) => Task.FromException(failure);
        var state = new TonarinkAppState(platform);

        var observed = await Assert.ThrowsAsync<IOException>(() =>
            state.UpdateSettingsAsync(settings => settings with { Alias = "Unsaved" }));
        Assert.AreSame(failure, observed);
        Assert.IsNull(platform.Stored);
        // The shared state is optimistic; failure must reach the caller rather
        // than pretending the in-memory value has been persisted.
        Assert.AreEqual("Unsaved", state.Settings.Alias);

        platform.SaveAction = null;
        await state.UpdateSettingsAsync(settings => settings with { Alias = "Saved" });
        Assert.AreEqual(2, platform.SaveCalls);
        Assert.AreEqual("Saved", platform.Stored!.Alias);
        Assert.AreEqual(state.Settings, platform.Stored);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ConcurrentSavesSerializeSnapshotsAndPersistLatestMergedSettings()
    {
        using var platform = new TestPlatform();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new List<TonarinkSettings>();
        platform.SaveAction = async (settings, cancellationToken) =>
        {
            saved.Add(settings);
            if (saved.Count == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        };
        var state = new TonarinkAppState(platform);
        var first = state.UpdateSettingsAsync(settings => settings with { Alias = "Latest name" });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = state.UpdateSettingsAsync(settings => settings with { AutoAccept = true });
        try
        {
            Assert.AreEqual(1, platform.SaveCalls);
            Assert.IsFalse(second.IsCompleted);
            Assert.AreEqual("Latest name", state.Settings.Alias);
            Assert.IsTrue(state.Settings.AutoAccept);
        }
        finally { release.TrySetResult(); }

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(2, saved.Count);
        Assert.IsFalse(saved[0].AutoAccept);
        Assert.IsTrue(saved[1].AutoAccept);
        Assert.AreEqual("Latest name", saved[1].Alias);
        Assert.AreEqual(state.Settings, platform.Stored);
    }

    [TestMethod]
    public async Task MissingReceivePinJsonDeserializesAsDisabled()
    {
        const string json = """{"Alias":"Phone","DownloadDirectory":"downloads","Theme":1,"Language":2,"AutoAccept":true}""";
        var settings = JsonSerializer.Deserialize(json, TonarinkJsonContext.Default.TonarinkSettings);
        Assert.IsNotNull(settings);
        Assert.AreEqual("Phone", settings.Alias);
        Assert.IsTrue(settings.AutoAccept);
        Assert.IsNull(settings.ReceivePin);
        Assert.IsNull(settings.ResolvedReceivePin);

        using var platform = new TestPlatform { Stored = settings };
        var state = new TonarinkAppState(platform);
        await state.InitializeAsync();
        Assert.IsNull(state.Settings.ReceivePin);
        Assert.AreEqual(DeviceAddress.DefaultPort, state.Settings.Port);
    }

    [TestMethod]
    public async Task RemovingImportedSendItemDeletesTheTempFile()
    {
        var root = CreateTempDirectory();
        try
        {
            using var platform = new TestPlatform(root);
            var state = new TonarinkAppState(platform);
            await using var source = new MemoryStream("imported-bytes"u8.ToArray());
            var item = await platform.ImportSharedFileAsync("note.txt", source, "text/plain");
            Assert.IsTrue(File.Exists(item.NativePath));

            state.AddSendItems([item]);
            Assert.IsTrue(File.Exists(item.NativePath));

            state.RemoveSendItem(item.Id);
            Assert.IsFalse(File.Exists(item.NativePath));
            Assert.IsEmpty(state.SendItems);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ClearingSendQueueReleasesImportedFilesAndCleanupKeepsHeldItems()
    {
        var root = CreateTempDirectory();
        try
        {
            using var platform = new TestPlatform(root);
            var state = new TonarinkAppState(platform);
            await using var firstSource = new MemoryStream("one"u8.ToArray());
            await using var secondSource = new MemoryStream("two"u8.ToArray());
            var first = await platform.ImportSharedFileAsync("one.txt", firstSource, "text/plain");
            var second = await platform.ImportSharedFileAsync("two.txt", secondSource, "text/plain");
            state.AddSendItems([first, second]);

            state.RemoveSendItem(first.Id);
            Assert.IsFalse(File.Exists(first.NativePath));
            Assert.IsTrue(File.Exists(second.NativePath));

            platform.ReleaseUnreferencedShareItems(state.SendItems);
            Assert.IsTrue(File.Exists(second.NativePath));

            state.ClearSendItems();
            Assert.IsFalse(File.Exists(second.NativePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void FolderItemsUseRelativeProtocolNames()
    {
        var root = CreateTempDirectory();
        try
        {
            var folder = Path.Combine(root, "Album");
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllText(Path.Combine(folder, "a.txt"), "a");
            File.WriteAllText(Path.Combine(folder, "nested", "b.txt"), "b");

            var items = ShareFolderItems.FromDirectory(folder);
            Assert.AreEqual(2, items.Count);
            Assert.Contains(item => item.Name == "Album/a.txt", items);
            Assert.Contains(item => item.Name == "Album/nested/b.txt", items);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DeviceAddressParsesIpAndPort()
    {
        Assert.IsTrue(DeviceAddress.TryParse("127.0.0.1", out var loopback, out var defaultPort));
        Assert.AreEqual(IPAddress.Loopback, loopback);
        Assert.AreEqual(DeviceAddress.DefaultPort, defaultPort);

        Assert.IsTrue(DeviceAddress.TryParse("192.168.1.8:12345", out var address, out var port));
        Assert.AreEqual(IPAddress.Parse("192.168.1.8"), address);
        Assert.AreEqual(12345, port);

        Assert.IsFalse(DeviceAddress.TryParse("not-an-address", out _, out _));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Tonarink-AppTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestPlatform : IPlatformServices, IDisposable
    {
        private readonly ImportedShareStore _imports;
        private readonly string? _ownedRoot;

        public TestPlatform(string? root = null)
        {
            var directory = root ?? Path.Combine(Path.GetTempPath(), "Tonarink-AppTests-platform-" + Guid.NewGuid().ToString("N"));
            _ownedRoot = root is null ? directory : null;
            DataDirectory = Path.Combine(directory, "data");
            DownloadDirectory = Path.Combine(directory, "downloads");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(DownloadDirectory);
            _imports = new ImportedShareStore(Path.Combine(directory, "shared"));
        }

        public TonarinkSettings? Stored { get; set; }
        public Func<CancellationToken, Task<TonarinkSettings?>>? LoadAction { get; set; }
        public Func<TonarinkSettings, CancellationToken, Task>? SaveAction { get; set; }
        public int LoadCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public PlatformCapabilities Capabilities => PlatformCapabilities.Browser;
        public string DataDirectory { get; }
        public string DownloadDirectory { get; }
        public string DefaultAlias => "Test device";
        public string DeviceModel => "Test model";
        public TonarinkDeviceKind DeviceKind => TonarinkDeviceKind.Desktop;
        public Task<TonarinkSettings?> LoadSettingsAsync(CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            return LoadAction?.Invoke(cancellationToken) ?? Task.FromResult(Stored);
        }
        public async Task SaveSettingsAsync(TonarinkSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (SaveAction is not null)
                await SaveAction(settings, cancellationToken);
            Stored = settings;
        }
        public Task<IReadOnlyList<ShareItem>> PickFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ShareItem>>([]);
        public Task<IReadOnlyList<ShareItem>> PickFoldersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ShareItem>>([]);
        public Task<ShareItem> ImportSharedFileAsync(string fileName, Stream source, string contentType, CancellationToken cancellationToken = default) =>
            _imports.ImportAsync(fileName, source, contentType, cancellationToken);
        public void ReleaseShareItem(ShareItem item) => _imports.Release(item);
        public void ReleaseUnreferencedShareItems(IReadOnlyList<ShareItem> stillHeld) => _imports.CleanupUnreferenced(stillHeld);
        public ValueTask<Stream> OpenReadAsync(ShareItem item, CancellationToken cancellationToken = default) =>
            item.OpenRead?.Invoke(cancellationToken) ?? ValueTask.FromResult<Stream>(Stream.Null);
        public Task<string?> ReadClipboardTextAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task WriteClipboardTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishReceivedFileAsync(string path, string contentType, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
            if (_ownedRoot is not null)
                Directory.Delete(_ownedRoot, recursive: true);
        }
    }
}
