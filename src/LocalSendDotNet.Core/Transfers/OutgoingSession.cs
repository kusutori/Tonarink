namespace LocalSendDotNet;

internal sealed class OutgoingSession(DeviceEndpoint endpoint, string fingerprint, CancellationTokenSource cancellation)
{
    private int _peerCancelled;
    public DeviceEndpoint Endpoint { get; } = endpoint;
    public string Fingerprint { get; } = fingerprint;
    public CancellationTokenSource Cancellation { get; } = cancellation;
    public bool PeerCancelled => Volatile.Read(ref _peerCancelled) != 0;

    // A peer's /cancel is already the handshake. Do not echo it while that
    // peer is waiting for our acknowledgement or shutting down its listener.
    public void MarkPeerCancellation() => Interlocked.Exchange(ref _peerCancelled, 1);
}
