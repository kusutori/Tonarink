namespace Tonarink.Application;

public sealed class ImportedShareStore
{
    public ImportedShareStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public async Task<ShareItem> ImportAsync(string fileName, Stream source, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var displayName = fileName.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "shared-file";
        var safeFile = Path.GetFileName(displayName);
        if (string.IsNullOrWhiteSpace(safeFile))
            safeFile = "shared-file";

        var path = Path.Combine(Directory, $"{Guid.NewGuid():N}-{safeFile}");
        await using (var target = File.Create(path))
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);

        var size = new FileInfo(path).Length;
        return new ShareItem(Guid.NewGuid(), displayName, size,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, path,
            OpenRead: _ => ValueTask.FromResult<Stream>(File.OpenRead(path)));
    }

    public bool Owns(ShareItem item) =>
        item.NativePath is { Length: > 0 } path && OwnsPath(path);

    public void Release(ShareItem item)
    {
        if (!Owns(item) || item.NativePath is not { } path)
            return;
        TryDelete(path);
    }

    public void CleanupUnreferenced(IEnumerable<ShareItem> stillHeld)
    {
        ArgumentNullException.ThrowIfNull(stillHeld);
        var keep = stillHeld
            .Select(static item => item.NativePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path!))
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        if (!System.IO.Directory.Exists(Directory))
            return;

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory))
        {
            if (!keep.Contains(Path.GetFullPath(file)))
                TryDelete(file);
        }
    }

    private bool OwnsPath(string path)
    {
        var full = Path.GetFullPath(path);
        var prefix = Directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // The send queue no longer references the file; a still-open reader can retry delete later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
