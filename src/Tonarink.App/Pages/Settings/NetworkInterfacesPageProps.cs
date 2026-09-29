namespace Tonarink.Pages.Settings;

sealed record NetworkInterfacesPageProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings);
