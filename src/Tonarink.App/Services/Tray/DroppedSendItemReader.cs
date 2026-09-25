using LocalSendDotNet;
using Microsoft.UI.Reactor.Input;
using Windows.Storage;

namespace Tonarink.Services.Tray;

sealed record DroppedSendPayload(
    IReadOnlyList<SendItem> Items,
    long TotalBytes);

/// <summary>Turns files dropped from Windows into repeatable LocalSend items.</summary>
static class DroppedSendItemReader
{
    public static async Task<DroppedSendPayload> ReadAsync(
        DragData data,
        CancellationToken cancellationToken = default)
    {
        var storageItems = await data.GetFilesAsync();
        var items = new List<SendItem>();
        long totalBytes = 0;

        foreach (var storageItem in storageItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSafeLocalStorageItem(storageItem))
                continue;

            switch (storageItem)
            {
                case StorageFile file:
                    {
                        var properties = await file.GetBasicPropertiesAsync()
                            .AsTask(cancellationToken)
                            .ConfigureAwait(false);
                        var length = checked((long)properties.Size);
                        items.Add(new SendStreamItem(
                            file.Name,
                            length,
                            async token =>
                            {
                                token.ThrowIfCancellationRequested();
                                return await file.OpenStreamForReadAsync().ConfigureAwait(false);
                            }));
                        totalBytes = checked(totalBytes + length);
                        break;
                    }

                case StorageFolder folder:
                    foreach (var item in ReadFolder(folder.Path, cancellationToken))
                    {
                        items.Add(item.Item);
                        totalBytes = checked(totalBytes + item.Length);
                    }
                    break;
            }
        }

        return new(items, totalBytes);
    }

    private static IEnumerable<(SendItem Item, long Length)> ReadFolder(
        string folderPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new IOException("The dropped folder has no accessible local path.");

        var folder = new DirectoryInfo(folderPath);
        if (!folder.Exists)
            throw new DirectoryNotFoundException($"The dropped folder is no longer available: {folderPath}");

        foreach (var path in Directory.EnumerateFiles(folder.FullName, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeName = Path.GetRelativePath(folder.FullName, path).Replace('\\', '/');
            var protocolName = $"{folder.Name}/{relativeName}";
            var length = new FileInfo(path).Length;
            yield return (new SendFileItem(path, protocolName), length);
        }
    }

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
