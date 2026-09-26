using LocalSendDotNet;

namespace Tonarink.Components.Transfers;

sealed record OutgoingTransferSnapshot(
    LocalSendIdentity? Sender,
    LocalSendDevice Receiver,
    string ContentSummary,
    TransferState State,
    long BytesTransferred,
    long TotalBytes,
    string Status,
    Action Cancel);

union OutgoingTransferViewState(
    OutgoingTransferViewState.Pending,
    OutgoingTransferViewState.AwaitingPin,
    OutgoingTransferViewState.Finished)
{
    public sealed record Pending(OutgoingTransferSnapshot Transfer);

    public sealed record AwaitingPin(OutgoingTransferSnapshot Transfer, OutgoingPinPrompt Prompt);

    public sealed record Finished(OutgoingTransferSnapshot Transfer, bool IsError);

    public OutgoingTransferSnapshot Transfer => this switch
    {
        Pending(var transfer) => transfer,
        AwaitingPin(var transfer, _) => transfer,
        Finished(var transfer, _) => transfer,
    };

    public LocalSendIdentity? Sender => Transfer.Sender;

    public LocalSendDevice Receiver => Transfer.Receiver;

    public string ContentSummary => Transfer.ContentSummary;

    public TransferState State => Transfer.State;

    public long BytesTransferred => Transfer.BytesTransferred;

    public long TotalBytes => Transfer.TotalBytes;

    public string Status => Transfer.Status;

    public Action Cancel => Transfer.Cancel;

    public bool IsPending => this is Pending;

    public bool IsError => this is Finished { IsError: true };

    public OutgoingPinPrompt? PinPrompt => this is AwaitingPin(_, var prompt) ? prompt : null;
}

sealed record OutgoingPinPrompt(
    string? Error,
    Action<string> Submit,
    Action Cancel);
