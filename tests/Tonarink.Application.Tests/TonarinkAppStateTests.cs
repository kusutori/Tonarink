using System.Net;
using System.Text.Json;
using Tonarink.Application;

namespace Tonarink.Application.Tests;

public sealed class TonarinkAppStateTests
{
    [Fact]
    public async Task InitializeRestoresAndNormalizesStoredSettings()
    {
        var platform = new TestPlatform
        {
            Stored = new TonarinkSettings("  Phone  ", string.Empty, TonarinkTheme.Dark, TonarinkLanguage.English, true, "  2468  ", 0),
        };
        var state = new TonarinkAppState(platform);

        await state.InitializeAsync();

        Assert.Equal("Phone", state.Settings.Alias);
        Assert.Equal(platform.DownloadDirectory, state.Settings.DownloadDirectory);
        Assert.Equal(TonarinkTheme.Dark, state.Settings.Theme);
        Assert.Equal(TonarinkLanguage.English, state.Settings.Language);
        Assert.True(state.Settings.AutoAccept);
        Assert.Equal("2468", state.Settings.ReceivePin);
        Assert.Equal("2468", state.Settings.ResolvedReceivePin);
        Assert.Equal(DeviceAddress.DefaultPort, state.Settings.Port);
    }

    [Fact]
    public async Task UpdatePersistsNormalizedSettingsAndRaisesChange()
    {
        var platform = new TestPlatform();
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

        Assert.Equal("Tablet", state.Settings.Alias);
        Assert.Equal("1234", state.Settings.ReceivePin);
        Assert.True(state.Settings.AutoAccept);
        Assert.Equal(state.Settings, platform.Stored);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task MissingReceivePinJsonDeserializesAsDisabled()
    {
        const string json = """{"Alias":"Phone","DownloadDirectory":"downloads","Theme":1,"Language":2,"AutoAccept":true}""";
        var settings = JsonSerializer.Deserialize(json, TonarinkJsonContext.Default.TonarinkSettings);
        Assert.NotNull(settings);
        Assert.Equal("Phone", settings.Alias);
        Assert.True(settings.AutoAccept);
        Assert.Null(settings.ReceivePin);
        Assert.Null(settings.ResolvedReceivePin);

        var platform = new TestPlatform { Stored = settings };
        var state = new TonarinkAppState(platform);
        await state.InitializeAsync();
        Assert.Null(state.Settings.ReceivePin);
        Assert.Equal(DeviceAddress.DefaultPort, state.Settings.Port);
    }

    [Fact]
    public async Task RemovingImportedSendItemDeletesTheTempFile()
    {
        var root = CreateTempDirectory();
        try
        {
            var platform = new TestPlatform(root);
            var state = new TonarinkAppState(platform);
            await using var source = new MemoryStream("imported-bytes"u8.ToArray());
            var item = await platform.ImportSharedFileAsync("note.txt", source, "text/plain");
            Assert.True(File.Exists(item.NativePath));

            state.AddSendItems([item]);
            Assert.True(File.Exists(item.NativePath));

            state.RemoveSendItem(item.Id);
            Assert.False(File.Exists(item.NativePath));
            Assert.Empty(state.SendItems);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ClearingSendQueueReleasesImportedFilesAndCleanupKeepsHeldItems()
    {
        var root = CreateTempDirectory();
        try
        {
            var platform = new TestPlatform(root);
            var state = new TonarinkAppState(platform);
            await using var firstSource = new MemoryStream("one"u8.ToArray());
            await using var secondSource = new MemoryStream("two"u8.ToArray());
            var first = await platform.ImportSharedFileAsync("one.txt", firstSource, "text/plain");
            var second = await platform.ImportSharedFileAsync("two.txt", secondSource, "text/plain");
            state.AddSendItems([first, second]);

            state.RemoveSendItem(first.Id);
            Assert.False(File.Exists(first.NativePath));
            Assert.True(File.Exists(second.NativePath));

            platform.ReleaseUnreferencedShareItems(state.SendItems);
            Assert.True(File.Exists(second.NativePath));

            state.ClearSendItems();
            Assert.False(File.Exists(second.NativePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
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
            Assert.Equal(2, items.Count);
            Assert.Contains(items, item => item.Name == "Album/a.txt");
            Assert.Contains(items, item => item.Name == "Album/nested/b.txt");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeviceAddressParsesIpAndPort()
    {
        Assert.True(DeviceAddress.TryParse("127.0.0.1", out var loopback, out var defaultPort));
        Assert.Equal(IPAddress.Loopback, loopback);
        Assert.Equal(DeviceAddress.DefaultPort, defaultPort);

        Assert.True(DeviceAddress.TryParse("192.168.1.8:12345", out var address, out var port));
        Assert.Equal(IPAddress.Parse("192.168.1.8"), address);
        Assert.Equal(12345, port);

        Assert.False(DeviceAddress.TryParse("not-an-address", out _, out _));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Tonarink-AppTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestPlatform : IPlatformServices
    {
        private readonly ImportedShareStore _imports;

        public TestPlatform(string? root = null)
        {
            var directory = root ?? Path.Combine(Path.GetTempPath(), "Tonarink-AppTests-platform-" + Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(directory, "data");
            DownloadDirectory = Path.Combine(directory, "downloads");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(DownloadDirectory);
            _imports = new ImportedShareStore(Path.Combine(directory, "shared"));
        }

        public TonarinkSettings? Stored { get; set; }
        public PlatformCapabilities Capabilities => PlatformCapabilities.Browser;
        public string DataDirectory { get; }
        public string DownloadDirectory { get; }
        public string DefaultAlias => "Test device";
        public string DeviceModel => "Test model";
        public TonarinkDeviceKind DeviceKind => TonarinkDeviceKind.Desktop;
        public Task<TonarinkSettings?> LoadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored);
        public Task SaveSettingsAsync(TonarinkSettings settings, CancellationToken cancellationToken = default)
        {
            Stored = settings;
            return Task.CompletedTask;
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
    }
}
