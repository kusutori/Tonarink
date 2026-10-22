namespace Tonarink.Pages.Send;

union OutgoingOverlayUpdate(
    OutgoingOverlayUpdate.Pending,
    OutgoingOverlayUpdate.AwaitingPin,
    OutgoingOverlayUpdate.Finished)
{
    public sealed record Pending;

    public sealed record AwaitingPin(OutgoingPinPrompt Prompt);

    public sealed record Finished;
}
