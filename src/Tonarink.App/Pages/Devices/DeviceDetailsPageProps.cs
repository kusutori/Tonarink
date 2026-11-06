using LocalSendDotNet;

namespace Tonarink.Pages.Devices;

sealed record DeviceDetailsPageProps(
    AppRuntimeState Runtime,
    LocalSendDevice Device);
