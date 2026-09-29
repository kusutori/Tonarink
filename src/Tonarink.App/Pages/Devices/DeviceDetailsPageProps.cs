using LocalSendDotNet;
using Microsoft.UI.Xaml;

namespace Tonarink.Pages.Devices;

sealed record DeviceDetailsPageProps(
    AppRuntimeState Runtime,
    LocalSendDevice Device,
    ElementTheme Theme);
