namespace Tonarink.Services;

static class RecentManualAddressStore
{
    private static readonly string FilePath = Path.Combine(
        AppPlatform.DataDirectory,
        "recent-manual-address.txt");

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var address = File.ReadAllText(FilePath).Trim();
            return address.Length == 0 ? null : address;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Report("Could not load the recent manual address", exception);
            return null;
        }
    }

    public static void Save(string address)
    {
        try
        {
            Directory.CreateDirectory(AppPlatform.DataDirectory);
            File.WriteAllText(FilePath, address);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A recent-address shortcut must never turn a successful transfer into a failure.
            AppDiagnostics.Report("Could not save the recent manual address", exception);
        }
    }
}
