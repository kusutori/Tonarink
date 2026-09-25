namespace LocalSendDotNet;

/// <summary>Represents the final outcome of an outgoing transfer.</summary>
public union SendOutcome(
    SendOutcome.Completed,
    SendOutcome.Cancelled,
    SendOutcome.PinRequired,
    SendOutcome.PinRateLimited,
    SendOutcome.PeerBusy,
    SendOutcome.Declined,
    SendOutcome.Failed)
{
    /// <summary>The selected items were sent successfully.</summary>
    /// <param name="TransferId">The transfer identifier.</param>
    /// <param name="Items">Successfully transferred items.</param>
    public sealed record Completed(Guid TransferId, IReadOnlyList<TransferredItemResult> Items);

    /// <summary>The transfer was cancelled after it started.</summary>
    /// <param name="TransferId">The transfer identifier.</param>
    /// <param name="Items">Items transferred before cancellation.</param>
    public sealed record Cancelled(Guid TransferId, IReadOnlyList<TransferredItemResult> Items);

    /// <summary>The peer requires a PIN or rejected the supplied PIN.</summary>
    /// <param name="TransferId">The transfer identifier.</param>
    /// <param name="InvalidPin">Whether a supplied PIN was rejected.</param>
    public sealed record PinRequired(Guid TransferId, bool InvalidPin);

    /// <summary>The peer temporarily rate-limited PIN attempts.</summary>
    /// <param name="TransferId">The transfer identifier.</param>
    public sealed record PinRateLimited(Guid TransferId);

    /// <summary>The peer has no free transfer capacity.</summary>
    /// <param name="TransferId">The transfer identifier.</param>
    public sealed record PeerBusy(Guid TransferId);

    /// <summary>The peer declined the outgoing offer.</summary>
    /// <param name="TransferId">The transfer identifier.</param>
    public sealed record Declined(Guid TransferId);

    /// <summary>The transfer failed after zero or more items were sent.</summary>
    /// <param name="TransferId">The transfer identifier.</param>
    /// <param name="Items">Items transferred before the failure.</param>
    /// <param name="Failure">Structured diagnostic information.</param>
    public sealed record Failed(
        Guid TransferId,
        IReadOnlyList<TransferredItemResult> Items,
        TransferFailure Failure);
}

/// <summary>Represents the final outcome of an accepted incoming transfer.</summary>
public union ReceiveOutcome(
    ReceiveOutcome.Completed,
    ReceiveOutcome.Cancelled,
    ReceiveOutcome.Failed)
{
    /// <summary>All selected items were received successfully.</summary>
    /// <param name="TransferId">The transfer identifier.</param>
    /// <param name="Items">Successfully received items.</param>
    public sealed record Completed(Guid TransferId, IReadOnlyList<TransferredItemResult> Items);

    /// <summary>The incoming transfer was cancelled after it started.</summary>
    /// <param name="TransferId">The transfer identifier.</param>
    /// <param name="Items">Items received before cancellation.</param>
    public sealed record Cancelled(Guid TransferId, IReadOnlyList<TransferredItemResult> Items);

    /// <summary>The incoming transfer failed after zero or more items were received.</summary>
    /// <param name="TransferId">The transfer identifier.</param>
    /// <param name="Items">Items received before the failure.</param>
    /// <param name="Failure">Structured diagnostic information.</param>
    public sealed record Failed(
        Guid TransferId,
        IReadOnlyList<TransferredItemResult> Items,
        TransferFailure Failure);
}
