namespace Tonarink.Pages.Receive;

sealed record ReceivePageProps(
    AppRuntimeState Runtime,
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings);
