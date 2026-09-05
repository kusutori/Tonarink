using Microsoft.UI.Reactor;

var startupSettings = AppSettingsStore.Load();
try
{
    if (await ShareTargetActivationBroker.RedirectToPrimaryInstanceAsync(
            () => AppNotificationService.Initialize(startupSettings.NotificationsEnabled)))
    {
        return;
    }

    ToolkitXamlMetadata.Register();
    WidgetAppHost.Start();
    try
    {
        ReactorApp.Run(_ =>
        {
            ReactorApp.ShutdownPolicy = ShutdownPolicy.OnLastSurfaceClosed;
            AppWindows.OpenMain(
                startHidden: (AppPlatform.StartHidden && startupSettings.MinimizeToTray)
                    || AppNotificationService.HasPendingBackgroundAction);
        });
    }
    finally
    {
        WidgetAppHost.Stop();
    }
}
finally
{
    AppNotificationService.Shutdown();
}
