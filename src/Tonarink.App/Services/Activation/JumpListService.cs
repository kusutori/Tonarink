using Windows.UI.StartScreen;

namespace Tonarink.Services.Activation;

sealed record JumpListLabels(
    string FavoritesGroup,
    string HistoryGroup,
    Func<string, string> FavoriteDescription,
    Func<string, string> HistoryDescription);

static class JumpListService
{
    private const int ItemsPerGroup = 5;

    private static readonly string RemovedItemsPath = Path.Combine(
        AppPlatform.DataDirectory,
        "jump-list-removed.txt");

    private static readonly SemaphoreSlim UpdateGate = new(1, 1);
    private static readonly Lock RemovedItemsGate = new();
    private static HashSet<string>? _removedItems;
    private static volatile bool _enabled = true;

    public static void SetEnabled(bool enabled) => _enabled = enabled;

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

            // Read the current preference inside the update gate: refreshes queued
            // before disabling must not repopulate the list after it is cleared.
            if (!_enabled)
            {
                await jumpList.SaveAsync();
                return;
            }

            var favoriteCount = 0;
            foreach (var favorite in FavoriteDeviceStore.Entries.Values
                         .OrderBy(static favorite => favorite.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var arguments = AppDeepLinks.ForFavorite(favorite.Fingerprint);
                if (WasRemoved(arguments))
                    continue;

                jumpList.Items.Add(CreateItem(
                    arguments,
                    favorite.Name,
                    labels.FavoritesGroup,
                    labels.FavoriteDescription(favorite.Name),
                    DeviceIconAssets.ForDeviceType(favorite.DeviceType)));
                if (++favoriteCount == ItemsPerGroup)
                    break;
            }

            var historyCount = 0;
            foreach (var entry in ReceiveHistoryStore.Entries
                         .Where(static entry => File.Exists(entry.Path) || Directory.Exists(entry.Path)))
            {
                var arguments = AppDeepLinks.ForHistory(entry.Id);
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

    private static Uri FileLogo(string path)
    {
        var kind = Directory.Exists(path)
            ? FileTypeGlyphKind.Folder
            : FileTypeGlyphs.KindForFileName(path);
        return new($"ms-appx:///Assets/JumpList/JumpList.File.{kind}.png");
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
            var changed = arguments.Aggregate(false, (current, argument) => current | removed.Add(argument));

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
                ?
                [
                    with(File.ReadAllLines(RemovedItemsPath)
                            .Where(static value => !string.IsNullOrWhiteSpace(value)),
                        StringComparer.Ordinal)
                ]
                : [with(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Report("Could not load removed jump-list items", exception);
            return [with(StringComparer.Ordinal)];
        }
    }
}
