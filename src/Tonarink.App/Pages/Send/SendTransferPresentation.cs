using LocalSendDotNet;
using Microsoft.UI.Reactor.Localization;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Pages.Send;

static class SendTransferPresentation
{
    public static string ContentSummary(IntlAccessor t, IReadOnlyList<SendItem> items) => items.Count switch
    {
        1 when items[0] is SendTextItem => t.Message(new("App", "ContentOneTextMessage")),
        1 => t.Message(new("App", "ContentOneFile"), ("file", items[0].FileName)),
        _ => t.Message(new("App", "ContentManyItems"), ("count", items.Count)),
    };

    public static string TransferProgressText(long bytesTransferred, long totalBytes) => totalBytes > 0
        ? $"{FormatBytes(bytesTransferred)} / {FormatBytes(totalBytes)}"
        : FormatBytes(bytesTransferred);

    public static TransferUiState.Active ResultState(
        IntlAccessor t,
        OutgoingTransferResult result,
        string deviceAlias,
        long requestedBytes) => result switch
        {
            OutgoingTransferResult.Completed completed => new TransferUiState.Active(
                TransferState.Completed,
                deviceAlias,
                completed.Items.Sum(static item => item.BytesTransferred),
                completed.Items.Sum(static item => item.BytesTransferred),
                t.Message(new("App", "SentToDevice"), ("device", deviceAlias)),
                IsError: false),
            OutgoingTransferResult.Cancelled cancelled => new TransferUiState.Active(
                TransferState.Cancelled,
                deviceAlias,
                cancelled.Items.Sum(static item => item.BytesTransferred),
                requestedBytes,
                t.Message(new("App", "TransferCancelled")),
                IsError: false),
            OutgoingTransferResult.Failed failed => new TransferUiState.Active(
                TransferState.Failed,
                deviceAlias,
                failed.Items.Sum(static item => item.BytesTransferred),
                requestedBytes,
                failed.Failure.Message,
                IsError: true),
            _ => new TransferUiState.Active(
                TransferState.Failed,
                deviceAlias,
                0,
                requestedBytes,
                t.Message(new("App", "TransferFailed")),
                IsError: true),
        };

    public static string ProgressMessage(
        IntlAccessor t,
        TransferState state,
        string deviceAlias) => state switch
        {
            TransferState.Preparing => t.Message(new("App", "PreparingForDevice"), ("device", deviceAlias)),
            TransferState.WaitingForAcceptance => t.Message(new("App", "WaitingForDevice"), ("device", deviceAlias)),
            TransferState.Transferring => t.Message(new("App", "SendingToDevice"), ("device", deviceAlias)),
            TransferState.Completed => t.Message(new("App", "SentToDevice"), ("device", deviceAlias)),
            TransferState.Cancelled => t.Message(new("App", "TransferCancelled")),
            _ => t.Message(new("App", "TransferFailed")),
        };
}
