using LocalSendDotNet;

namespace Tonarink.Pages.Send;

sealed record SendRequest(
    Guid TransferId,
    LocalSendDevice Device,
    IReadOnlyList<SendItem> Items,
    long TotalBytes,
    string? Pin,
    CancellationToken CancellationToken);

sealed record SuggestedContactSend(Guid Id, string Fingerprint, IReadOnlyList<SelectedSendItem> Items);

sealed record SendTransferStatus(
    TransferState State,
    string DeviceName,
    long BytesTransferred,
    long TotalBytes,
    string Message,
    bool IsError);

union OutgoingOverlayUpdate(
    OutgoingOverlayUpdate.Pending,
    OutgoingOverlayUpdate.AwaitingPin,
    OutgoingOverlayUpdate.Finished)
{
    public sealed record Pending;

    public sealed record AwaitingPin(OutgoingPinPrompt Prompt);

    public sealed record Finished;
}
