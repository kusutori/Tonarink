using System.Diagnostics;

namespace Tonarink.CommandLine.Tests;

internal static class ProbeLease
{
    public static async Task<FileStream> WaitForReleaseAsync(string path)
    {
        // WaitForExit observes only the owned controller, not every descendant.
        // Wait for this actual fixture resource, with a hard bound; an orphan
        // keeping it open still fails. Never find or kill a process by PID.
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                Console.WriteLine($"Owned probe lease released after {elapsed.Elapsed.TotalMilliseconds:F0} ms.");
                return stream;
            }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(5) && File.Exists(path))
            {
                await Task.Delay(25);
            }
        }
    }
}
