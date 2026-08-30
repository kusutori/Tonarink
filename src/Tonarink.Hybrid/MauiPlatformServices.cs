using System.Collections.Concurrent;
using System.Text.Json;
using Tonarink.Application;

namespace Tonarink.Hybrid;

internal sealed class MauiPlatformServices : IPlatformServices
{
    private readonly ConcurrentDictionary<Guid, FileResult> _files = new();

    public PlatformCapabilities Capabilities { get; } = CreateCapabilities();

    public string DataDirectory => FileSystem.Current.AppDataDirectory;
    public string DownloadDirectory => GetDownloadDirectory();
    public string DefaultAlias => DeviceInfo.Current.Name;
    public string DeviceModel => DeviceInfo.Current.Model;
    public TonarinkDeviceKind DeviceKind => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
        ? TonarinkDeviceKind.Mobile
        : TonarinkDeviceKind.Desktop;

    public Task<TonarinkSettings?> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = Preferences.Default.Get("tonarink.settings", string.Empty);
        return Task.FromResult(string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize(json, TonarinkJsonContext.Default.TonarinkSettings));
    }

    public Task SaveSettingsAsync(TonarinkSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Preferences.Default.Set("tonarink.settings", JsonSerializer.Serialize(settings, TonarinkJsonContext.Default.TonarinkSettings));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ShareItem>> PickFilesAsync(CancellationToken cancellationToken = default)
    {
        var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "选择要发送的文件",
        }).WaitAsync(cancellationToken);
        if (results is null)
            return [];

        var items = new List<ShareItem>();
        foreach (var result in results)
        {
            if (result is null)
                continue;
            cancellationToken.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            _files[id] = result;
            await using var stream = await result.OpenReadAsync().WaitAsync(cancellationToken);
            items.Add(new ShareItem(id, result.FileName, stream.CanSeek ? stream.Length : 0,
                result.ContentType ?? "application/octet-stream", result.FullPath));
        }
        return items;
    }

    public async ValueTask<Stream> OpenReadAsync(ShareItem item, CancellationToken cancellationToken = default)
    {
        if (!_files.TryGetValue(item.Id, out var file))
            throw new FileNotFoundException("所选文件已不在当前会话中。", item.Name);
        return await file.OpenReadAsync().WaitAsync(cancellationToken);
    }

    public Task<string?> ReadClipboardTextAsync(CancellationToken cancellationToken = default) =>
        Clipboard.Default.GetTextAsync().WaitAsync(cancellationToken);

    public Task WriteClipboardTextAsync(string text, CancellationToken cancellationToken = default) =>
        Clipboard.Default.SetTextAsync(text).WaitAsync(cancellationToken);

    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default) =>
        MauiNotificationService.NotifyAsync(title, message, cancellationToken);

    public async Task PublishReceivedFileAsync(string path, string contentType, CancellationToken cancellationToken = default)
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
#pragma warning disable CA1422, CS0618
            var downloads = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
                ?? throw new IOException("Android public download directory is unavailable.");
#pragma warning restore CA1422, CS0618
            var directory = Path.Combine(downloads, "Tonarink");
            Directory.CreateDirectory(directory);
            var destinationPath = UniquePath(directory, Path.GetFileName(path));
            await using var source = File.OpenRead(path);
            await using var target = File.Create(destinationPath);
            await source.CopyToAsync(target, cancellationToken);
            return;
        }
        var resolver = Android.App.Application.Context.ContentResolver;
        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, Path.GetFileName(path));
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, contentType);
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, Path.Combine(Android.OS.Environment.DirectoryDownloads ?? "Download", "Tonarink"));
        values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 1);
        var destination = resolver?.Insert(Android.Provider.MediaStore.Downloads.ExternalContentUri, values)
            ?? throw new IOException("Android MediaStore could not create the download entry.");
        try
        {
            await using var source = File.OpenRead(path);
            await using var target = resolver.OpenOutputStream(destination)
                ?? throw new IOException("Android MediaStore could not open the download entry.");
            await source.CopyToAsync(target, cancellationToken);
            values.Clear();
            values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(destination, values, null, null);
        }
        catch
        {
            resolver?.Delete(destination, null, null);
            throw;
        }
#else
        await Task.CompletedTask;
#endif
    }

    private static PlatformCapabilities CreateCapabilities()
    {
        var android = OperatingSystem.IsAndroid();
        var limitations = new List<string>();
        if (OperatingSystem.IsIOS())
            limitations.Add("iOS suspends arbitrary listening sockets after the app enters the background.");
        return new(DeviceInfo.Current.Platform.ToString(), true, true, android || OperatingSystem.IsMacCatalyst(), true, true,
            android || OperatingSystem.IsIOS(), limitations);
    }

    private static string GetDownloadDirectory()
    {
#if IOS || MACCATALYST
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#else
        return Path.Combine(FileSystem.Current.AppDataDirectory, "Downloads");
#endif
    }

    private static string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
            return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 1; ; index++)
        {
            candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }
}
