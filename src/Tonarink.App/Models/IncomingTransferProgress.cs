namespace Tonarink.Models;

sealed record IncomingTransferProgress(
    string Title,
    string Peer,
    string Status,
    long BytesTransferred,
    long TotalBytes,
    bool Indeterminate);
