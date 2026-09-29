using LocalSendDotNet;
using Microsoft.UI.Reactor;
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

sealed record SelectedSendItem(
    Guid Id,
    SendItem Item,
    string DisplayName,
    long Length,
    string Kind,
    string? LocalPath = null,
    string? OriginalFileName = null,
    string? OriginalDisplayName = null,
    string? RedoFileName = null,
    string? RedoDisplayName = null)
{
    public bool IsRenamed => OriginalFileName is not null
                             && !string.Equals(Item.FileName, OriginalFileName, StringComparison.Ordinal);

    public bool CanRedoRename => RedoFileName is not null
                                 && !string.Equals(Item.FileName, RedoFileName, StringComparison.Ordinal);
}

sealed record SendRequest(
    Guid TransferId,
    LocalSendDevice Device,
    IReadOnlyList<SendItem> Items,
    long TotalBytes,
    string? Pin,
    CancellationToken CancellationToken);

sealed record SuggestedContactSend(Guid Id, string Fingerprint);
