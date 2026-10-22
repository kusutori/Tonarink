using LocalSendDotNet;
using Microsoft.UI.Reactor.Localization;
using static Tonarink.Services.Transfers.TransferPresentation;

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

        try
        {
            var result = await OutgoingTransferService.SendAsync(node,
                new(transferId, request.Device, request.Items, request.TotalBytes, request.Pin, cancellation.Token),
                settings.VerifyChecksumsOnSend, t,
                value => PublishPending(value.State, value.BytesTransferred, value.TotalBytes,
                    ProgressMessage(t, value.State, request.Device.Alias)), "tray-send-progress");
            if (result is OutgoingTransferResult.PinRequired)
            {
                setOutgoingTransfer(null);
                return result;
            }

            var finished = ResultState(t, result, request.Device.Alias, request.TotalBytes);
            PublishFinished(finished.State, finished.BytesTransferred, finished.TotalBytes,
                finished.Message, finished.IsError);

            if (result is OutgoingTransferResult.Completed)
            {
                OutgoingTransferService.NotifyCompleted(t, request.Device.Alias, request.Items.Count,
                    request.LocalPaths ?? request.Items.OfType<SendFileItem>().Select(static item => item.Path).ToArray(),
                    settings.NotificationDefaultAction, "tray-send-complete");
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
    }
}
