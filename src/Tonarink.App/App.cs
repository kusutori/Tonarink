using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;

AppDiagnostics.Initialize();
var startupSettings = AppSettingsStore.Load();
try
{
    if (await ShareTargetActivationBroker.RedirectToPrimaryInstanceAsync(
            () => AppNotificationService.Initialize(startupSettings.NotificationsEnabled)))
    {
        return;
    }

    ToolkitXamlMetadata.Register();
    NativeSystemMenuTheme.Apply(startupSettings.ThemeIndex);
    WidgetAppHost.Start();
    try
    {
        ReactorApp.Run(_ =>
        {
            Application.Current.HighContrastAdjustment = ApplicationHighContrastAdjustment.None;
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
