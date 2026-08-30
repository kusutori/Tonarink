using Tonarink.Application;

namespace Tonarink.Application.Tests;

public sealed class TonarinkAppStateTests
{
    [Fact]
    public async Task InitializeRestoresAndNormalizesStoredSettings()
    {
        var platform = new TestPlatform
        {
            Stored = new TonarinkSettings("  Phone  ", string.Empty, TonarinkTheme.Dark, TonarinkLanguage.English, true),
        };
        var state = new TonarinkAppState(platform);

        await state.InitializeAsync();

        Assert.Equal("Phone", state.Settings.Alias);
        Assert.Equal(platform.DownloadDirectory, state.Settings.DownloadDirectory);
        Assert.Equal(TonarinkTheme.Dark, state.Settings.Theme);
        Assert.True(state.Settings.AutoAccept);
    }

    [Fact]
    public async Task UpdatePersistsNormalizedSettingsAndRaisesChange()
    {
        var platform = new TestPlatform();
        var state = new TonarinkAppState(platform);
        var changes = 0;
        state.Changed += () => changes++;

        await state.UpdateSettingsAsync(settings => settings with { Alias = "  Tablet  ", Language = TonarinkLanguage.English });

        Assert.Equal("Tablet", state.Settings.Alias);
        Assert.Equal(state.Settings, platform.Stored);
        Assert.Equal(1, changes);
    }

    private sealed class TestPlatform : IPlatformServices
    {
        public TonarinkSettings? Stored { get; set; }
        public PlatformCapabilities Capabilities => PlatformCapabilities.Browser;
        public string DataDirectory => "data";
        public string DownloadDirectory => "downloads";
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
        public ValueTask<Stream> OpenReadAsync(ShareItem item, CancellationToken cancellationToken = default) => ValueTask.FromResult<Stream>(Stream.Null);
        public Task<string?> ReadClipboardTextAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task WriteClipboardTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishReceivedFileAsync(string path, string contentType, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
