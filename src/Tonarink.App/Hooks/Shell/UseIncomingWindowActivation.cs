using Microsoft.UI.Reactor.Core;

namespace Tonarink.Hooks.Shell;

static class IncomingWindowActivationHooks
{
    public static void UseIncomingWindowActivation(
        this RenderContext context,
        int requestCount,
        bool notificationsEnabled,
        bool receiveInBackground,
        bool hasReceiveClient,
        Action restoreWindow)
    {
        context.UseEffect(() =>
        {
            // Require notifications for CLI and persisted preferences too, not
            // just for the disabled toggle shown by the Settings page.
            if (requestCount > 0 && !hasReceiveClient && !(notificationsEnabled && receiveInBackground))
                restoreWindow();
        }, requestCount, notificationsEnabled, receiveInBackground, hasReceiveClient);
    }
}
