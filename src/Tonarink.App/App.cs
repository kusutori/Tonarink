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
#if DEBUG
        // Reactor's multi-window startup overload doesn't dispatch --devtools.
        // Use its root-host entry only for an explicitly requested debug session.
        if (Environment.GetCommandLineArgs().Contains("--devtools"))
        {
            ReactorApp.Run<AppShell>(AppWindows.MainSpec(startHidden: false), configure: _ =>
            {
                Application.Current.HighContrastAdjustment = ApplicationHighContrastAdjustment.None;
                ReactorApp.ShutdownPolicy = ShutdownPolicy.OnLastSurfaceClosed;
            });
        }
        else
#endif
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
