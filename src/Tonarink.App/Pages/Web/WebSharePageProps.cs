using LocalSendDotNet;

namespace Tonarink.Pages.Web;

sealed record WebSharePageProps(
    LocalSendNode? Node,
    AppRuntimeState Runtime,
    AppSettings Settings,
    Action<bool?> SetHttpsOverride,
    WebShareMode Mode);
