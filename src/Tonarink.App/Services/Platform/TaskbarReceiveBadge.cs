using Microsoft.Windows.BadgeNotifications;

namespace Tonarink.Services.Platform;

static class TaskbarReceiveBadge
{
    // Updated only by UI-thread effects. Keep the count while disabled so
    // enabling the setting immediately restores the current selection's badge.
    private static bool _enabled;
    private static uint _count;
    private static uint? _appliedCount;

    public static void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Apply();
    }

    public static void SetCount(uint count)
    {
        _count = count;
        Apply();
    }

    public static void Clear() => SetCount(0);

    private static void Apply()
    {
        if (!AppPlatform.HasPackageIdentity())
            return;

        var count = _enabled ? _count : 0;
        if (_appliedCount == count)
            return;

        try
        {
            var manager = BadgeNotificationManager.Current;
            if (count == 0)
                manager.ClearBadge();
            else
                manager.SetBadgeAsCount(count);
            _appliedCount = count;
        }
        catch (Exception exception)
        {
            // A shell badge failure must not interrupt receiving files.
            AppDiagnostics.Report("Could not update the receive taskbar badge", exception);
        }
    }
}
