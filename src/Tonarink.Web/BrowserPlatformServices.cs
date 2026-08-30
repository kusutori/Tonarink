using Tonarink.Application;
using System.Text.Json;

namespace Tonarink.Web;

internal sealed class BrowserPlatformServices : IPlatformServices
{
    public PlatformCapabilities Capabilities { get; } = new(
        "Web Host", false, true, true, true, true, true,
        ["浏览器页面控制 Web 服务器所在设备上的 LocalSend 节点，而不是浏览器沙箱本身。"]);
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
        Task.FromException<IReadOnlyList<ShareItem>>(new PlatformNotSupportedException("Web 文件选择由浏览器 InputFile 组件提供。"));

    public ValueTask<Stream> OpenReadAsync(ShareItem item, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<Stream>(new PlatformNotSupportedException("浏览器文件流必须在当前浏览器会话中读取。"));

    public Task<string?> ReadClipboardTextAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<string?>(new PlatformNotSupportedException("Web 剪贴板需要通过浏览器 JavaScript 互操作访问。"));

    public Task WriteClipboardTextAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromException(new PlatformNotSupportedException("Web 剪贴板需要通过浏览器 JavaScript 互操作访问。"));

    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PublishReceivedFileAsync(string path, string contentType, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
