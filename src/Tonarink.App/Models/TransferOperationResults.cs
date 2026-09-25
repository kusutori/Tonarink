namespace Tonarink.Models;

sealed record TransferredContentItem(
    string ItemId,
    string FileName,
    long BytesTransferred,
    string? SavedPath);

sealed record TransferFailureInfo(string Code, string Message, string? ItemId);

union OutgoingTransferResult(
    OutgoingTransferResult.Completed,
    OutgoingTransferResult.Cancelled,
    OutgoingTransferResult.PinRequired,
    OutgoingTransferResult.PinRateLimited,
    OutgoingTransferResult.PeerBusy,
    OutgoingTransferResult.Declined,
    OutgoingTransferResult.Failed)
{
    public sealed record Completed(Guid TransferId, IReadOnlyList<TransferredContentItem> Items);

    public sealed record Cancelled(Guid TransferId, IReadOnlyList<TransferredContentItem> Items);

    public sealed record PinRequired(Guid TransferId, bool InvalidPin);

    public sealed record PinRateLimited(Guid TransferId);

    public sealed record PeerBusy(Guid TransferId);

    public sealed record Declined(Guid TransferId);

    public sealed record Failed(
        Guid TransferId,
        IReadOnlyList<TransferredContentItem> Items,
        TransferFailureInfo Failure);
}

union IncomingTransferResult(
    IncomingTransferResult.Completed,
    IncomingTransferResult.Cancelled,
    IncomingTransferResult.Failed)
{
    public sealed record Completed(Guid TransferId, IReadOnlyList<TransferredContentItem> Items);

    public sealed record Cancelled(Guid TransferId, IReadOnlyList<TransferredContentItem> Items);

    public sealed record Failed(
        Guid TransferId,
        IReadOnlyList<TransferredContentItem> Items,
        TransferFailureInfo Failure);
}
