using LocalSendDotNet;

namespace Tonarink.Services.Transfers;

static class CoreTransferOutcomeMapper
{
    public static OutgoingTransferResult Map(SendOutcome outcome) => outcome switch
    {
        SendOutcome.Completed completed => new OutgoingTransferResult.Completed(
            completed.TransferId,
            MapItems(completed.Items)),
        SendOutcome.Cancelled cancelled => new OutgoingTransferResult.Cancelled(
            cancelled.TransferId,
            MapItems(cancelled.Items)),
        SendOutcome.PinRequired required => new OutgoingTransferResult.PinRequired(
            required.TransferId,
            required.InvalidPin),
        SendOutcome.PinRateLimited limited => new OutgoingTransferResult.PinRateLimited(limited.TransferId),
        SendOutcome.PeerBusy busy => new OutgoingTransferResult.PeerBusy(busy.TransferId),
        SendOutcome.Declined declined => new OutgoingTransferResult.Declined(declined.TransferId),
        SendOutcome.Failed failed => new OutgoingTransferResult.Failed(
            failed.TransferId,
            MapItems(failed.Items),
            MapFailure(failed.Failure)),
    };

    public static IncomingTransferResult Map(ReceiveOutcome outcome) => outcome switch
    {
        ReceiveOutcome.Completed completed => new IncomingTransferResult.Completed(
            completed.TransferId,
            MapItems(completed.Items)),
        ReceiveOutcome.Cancelled cancelled => new IncomingTransferResult.Cancelled(
            cancelled.TransferId,
            MapItems(cancelled.Items)),
        ReceiveOutcome.Failed failed => new IncomingTransferResult.Failed(
            failed.TransferId,
            MapItems(failed.Items),
            MapFailure(failed.Failure)),
    };

    private static IReadOnlyList<TransferredContentItem> MapItems(
        IReadOnlyList<TransferredItemResult> items) =>
        (TransferredContentItem[])
        [
            .. items.Select(static item => new TransferredContentItem(
                item.ItemId,
                item.FileName,
                item.BytesTransferred,
                item.SavedPath))
        ];

    private static TransferFailureInfo MapFailure(TransferFailure failure) =>
        new(failure.Code, failure.Message, failure.ItemId);
}
