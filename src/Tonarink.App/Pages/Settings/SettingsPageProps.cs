namespace Tonarink.Pages.Settings;

sealed record SettingsPageProps(
    AppSettings Settings,
    AppRuntimeState Runtime,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    Action StartOrRestartServer,
    Action StopServer);
