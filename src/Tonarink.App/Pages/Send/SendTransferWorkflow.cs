using LocalSendDotNet;

namespace Tonarink.Pages.Send;

union TransferUiState(TransferUiState.Idle, TransferUiState.Active)
{
    public sealed record Idle(string Message);

    public sealed record Active(
        TransferState State,
        string DeviceName,
        long BytesTransferred,
        long TotalBytes,
        string Message,
        bool IsError);

    public TransferState? State => this switch
    {
        Idle => null,
        Active(var state, _, _, _, _, _) => state,
    };

    public long BytesTransferred => this switch
    {
        Idle => 0,
        Active(_, _, var bytesTransferred, _, _, _) => bytesTransferred,
    };

    public long TotalBytes => this switch
    {
        Idle => 0,
        Active(_, _, _, var totalBytes, _, _) => totalBytes,
    };

    public string Message => this switch
    {
        Idle(var message) => message,
        Active(_, _, _, _, var message, _) => message,
    };

    public bool IsError => this is Active { IsError: true };

    public TransferUiState WithMessage(string message) => this switch
    {
        Idle => new Idle(message),
        Active active => active with { Message = message },
    };
}

union SendTransferAction(
    SendTransferAction.Reset,
    SendTransferAction.Started,
    SendTransferAction.Progressed,
    SendTransferAction.Finished,
    SendTransferAction.PinRequested,
    SendTransferAction.Failed,
    SendTransferAction.MessageChanged)
{
    public sealed record Reset(string Message);

    public sealed record Started(TransferUiState.Active State);

    public sealed record Progressed(TransferUiState.Active State);

    public sealed record Finished(TransferUiState.Active State);

    public sealed record PinRequested(TransferUiState.Active State);

    public sealed record Failed(TransferUiState.Active State);

    public sealed record MessageChanged(string Message);
}

union OutgoingOverlayUpdate(
    OutgoingOverlayUpdate.Pending,
    OutgoingOverlayUpdate.AwaitingPin,
    OutgoingOverlayUpdate.Finished)
{
    public sealed record Pending;

    public sealed record AwaitingPin(OutgoingPinPrompt Prompt);

    public sealed record Finished;
}

static class SendTransferReducer
{
    public static TransferUiState Reduce(
        TransferUiState state,
        SendTransferAction action) => action switch
    {
        SendTransferAction.Reset reset => new TransferUiState.Idle(reset.Message),
        SendTransferAction.Started started => started.State,
        SendTransferAction.Progressed progressed => progressed.State,
        SendTransferAction.Finished finished => finished.State,
        SendTransferAction.PinRequested requested => requested.State,
        SendTransferAction.Failed failed => failed.State,
        SendTransferAction.MessageChanged changed => state.WithMessage(changed.Message),
    };
}
