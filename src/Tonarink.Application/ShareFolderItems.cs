namespace Tonarink.Application;

public static class ShareFolderItems
{
    public static IReadOnlyList<ShareItem> FromDirectory(string folderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var folder = new DirectoryInfo(folderPath);
        if (!folder.Exists)
            throw new DirectoryNotFoundException($"The shared folder is no longer available: {folderPath}");

        return folder.EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(file =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(folder.FullName, file.FullName).Replace('\\', '/');
                var name = $"{folder.Name}/{relative}";
                var path = file.FullName;
                return new ShareItem(Guid.NewGuid(), name, file.Length, GuessContentType(file.Name), path,
                    OpenRead: _ => ValueTask.FromResult<Stream>(File.OpenRead(path)));
            })
            .ToArray();
    }

    public static async Task<IReadOnlyList<ShareItem>> ImportDirectoryAsync(
        string folderPath, ImportedShareStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var items = new List<ShareItem>();
        foreach (var item in FromDirectory(folderPath, cancellationToken))
        {
            await using var source = File.OpenRead(item.NativePath!);
            items.Add(await store.ImportAsync(item.Name, source, item.ContentType, cancellationToken).ConfigureAwait(false));
        }
        return items;
    }

    public static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".txt" => "text/plain",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".json" => "application/json",
        ".pdf" => "application/pdf",
        ".mp4" => "video/mp4",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };
}
