namespace Tonarink.Pages.Settings;

sealed record SettingsPageProps(
    AppSettings Settings,
    AppRuntimeState Runtime,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    Action<Func<AppSettings, AppSettings>> ApplyAndRestartServer,
    Action StopServer);
