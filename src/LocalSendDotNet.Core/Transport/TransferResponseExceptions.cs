namespace LocalSendDotNet;

internal sealed class PinRequiredException(bool invalidPin)
    : LocalSendException(invalidPin
        ? CoreText.PeerPinInvalid
        : CoreText.PeerPinRequired)
{
    public bool InvalidPin { get; } = invalidPin;
}

internal sealed class RateLimitedException()
    : LocalSendException(CoreText.RateLimited);

internal sealed class PeerBusyException()
    : LocalSendException(CoreText.TransferBusy);

internal sealed class TransferDeclinedException()
    : LocalSendException(CoreText.TransferDeclined);

// Classification must survive translating the human-readable exception message.
internal sealed class IncomingContentException(string code, string message) : LocalSendException(message)
{
    public string Code { get; } = code;
}
