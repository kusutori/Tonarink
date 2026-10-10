namespace Tonarink.TestSupport;

public sealed class TemporaryDirectory : IDisposable, IAsyncDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Tonarink.Tests", Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }

    public async ValueTask DisposeAsync()
    {
        // A Windows child can release its ownership lock before the OS releases
        // its current-directory handle. Wait for that observable lease release,
        // not a fixed sleep or a retry of the business operation.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Directory.Exists(Path))
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }
        }
    }
}
