using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Tonarink.Cli;

args = AppCommandActivation.NormalizeLegacyArguments(args);
if (CliClient.ShouldRun(args))
    return await CliClient.RunAsync(args, new(AppPlatform.DataDirectory, AppPlatform.ExecutablePath, Integrated: true));

AppDiagnostics.Initialize();
var startupSettings = AppSettingsStore.Load();
try
{
    if (await ShareTargetActivationBroker.RedirectToPrimaryInstanceAsync(
            () => AppNotificationService.Initialize(startupSettings.NotificationsEnabled)))
    {
        return 0;
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
            Application.Current.UnhandledException += (_, exception) =>
                AppDiagnostics.Report($"Unhandled XAML exception: {exception.Message}", exception.Exception);
            AppWindows.OpenMain(
                startHidden: AppPlatform.CliBackground || (AppPlatform.StartHidden && startupSettings.MinimizeToTray)
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
return 0;
