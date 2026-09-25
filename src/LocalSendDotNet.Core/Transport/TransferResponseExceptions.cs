namespace LocalSendDotNet;

internal sealed class PinRequiredException(bool invalidPin)
    : LocalSendException(invalidPin
        ? "The remote device rejected the PIN."
        : "The remote device requires a PIN.")
{
    public bool InvalidPin { get; } = invalidPin;
}

internal sealed class PinRateLimitedException()
    : LocalSendException("The remote device has rate-limited PIN attempts.");

internal sealed class PeerBusyException()
    : LocalSendException("The remote device is handling the maximum number of transfers.");

internal sealed class TransferDeclinedException()
    : LocalSendException("The remote device declined the transfer.");
