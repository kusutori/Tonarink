using System.Collections.Concurrent;
using LocalSendDotNet;
using Tonarink.Utilities;
using Windows.UI.StartScreen;

namespace Tonarink.Services;

enum JumpListActivationKind
{
    Favorite,
    History,
}

sealed record JumpListActivation(JumpListActivationKind Kind, string Value);

sealed record JumpListLabels(
    string FavoritesGroup,
    string HistoryGroup,
    Func<string, string> FavoriteDescription,
    Func<string, string> HistoryDescription);

static class JumpListService
{
    private const int ItemsPerGroup = 5;
    private const string FavoritePrefix = "tonarink-jump:favorite:";
    private const string HistoryPrefix = "tonarink-jump:history:";
    private static readonly string RemovedItemsPath = Path.Combine(
        AppPlatform.DataDirectory,
        "jump-list-removed.txt");
    private static readonly ConcurrentQueue<JumpListActivation> PendingActivations = new();
    private static readonly SemaphoreSlim UpdateGate = new(1, 1);
    private static readonly Lock RemovedItemsGate = new();
    private static HashSet<string>? _removedItems;

    public static bool HasPendingActivations => !PendingActivations.IsEmpty;

    public static bool TryDequeue(out JumpListActivation? activation) =>
        PendingActivations.TryDequeue(out activation);

    public static bool TryEnqueueActivation(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return false;

        if (arguments.StartsWith(FavoritePrefix, StringComparison.Ordinal))
        {
            var fingerprint = arguments[FavoritePrefix.Length..];
            if (fingerprint.Length == 0)
                return false;

            PendingActivations.Enqueue(new(JumpListActivationKind.Favorite, fingerprint));
            return true;
        }

        if (arguments.StartsWith(HistoryPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(arguments[HistoryPrefix.Length..], "N", out var historyId))
        {
            PendingActivations.Enqueue(new(JumpListActivationKind.History, historyId.ToString("N")));
            return true;
        }

        return false;
    }

    public static async Task RefreshAsync(JumpListLabels labels)
    {
        if (!AppPlatform.HasPackageIdentity() || !JumpList.IsSupported())
            return;

        await UpdateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var jumpList = await JumpList.LoadCurrentAsync();
            RememberRemovedItems(jumpList.Items.Where(static item => item.RemovedByUser));
            jumpList.SystemGroupKind = JumpListSystemGroupKind.None;
            jumpList.Items.Clear();

            var favoriteCount = 0;
            foreach (var favorite in FavoriteDeviceStore.Entries.Values
                         .OrderBy(static favorite => favorite.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var arguments = FavoritePrefix + favorite.Fingerprint;
                if (WasRemoved(arguments))
                    continue;

                jumpList.Items.Add(CreateItem(
                    arguments,
                    favorite.Name,
                    labels.FavoritesGroup,
                    labels.FavoriteDescription(favorite.Name),
                    DeviceLogo(favorite.DeviceType ?? LocalSendDeviceType.Desktop)));
                if (++favoriteCount == ItemsPerGroup)
                    break;
            }

            var historyCount = 0;
            foreach (var entry in ReceiveHistoryStore.Entries
                         .Where(static entry => File.Exists(entry.Path) || Directory.Exists(entry.Path)))
            {
                var arguments = HistoryPrefix + entry.Id.ToString("N");
                if (WasRemoved(arguments))
                    continue;

                jumpList.Items.Add(CreateItem(
                    arguments,
                    entry.FileName,
                    labels.HistoryGroup,
                    labels.HistoryDescription(entry.FileName),
                    FileLogo(entry.Path)));
                if (++historyCount == ItemsPerGroup)
                    break;
            }

            await jumpList.SaveAsync();
        }
        catch (Exception exception)
        {
            AppDiagnostics.Report("Could not update the Windows jump list", exception);
        }
        finally
        {
            UpdateGate.Release();
        }
    }

    private static JumpListItem CreateItem(
        string arguments,
        string displayName,
        string groupName,
        string description,
        Uri logo)
    {
        var item = JumpListItem.CreateWithArguments(arguments, displayName);
        item.GroupName = groupName;
        item.Description = description;
        item.Logo = logo;
        return item;
    }

    private static Uri DeviceLogo(LocalSendDeviceType type) =>
        new($"ms-appx:///Assets/JumpList.Device.{type}.png");

    private static Uri FileLogo(string path)
    {
        var kind = Directory.Exists(path)
            ? FileTypeGlyphKind.Folder
            : FileTypeGlyphs.KindForFileName(path);
        return new($"ms-appx:///Assets/JumpList.File.{kind}.png");
    }

    private static void RememberRemovedItems(IEnumerable<JumpListItem> items)
    {
        var arguments = items
            .Select(static item => item.Arguments)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (arguments.Length == 0)
            return;

        lock (RemovedItemsGate)
        {
            var removed = _removedItems ??= LoadRemovedItems();
            var changed = false;
            foreach (var argument in arguments)
                changed |= removed.Add(argument);

            if (!changed)
                return;

            Directory.CreateDirectory(AppPlatform.DataDirectory);
            File.WriteAllLines(RemovedItemsPath, removed.Order(StringComparer.Ordinal));
        }
    }

    private static bool WasRemoved(string arguments)
    {
        lock (RemovedItemsGate)
            return (_removedItems ??= LoadRemovedItems()).Contains(arguments);
    }

    private static HashSet<string> LoadRemovedItems()
    {
        try
        {
            return File.Exists(RemovedItemsPath)
                ? new HashSet<string>(
                    File.ReadAllLines(RemovedItemsPath)
                        .Where(static value => !string.IsNullOrWhiteSpace(value)),
                    StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Report("Could not load removed jump-list items", exception);
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
