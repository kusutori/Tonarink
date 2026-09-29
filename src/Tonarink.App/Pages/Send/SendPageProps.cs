using LocalSendDotNet;
using Microsoft.UI.Xaml;

namespace Tonarink.Pages.Send;

sealed record SendPageProps(
    AppRuntimeState Runtime,
    LocalSendNode? Node,
    ElementTheme Theme,
    Func<Task> RefreshAsync,
    Action<OutgoingTransferViewState?> SetTransferOverlay,
    ShareTargetPayload? ShareTargetPayload,
    Action<Guid> ConsumeShareTargetPayload,
    IReadOnlyList<SelectedSendItem> SelectedItems,
    Action<Func<IReadOnlyList<SelectedSendItem>, IReadOnlyList<SelectedSendItem>>> UpdateSelectedItems,
    bool KeepItemsForMultipleReceivers,
    Action<bool> SetKeepItemsForMultipleReceivers,
    bool VerifyChecksums,
    bool ExpandDragDropToEntireApp,
    bool FilePreviewEnabled,
    FilePreviewProvider PreviewProvider,
    string PreviewExecutablePath,
    Action<LocalSendDevice> OpenDeviceDetails,
    string? JumpListFavoriteFingerprint,
    Action<string> ConsumeJumpListFavorite);
