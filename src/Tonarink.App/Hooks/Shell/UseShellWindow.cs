using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;

namespace Tonarink.Hooks.Shell;

sealed record ShellWindowController(Action Restore, Action Hide);

static class ShellWindowHooks
{
    public static ShellWindowController UseShellWindow(
        this RenderContext context,
        ReactorWindow? window,
        bool minimizeToTray,
        IntlAccessor t)
    {
        context.UseEffect(() =>
        {
            if (window is null)
                return () => { };

            // UseClosingGuard cannot distinguish a user close from a programmatic close.
            // Only title-bar/Alt+F4 closes should minimize to the tray; explicit shutdown
            // must continue through the normal Reactor window lifecycle.
            void OnClosing(object? sender, WindowClosingEventArgs args)
            {
                if (args.Reason != WindowCloseReason.UserClosed || !minimizeToTray)
                    return;

                args.Cancel = true;
                Hide();
            }

            window.Closing += OnClosing;
            return () => window.Closing -= OnClosing;
        }, window, minimizeToTray);

        context.UseEffect(() =>
        {
            ReactorApp.ShutdownPolicy = minimizeToTray
                ? ShutdownPolicy.Explicit
                : ShutdownPolicy.OnLastSurfaceClosed;

            if (!minimizeToTray || window is null)
            {
                TrayFlyoutHost.Close();
                return () => { };
            }

            // The host's component owns UseTrayIcon, so closing the auxiliary
            // window also unregisters the icon and detaches its callbacks.
            var menuHost = new TrayMenuHost(window, t);
            return menuHost.Dispose;
        }, window, minimizeToTray, t.Locale);

        return new(Restore, Hide);

        void Restore()
        {
            if (window is null)
                return;

            if (!window.Spec.ShowInTaskbar)
                window.Update(window.Spec with { ShowInTaskbar = true });
            window.Show();
            window.Activate();
        }

        void Hide()
        {
            if (window is null)
                return;

            window.Hide();
            if (window.Spec.ShowInTaskbar)
                window.Update(window.Spec with { ShowInTaskbar = false });
        }
    }
}
