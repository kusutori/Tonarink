namespace Tonarink.Services.Settings;

// Coordinates settings changes with OS integration; the store only persists data.
static class AppSettingsService
{
    private static readonly Lock Gate = new();

    public static void Save(AppSettings settings)
    {
        lock (Gate)
        {
            var previous = AppSettingsStore.Load();
            var updateSendTo = previous.ShowInSendToMenu != settings.ShowInSendToMenu;
            if (updateSendTo)
                WindowsSendTo.SetEnabled(settings.ShowInSendToMenu);

            try
            {
                AppSettingsStore.Save(settings);
            }
            catch
            {
                if (!updateSendTo) throw;
                try { WindowsSendTo.SetEnabled(previous.ShowInSendToMenu); }
                catch (Exception exception)
                {
                    AppDiagnostics.Report("Could not restore the Send To shortcut after settings save failed",
                        exception);
                }

                throw;
            }
        }
    }

    // Executable paths can change between launches, even when the preference did not.
    public static void SynchronizeSendTo()
    {
        lock (Gate)
        {
            if (!AppPlatform.HasPackageIdentity())
                WindowsSendTo.SetEnabled(AppSettingsStore.Load().ShowInSendToMenu);
        }
    }
}
