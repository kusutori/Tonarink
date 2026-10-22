using LocalSendDotNet;
using Microsoft.UI.Reactor.Localization;
using static Tonarink.Pages.Send.SendTransferPresentation;

namespace Tonarink.Services.Transfers;

/// <summary>Shared transport and notification lifecycle for page and tray sends.</summary>
static class OutgoingTransferService
{
    public static async Task<OutgoingTransferResult> SendAsync(
        LocalSendNode node,
        SendRequest request,
        bool verifyChecksums,
        IntlAccessor t,
        Action<TransferProgress> onProgress,
        string notificationKind)
    {
        var summary = ContentSummary(t, request.Items);
        var notification = AppNotificationService.StartTransferProgress(
            request.TransferId,
            t.Message(new("App", "NotificationSendProgressTitle"), ("device", request.Device.Alias)),
            summary, t.Message(new("App", "SendingTransferring")),
            0, request.TotalBytes, TransferProgressText(0, request.TotalBytes), notificationKind);
        var progress = new Progress<TransferProgress>(value =>
        {
            onProgress(value);
            notification?.Report(summary, ProgressMessage(t, value.State, request.Device.Alias),
                value.BytesTransferred, value.TotalBytes,
                TransferProgressText(value.BytesTransferred, value.TotalBytes));
        });
        try
        {
            return CoreTransferOutcomeMapper.Map(await node.SendAsync(
                request.Device, request.Items,
                new SendOptions { Pin = request.Pin, ComputeSha256 = verifyChecksums },
                progress, request.CancellationToken).ConfigureAwait(false));
        }
        finally
        {
            if (notification is not null)
                await notification.RemoveAsync().ConfigureAwait(false);
        }
    }

    public static void NotifyCompleted(IntlAccessor t, string deviceAlias, int count,
        IEnumerable<string> paths, NotificationDefaultAction action, string kind) =>
        AppNotificationService.ShowTransferComplete(
            t.Message(new("App", "NotificationSendCompleteTitle")),
            t.Message(new("App", "NotificationSendComplete"), ("count", count), ("device", deviceAlias)),
            kind, paths, action,
            t.Message(new("App", "NotificationOpenFile")),
            t.Message(new("App", "NotificationShowInFolder")));
}
