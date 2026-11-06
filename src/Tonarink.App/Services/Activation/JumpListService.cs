using System.Runtime.InteropServices;
using Microsoft.UI.Reactor;

namespace Tonarink.Services.Activation;

sealed record JumpListLabels(
    string FavoritesGroup,
    string HistoryGroup,
    Func<string, string> FavoriteDescription,
    Func<string, string> HistoryDescription);

static partial class JumpListService
{
    private const int ItemsPerGroup = 5;
    private static readonly SemaphoreSlim UpdateGate = new(1, 1);
    private static volatile bool _enabled = true;

    public static void Initialize()
    {
        if (AppPlatform.HasPackageIdentity())
            return;

        var appId = typeof(AppPlatform).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The application assembly name is unavailable.");
        JumpList.AppUserModelId = appId;
        try
        {
            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(appId));
        }
        catch (COMException exception)
        {
            AppDiagnostics.Report("Could not set the Windows jump-list identity", exception);
        }
    }

    public static void SetEnabled(bool enabled) => _enabled = enabled;

    public static async Task RefreshAsync(JumpListLabels labels)
    {
        await UpdateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await AppNavigationService.OnUIAsync(() => PublishAsync(labels), CancellationToken.None)
                .ConfigureAwait(false);
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

    private static async Task PublishAsync(JumpListLabels labels)
    {
        JumpList.ShowRecent = false;
        JumpList.ShowFrequent = false;

        // Observe the preference inside the gate, after dispatching to the UI:
        // refreshes queued before disabling must not repopulate the list.
        if (!_enabled)
        {
            ApplyRemovedArguments(await JumpList.ClearAsync());
            return;
        }

        List<JumpListItem> items = [];
        foreach (var favorite in FavoriteDeviceStore.Entries.Values
                     .Where(static favorite => !favorite.JumpListRemoved)
                     .OrderBy(static favorite => favorite.Name, StringComparer.CurrentCultureIgnoreCase)
                     .Take(ItemsPerGroup))
        {
            items.Add(JumpListItem.ForUri(
                favorite.Name,
                AppDeepLinks.ForFavorite(favorite.Fingerprint),
                labels.FavoriteDescription(favorite.Name),
                ItemIcon(DeviceIconAssets.ForDeviceType(favorite.DeviceType)),
                labels.FavoritesGroup));
        }

        foreach (var entry in ReceiveHistoryStore.Entries
                     .Where(static entry => !entry.JumpListRemoved &&
                         (File.Exists(entry.Path) || Directory.Exists(entry.Path)))
                     .Take(ItemsPerGroup))
        {
            items.Add(JumpListItem.ForUri(
                entry.FileName,
                AppDeepLinks.ForHistory(entry.Id),
                labels.HistoryDescription(entry.FileName),
                ItemIcon(FileLogo(entry.Path)),
                labels.HistoryGroup));
        }

        // Windows consumes removal records when the transaction commits.
        // Track the returned observations on existing destinations, not in a
        // permanent blacklist. Editing a favorite or receiving a new file
        // creates fresh user activity eligible for the list.
        ApplyRemovedArguments(await JumpList.UpdateAsync(items));
    }

    private static void ApplyRemovedArguments(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
            return;

        var removed = new HashSet<string>(arguments, StringComparer.Ordinal);
        FavoriteDeviceStore.HideFromJumpList(FavoriteDeviceStore.Entries.Values
            .Where(favorite => removed.Contains(AppDeepLinks.ForFavorite(favorite.Fingerprint)))
            .Select(static favorite => favorite.Fingerprint)
            .ToHashSet(StringComparer.Ordinal));
        ReceiveHistoryStore.HideFromJumpList(ReceiveHistoryStore.Entries
            .Where(entry => removed.Contains(AppDeepLinks.ForHistory(entry.Id)))
            .Select(static entry => entry.Id)
            .ToHashSet());
    }

    private static WindowIcon ItemIcon(Uri logo) => AppPlatform.HasPackageIdentity()
        ? WindowIcon.FromResource(logo.AbsoluteUri)
        : AppPlatform.AppWindowIcon;

    private static Uri FileLogo(string path)
    {
        var kind = Directory.Exists(path)
            ? FileTypeGlyphKind.Folder
            : FileTypeGlyphs.KindForFileName(path);
        return new($"ms-appx:///Assets/JumpList/JumpList.File.{kind}.png");
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);
}
