using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tonarink.Services.History;

static class ReceiveHistoryStore
{
    private const int MaxEntries = 500;
    private static readonly string FilePath = Path.Combine(AppPlatform.DataDirectory, "receive-history.json");
    private static readonly Lock Gate = new();
    private static IReadOnlyList<ReceiveHistoryEntry>? _entries;

    public static IReceiveHistoryStore Source { get; } = new PersistentSource();

    public static event Action? Changed;

    public static IReadOnlyList<ReceiveHistoryEntry> Entries
    {
        get
        {
            lock (Gate)
                return _entries ??= LoadUnlocked();
        }
    }

    public static bool TryRecord(string senderAlias, IncomingTransferResult.Completed result)
    {
        try
        {
            Record(senderAlias, result);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // History is optional bookkeeping; the files have already been received.
            AppDiagnostics.Report("Transfer completed, but receive history could not be saved", exception);
            return false;
        }
    }

    private static void Record(string senderAlias, IncomingTransferResult.Completed result)
    {
        var sender = string.IsNullOrWhiteSpace(senderAlias) ? "?" : senderAlias.Trim();
        var receivedAt = DateTimeOffset.UtcNow;
        var added = new List<ReceiveHistoryEntry>();
        foreach (var item in result.Items)
        {
            if (string.IsNullOrWhiteSpace(item.SavedPath))
                continue;

            added.Add(new ReceiveHistoryEntry(
                Guid.NewGuid(),
                string.IsNullOrWhiteSpace(item.FileName) ? Path.GetFileName(item.SavedPath) : item.FileName,
                item.SavedPath,
                item.BytesTransferred,
                sender,
                receivedAt));
        }

        if (added.Count == 0)
            return;

        Mutate(current => (ReceiveHistoryEntry[])[.. added, .. current]);
    }

    public static void Remove(Guid id) =>
        Mutate(current => (ReceiveHistoryEntry[])[.. current.Where(entry => entry.Id != id)]);

    public static void Clear() => Mutate(_ => []);

    private static void Mutate(Func<IReadOnlyList<ReceiveHistoryEntry>, IReadOnlyList<ReceiveHistoryEntry>> update)
    {
        lock (Gate)
        {
            var next = update(_entries ??= LoadUnlocked());
            if (next.Count > MaxEntries)
                next = (ReceiveHistoryEntry[])[.. next.Take(MaxEntries)];
            SaveUnlocked(next);
            _entries = next;
        }

        Changed?.Invoke();
    }

    private static IReadOnlyList<ReceiveHistoryEntry> LoadUnlocked()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            var file = JsonSerializer.Deserialize(
                File.ReadAllText(FilePath),
                ReceiveHistoryJsonContext.Default.ReceiveHistoryFile);
            return file?.ToEntries() ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Report("Could not load receive history", exception);
            return [];
        }
    }

    private static void SaveUnlocked(IReadOnlyList<ReceiveHistoryEntry> entries)
    {
        var json = JsonSerializer.Serialize(
            ReceiveHistoryFile.FromEntries(entries),
            ReceiveHistoryJsonContext.Default.ReceiveHistoryFile);
        AtomicFile.WriteAllText(FilePath, json);
    }

    private sealed class PersistentSource : IReceiveHistoryStore
    {
        IReadOnlyList<ReceiveHistoryEntry> IReceiveHistoryStore.Entries => ReceiveHistoryStore.Entries;

        event Action? IReceiveHistoryStore.Changed
        {
            add => ReceiveHistoryStore.Changed += value;
            remove => ReceiveHistoryStore.Changed -= value;
        }

        void IReceiveHistoryStore.Remove(Guid id) => ReceiveHistoryStore.Remove(id);
        void IReceiveHistoryStore.Clear() => ReceiveHistoryStore.Clear();
    }
}

sealed class ReceiveHistoryFile
{
    public List<ReceiveHistoryItemFile>? Items { get; set; }

    public static ReceiveHistoryFile FromEntries(IReadOnlyList<ReceiveHistoryEntry> entries) => new()
    {
        Items =
        [
            .. entries.Select(static entry => new ReceiveHistoryItemFile
            {
                Id = entry.Id,
                FileName = entry.FileName,
                Path = entry.Path,
                Size = entry.Size,
                SenderAlias = entry.SenderAlias,
                ReceivedAt = entry.ReceivedAt,
            })
        ],
    };

    public IReadOnlyList<ReceiveHistoryEntry> ToEntries()
    {
        if (Items is not { Count: > 0 })
            return [];

        return (ReceiveHistoryEntry[])
        [
            .. Items
                .Where(static item => item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Path))
                .Select(static item =>
                {
                    var path = item.Path ?? string.Empty;
                    var fileName = string.IsNullOrWhiteSpace(item.FileName)
                        ? Path.GetFileName(path)
                        : item.FileName;
                    return new ReceiveHistoryEntry(
                        item.Id,
                        string.IsNullOrWhiteSpace(fileName) ? path : fileName,
                        path,
                        item.Size,
                        string.IsNullOrWhiteSpace(item.SenderAlias) ? "?" : item.SenderAlias,
                        item.ReceivedAt == default ? DateTimeOffset.UtcNow : item.ReceivedAt);
                })
        ];
    }
}

sealed class ReceiveHistoryItemFile
{
    public Guid Id { get; set; }
    public string? FileName { get; set; }
    public string? Path { get; set; }
    public long Size { get; set; }
    public string? SenderAlias { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ReceiveHistoryFile))]
internal sealed partial class ReceiveHistoryJsonContext : JsonSerializerContext;
