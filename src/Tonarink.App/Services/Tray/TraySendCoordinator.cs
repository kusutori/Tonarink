using LocalSendDotNet;
using Microsoft.UI.Reactor.Localization;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Services.Tray;

/// <summary>Runs a transfer initiated by dropping files onto the tray flyout.</summary>
static class TraySendCoordinator
{
    public static async Task<OutgoingTransferResult> SendAsync(
        TraySendRequest request,
        LocalSendNode? node,
        LocalSendIdentity? identity,
        AppSettings settings,
        IntlAccessor t,
        Action<OutgoingTransferViewState?> setOutgoingTransfer)
    {
        if (node?.State != LocalSendNodeState.Running)
            throw new InvalidOperationException(t.Message(new("App", "NodeDisconnected")));

        var transferId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var summary = ContentSummary(t, request.Items);
        var progressNotification = AppNotificationService.StartTransferProgress(
            transferId,
            t.Message(new("App", "NotificationSendProgressTitle"), ("device", request.Device.Alias)),
            summary,
            t.Message(new("App", "SendingTransferring")),
            0,
            request.TotalBytes,
            ProgressText(0, request.TotalBytes),
            "tray-send-progress");

        OutgoingTransferSnapshot Snapshot(
            TransferState state,
            long bytesTransferred,
            long totalBytes,
            string status) => new(
                identity,
                request.Device,
                summary,
                state,
                bytesTransferred,
                totalBytes,
                status,
                cancellation.Cancel);

        void PublishPending(
            TransferState state,
            long bytesTransferred,
            long totalBytes,
            string status) =>
            setOutgoingTransfer(new OutgoingTransferViewState.Pending(
                Snapshot(state, bytesTransferred, totalBytes, status)));

        void PublishFinished(
            TransferState state,
            long bytesTransferred,
            long totalBytes,
            string status,
            bool isError) =>
            setOutgoingTransfer(new OutgoingTransferViewState.Finished(
                Snapshot(state, bytesTransferred, totalBytes, status),
                isError));

        PublishPending(
            TransferState.Preparing,
            0,
            request.TotalBytes,
            t.Message(new("App", "PreparingForDevice"), ("device", request.Device.Alias)));

        var progress = new Progress<TransferProgress>(value =>
        {
            var status = ProgressMessage(t, value.State, request.Device.Alias);
            PublishPending(
                value.State,
                value.BytesTransferred,
                value.TotalBytes,
                status);
            progressNotification?.Report(
                summary,
                status,
                value.BytesTransferred,
                value.TotalBytes,
                ProgressText(value.BytesTransferred, value.TotalBytes));
        });

        try
        {
            var result = CoreTransferOutcomeMapper.Map(await node.SendAsync(
                    request.Device,
                    request.Items,
                    new SendOptions
                    {
                        Pin = request.Pin,
                        ComputeSha256 = settings.VerifyChecksumsOnSend,
                    },
                    progress,
                    cancellation.Token)
                .ConfigureAwait(false));
            if (result is OutgoingTransferResult.PinRequired)
            {
                setOutgoingTransfer(null);
                return result;
            }

            var (state, bytesTransferred, status, isError) = result switch
            {
                OutgoingTransferResult.Completed completed => (
                    TransferState.Completed,
                    completed.Items.Sum(static item => item.BytesTransferred),
                    t.Message(new("App", "SentToDevice"), ("device", request.Device.Alias)),
                    false),
                OutgoingTransferResult.Cancelled cancelled => (
                    TransferState.Cancelled,
                    cancelled.Items.Sum(static item => item.BytesTransferred),
                    t.Message(new("App", "TransferCancelled")),
                    false),
                OutgoingTransferResult.PinRateLimited => (
                    TransferState.Failed,
                    0,
                    t.Message(new("App", "PinRateLimited")),
                    true),
                OutgoingTransferResult.Failed failed => (
                    TransferState.Failed,
                    failed.Items.Sum(static item => item.BytesTransferred),
                    failed.Failure.Message,
                    true),
                _ => (
                    TransferState.Failed,
                    0,
                    t.Message(new("App", "TransferFailed")),
                    true),
            };
            PublishFinished(
                state,
                bytesTransferred,
                state == TransferState.Completed ? bytesTransferred : request.TotalBytes,
                status,
                isError);

            if (result is OutgoingTransferResult.Completed)
            {
                AppNotificationService.ShowTransferComplete(
                    t.Message(new("App", "NotificationSendCompleteTitle")),
                    request.Items.Count == 1
                        ? t.Message(
                            new("App", "NotificationSendCompleteOne"),
                            ("device", request.Device.Alias))
                        : t.Message(
                            new("App", "NotificationSendCompleteMany"),
                            ("count", request.Items.Count),
                            ("device", request.Device.Alias)),
                    "tray-send-complete",
                    request.Items.OfType<SendFileItem>().Select(static item => item.Path),
                    settings.NotificationDefaultAction,
                    t.Message(new("App", "NotificationOpenFile")),
                    t.Message(new("App", "NotificationShowInFolder")));
            }
            return result;
        }
        catch (Exception exception)
        {
            PublishFinished(
                exception is OperationCanceledException
                    ? TransferState.Cancelled
                    : TransferState.Failed,
                0,
                request.TotalBytes,
                exception is OperationCanceledException
                    ? t.Message(new("App", "TransferCancelled"))
                    : exception.Message,
                exception is not OperationCanceledException);
            throw;
        }
        finally
        {
            if (progressNotification is not null)
                await progressNotification.RemoveAsync();
        }
    }

    private static string ContentSummary(IntlAccessor t, IReadOnlyList<SendItem> items) => items.Count switch
    {
        1 when items[0] is SendTextItem => t.Message(new("App", "ContentOneTextMessage")),
        1 => t.Message(new("App", "ContentOneFile"), ("file", items[0].FileName)),
        _ => t.Message(new("App", "ContentManyItems"), ("count", items.Count)),
    };

    private static string ProgressMessage(IntlAccessor t, TransferState state, string deviceAlias) => state switch
    {
        TransferState.Preparing => t.Message(new("App", "PreparingForDevice"), ("device", deviceAlias)),
        TransferState.WaitingForAcceptance => t.Message(new("App", "WaitingForDevice"), ("device", deviceAlias)),
        TransferState.Transferring => t.Message(new("App", "SendingToDevice"), ("device", deviceAlias)),
        TransferState.Completed => t.Message(new("App", "SentToDevice"), ("device", deviceAlias)),
        TransferState.Cancelled => t.Message(new("App", "TransferCancelled")),
        _ => t.Message(new("App", "TransferFailed")),
    };

    private static string ProgressText(long bytesTransferred, long totalBytes) => totalBytes > 0
        ? $"{FormatBytes(bytesTransferred)} / {FormatBytes(totalBytes)}"
        : FormatBytes(bytesTransferred);
}
