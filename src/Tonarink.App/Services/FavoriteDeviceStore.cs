using System.Text.Json;
using System.Text.Json.Serialization;

sealed record FavoriteDevice(
    string Fingerprint,
    string Name,
    string Address,
    int Port);

static class FavoriteDeviceStore
{
    private static readonly string FilePath = Path.Combine(
        AppPlatform.DataDirectory,
        "favorite-devices.json");
    private static readonly object Gate = new();
    private static IReadOnlyDictionary<string, FavoriteDevice>? _cached;
    private static int _revision;

    public static event Action? Changed;

    public static int Revision
    {
        get
        {
            lock (Gate)
                return _revision;
        }
    }

    public static IReadOnlyDictionary<string, FavoriteDevice> Entries
    {
        get
        {
            lock (Gate)
                return _cached ??= LoadCore();
        }
    }

    public static bool Contains(string fingerprint)
    {
        lock (Gate)
            return (_cached ??= LoadCore()).ContainsKey(fingerprint);
    }

    public static void Upsert(FavoriteDevice favorite)
    {
        lock (Gate)
        {
            var updated = (_cached ??= LoadCore()).ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
            updated[favorite.Fingerprint] = favorite;
            _cached = updated;
            SaveCore(updated);
            _revision++;
        }

        Changed?.Invoke();
    }

    public static void Remove(string fingerprint)
    {
        var removed = false;
        lock (Gate)
        {
            var updated = (_cached ??= LoadCore()).ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
            removed = updated.Remove(fingerprint);
            if (removed)
            {
                _cached = updated;
                SaveCore(updated);
                _revision++;
            }
        }

        if (removed)
            Changed?.Invoke();
    }

    private static IReadOnlyDictionary<string, FavoriteDevice> LoadCore()
    {
        try
        {
            if (!File.Exists(FilePath))
                return Empty();

            var json = File.ReadAllText(FilePath);
            var entries = JsonSerializer.Deserialize(
                json,
                FavoriteDeviceJsonContext.Default.DictionaryStringFavoriteDevice);
            return entries is null
                ? Empty()
                : new Dictionary<string, FavoriteDevice>(entries, StringComparer.Ordinal);
        }
        catch
        {
            return Empty();
        }
    }

    private static void SaveCore(IReadOnlyDictionary<string, FavoriteDevice> entries)
    {
        Directory.CreateDirectory(AppPlatform.DataDirectory);
        var json = JsonSerializer.Serialize(
            entries,
            FavoriteDeviceJsonContext.Default.IReadOnlyDictionaryStringFavoriteDevice);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private static IReadOnlyDictionary<string, FavoriteDevice> Empty() =>
        new Dictionary<string, FavoriteDevice>(StringComparer.Ordinal);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, FavoriteDevice>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, FavoriteDevice>))]
internal sealed partial class FavoriteDeviceJsonContext : JsonSerializerContext;
