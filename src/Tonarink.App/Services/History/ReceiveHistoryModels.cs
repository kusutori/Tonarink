namespace Tonarink.Services.History;

sealed record ReceiveHistoryEntry(
    Guid Id,
    string FileName,
    string Path,
    long Size,
    string SenderAlias,
    DateTimeOffset ReceivedAt);
