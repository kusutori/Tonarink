using LocalSendDotNet;
using static Tonarink.Components.Transfers.TransferOverlayVisuals;

namespace Tonarink.Components.Transfers;

union IncomingTransferViewState(
    IncomingTransferViewState.Pending,
    IncomingTransferViewState.Receiving,
    IncomingTransferViewState.Finished)
{
    public sealed record Pending(long TotalBytes, string Status, string? Text);

    public sealed record Receiving(
        TransferState State,
        long BytesTransferred,
        long TotalBytes,
        string Status,
        string? Text);

    public sealed record Finished(
        TransferState State,
        long BytesTransferred,
        long TotalBytes,
        string Status,
        string? Text,
        bool IsError);

    public TransferState State => this switch
    {
        Pending => TransferState.WaitingForAcceptance,
        Receiving(var state, _, _, _, _) => state,
        Finished(var state, _, _, _, _, _) => state,
    };

    public long BytesTransferred => this switch
    {
        Pending => 0,
        Receiving(_, var bytesTransferred, _, _, _) => bytesTransferred,
        Finished(_, var bytesTransferred, _, _, _, _) => bytesTransferred,
    };

    public long TotalBytes => this switch
    {
        Pending(var totalBytes, _, _) => totalBytes,
        Receiving(_, _, var totalBytes, _, _) => totalBytes,
        Finished(_, _, var totalBytes, _, _, _) => totalBytes,
    };

    public string Status => this switch
    {
        Pending(_, var status, _) => status,
        Receiving(_, _, _, var status, _) => status,
        Finished(_, _, _, var status, _, _) => status,
    };

    public string? Text => this switch
    {
        Pending(_, _, var text) => text,
        Receiving(_, _, _, _, var text) => text,
        Finished(_, _, _, _, var text, _) => text,
    };

    public bool IsDecided => this is not Pending;

    public bool IsError => this is Finished { IsError: true };

    public static IncomingTransferViewState Initial(IncomingTransferRequest request, string summary) =>
        new Pending(
            request.Items.Sum(static item => item.Size),
            summary,
            InitialText(request.Items));

    private static string? InitialText(IReadOnlyList<IncomingItem> items) =>
        items.Count == 1 && IsText(items[0]) ? items[0].Preview : null;
}

union IncomingTransferAction(
    IncomingTransferAction.Started,
    IncomingTransferAction.Progressed,
    IncomingTransferAction.Completed,
    IncomingTransferAction.Failed)
{
    public sealed record Started(string Status);

    public sealed record Progressed(TransferProgress Progress, string Status);

    public sealed record Completed(
        TransferState State,
        long BytesTransferred,
        string Status,
        string? Text,
        bool IsError);

    public sealed record Failed(string Status);
}

static class IncomingTransferReducer
{
    public static IncomingTransferViewState Reduce(
        IncomingTransferViewState state,
        IncomingTransferAction action) => action switch
    {
        IncomingTransferAction.Started started => new IncomingTransferViewState.Receiving(
            TransferState.Preparing,
            state.BytesTransferred,
            state.TotalBytes,
            started.Status,
            state.Text),
        IncomingTransferAction.Progressed progressed => new IncomingTransferViewState.Receiving(
            progressed.Progress.State,
            progressed.Progress.BytesTransferred,
            progressed.Progress.TotalBytes,
            progressed.Status,
            state.Text),
        IncomingTransferAction.Completed completed => new IncomingTransferViewState.Finished(
            completed.State,
            completed.BytesTransferred,
            state.TotalBytes,
            completed.Status,
            completed.Text ?? state.Text,
            completed.IsError),
        IncomingTransferAction.Failed failed => new IncomingTransferViewState.Finished(
            TransferState.Failed,
            state.BytesTransferred,
            state.TotalBytes,
            failed.Status,
            state.Text,
            IsError: true),
    };
}
