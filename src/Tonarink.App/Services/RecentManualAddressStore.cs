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
        catch
        {
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
        catch
        {
            // A recent-address shortcut must never turn a successful transfer into a failure.
        }
    }
}
