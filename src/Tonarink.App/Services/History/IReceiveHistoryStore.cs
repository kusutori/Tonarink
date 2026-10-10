namespace Tonarink.Services.History;

interface IReceiveHistoryStore
{
    IReadOnlyList<ReceiveHistoryEntry> Entries { get; }
    event Action? Changed;
    void Remove(Guid id);
    void Clear();
}
