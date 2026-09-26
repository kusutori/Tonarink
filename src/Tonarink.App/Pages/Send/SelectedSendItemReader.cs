using LocalSendDotNet;
using Microsoft.UI.Reactor.Input;
using Windows.Storage;

namespace Tonarink.Pages.Send;

static class SelectedSendItemReader
{
    public static async Task<IReadOnlyList<SelectedSendItem>> ReadDroppedAsync(
        DragData data,
        CancellationToken cancellationToken = default)
    {
        var storageItems = await data.GetFilesAsync();
        var selected = new List<SelectedSendItem>();
        foreach (var storageItem in storageItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSafeLocalStorageItem(storageItem))
                continue;

            switch (storageItem)
            {
                case StorageFile file:
                    selected.Add(await FromStorageFileAsync(file, file.Name, cancellationToken));
                    break;
                case StorageFolder folder:
                    selected.AddRange(await FromFolderAsync(folder, cancellationToken));
                    break;
            }
        }

        return selected;
    }

    public static async Task<SelectedSendItem> FromStorageFileAsync(
        StorageFile file,
        string protocolName,
        CancellationToken cancellationToken)
    {
        var properties = await file.GetBasicPropertiesAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var length = checked((long)properties.Size);
        var item = new SendStreamItem(
            protocolName.Replace('\\', '/'),
            length,
            async token =>
            {
                token.ThrowIfCancellationRequested();
                return await file.OpenStreamForReadAsync().ConfigureAwait(false);
            });
        return new(Guid.NewGuid(), item, protocolName, length, "file");
    }

    public static Task<IReadOnlyList<SelectedSendItem>> FromFolderAsync(
        StorageFolder folder,
        CancellationToken cancellationToken) => FromFolderPathAsync(folder.Path, cancellationToken);

    public static Task<IReadOnlyList<SelectedSendItem>> FromFolderPathAsync(
        string folderPath,
        CancellationToken cancellationToken) => Task.Run<IReadOnlyList<SelectedSendItem>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new IOException("The selected folder has no accessible local path.");

        var folder = new DirectoryInfo(folderPath);
        if (!folder.Exists)
            throw new DirectoryNotFoundException($"The shared folder is no longer available: {folderPath}");

        return (SelectedSendItem[])
        [
            .. Directory.EnumerateFiles(folder.FullName, "*", SearchOption.AllDirectories)
                .Select(path =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativeName = Path.GetRelativePath(folder.FullName, path).Replace('\\', '/');
                    var protocolName = $"{folder.Name}/{relativeName}";
                    return new SelectedSendItem(
                        Guid.NewGuid(),
                        new SendFileItem(path, protocolName),
                        protocolName,
                        new FileInfo(path).Length,
                        "folder");
                })
        ];
    }, cancellationToken);

    private static bool IsSafeLocalStorageItem(IStorageItem storageItem)
    {
        try
        {
            var path = storageItem.Path;
            if (string.IsNullOrWhiteSpace(path)
                || path.StartsWith(@"\\", StringComparison.Ordinal)
                || !Path.IsPathFullyQualified(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or IOException
                                              or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            AppDiagnostics.Report("Could not inspect a dropped storage item", exception);
            return false;
        }
    }
}
