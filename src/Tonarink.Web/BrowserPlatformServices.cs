using Tonarink.Application;
using System.Text.Json;

namespace Tonarink.Web;

internal sealed class BrowserPlatformServices(IServiceProvider services) : IPlatformServices
{
    public PlatformCapabilities Capabilities => new(
        "Web Host", false, true, true, true, false, true, true,
        [Text("BrowserHostLimits")]);
    public string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tonarink", "Web");
    public string DownloadDirectory => Path.Combine(DataDirectory, "downloads");
    public string DefaultAlias => Environment.MachineName;
    public string DeviceModel => Environment.MachineName;
    public TonarinkDeviceKind DeviceKind => TonarinkDeviceKind.Desktop;

    public async Task<TonarinkSettings?> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(DataDirectory, "settings.json");
        if (!File.Exists(path))
            return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, TonarinkJsonContext.Default.TonarinkSettings, cancellationToken);
    }

    public async Task SaveSettingsAsync(TonarinkSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, "settings.json");
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, settings, TonarinkJsonContext.Default.TonarinkSettings, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public Task<IReadOnlyList<ShareItem>> PickFilesAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<ShareItem>>(new PlatformNotSupportedException(Text("BrowserPickFiles")));

    public Task<IReadOnlyList<ShareItem>> PickFoldersAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<ShareItem>>(new PlatformNotSupportedException(Text("BrowserPickFolders")));

    public Task<ShareItem> ImportSharedFileAsync(string fileName, Stream source, string contentType, CancellationToken cancellationToken = default) =>
        Task.FromException<ShareItem>(new PlatformNotSupportedException(Text("BrowserShareUnavailable")));

    public void ReleaseShareItem(ShareItem item) { }

    public void ReleaseUnreferencedShareItems(IReadOnlyList<ShareItem> stillHeld) { }

    public ValueTask<Stream> OpenReadAsync(ShareItem item, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<Stream>(new PlatformNotSupportedException(Text("BrowserFileExpired")));

    public Task<string?> ReadClipboardTextAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<string?>(new PlatformNotSupportedException(Text("BrowserClipboard")));

    public Task WriteClipboardTextAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromException(new PlatformNotSupportedException(Text("BrowserClipboard")));

    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PublishReceivedFileAsync(string path, string contentType, CancellationToken cancellationToken = default) => Task.CompletedTask;

    private string Text(string key) => AppText.Get(key,
        services.GetRequiredService<TonarinkAppState>().Settings.Language);
}
