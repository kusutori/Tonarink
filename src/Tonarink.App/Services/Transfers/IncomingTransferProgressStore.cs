namespace Tonarink.Services.Transfers;

// A presentation snapshot shared by the tray and widgets; neither owns the transfer.
static class IncomingTransferProgressStore
{
    private static IncomingTransferProgress? _current;

    public static IncomingTransferProgress? Current => Volatile.Read(ref _current);

    public static event Action? Changed;

    public static void Publish(IncomingTransferProgress? progress)
    {
        Interlocked.Exchange(ref _current, progress);
        Changed?.Invoke();
    }
}
