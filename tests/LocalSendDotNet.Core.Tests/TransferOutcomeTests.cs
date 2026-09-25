using LocalSendDotNet;

namespace LocalSendDotNet.Core.Tests;

public sealed class TransferOutcomeTests
{
    [Fact]
    public void SendOutcomeCarriesOnlyCaseSpecificData()
    {
        var transferId = Guid.NewGuid();
        SendOutcome outcome = new SendOutcome.PinRequired(transferId, InvalidPin: true);

        var description = outcome switch
        {
            SendOutcome.Completed completed => $"completed:{completed.Items.Count}",
            SendOutcome.Cancelled cancelled => $"cancelled:{cancelled.Items.Count}",
            SendOutcome.PinRequired pin => $"pin:{pin.InvalidPin}",
            SendOutcome.PinRateLimited => "pin-rate-limited",
            SendOutcome.PeerBusy => "peer-busy",
            SendOutcome.Declined => "declined",
            SendOutcome.Failed failed => $"failed:{failed.Failure.Code}",
        };

        Assert.Equal("pin:True", description);
    }

    [Fact]
    public void ReceiveFailureRequiresStructuredFailure()
    {
        var failure = new TransferFailure(TransferFailureCodes.ChecksumMismatch, "Digest mismatch", "item-1");
        ReceiveOutcome outcome = new ReceiveOutcome.Failed(Guid.NewGuid(), [], failure);

        var actual = outcome switch
        {
            ReceiveOutcome.Failed failed => failed.Failure,
            _ => null,
        };

        Assert.Same(failure, actual);
    }
}
