using System.Reflection;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        var trayIcon = context.UseRef<WinUIEx.TrayIcon?>();

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

            if (!minimizeToTray)
            {
                TrayFlyoutHost.Close();
                trayIcon.Current?.Dispose();
                trayIcon.Current = null;
                return () => { };
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (!File.Exists(iconPath))
                iconPath = AppPlatform.ExecutablePath;

            var icon = new WinUIEx.TrayIcon(1, iconPath, t.Message(new("App", "TrayTooltip")));
            icon.Selected += (_, _) =>
            {
                if (AppSettingsStore.Load().TrayClickOpensFlyout)
                {
                    TrayFlyoutHost.Toggle();
                    return;
                }

                TrayFlyoutHost.Dismiss();
                Restore();
            };
            icon.LeftDoubleClick += (_, _) =>
            {
                TrayFlyoutHost.Dismiss();
                Restore();
            };
            icon.ContextMenu += (_, args) =>
            {
                var theme = AppTheme.ToElementTheme(AppSettingsStore.Load().ThemeIndex);
                ApplyTrayHostTheme(icon, theme);
                var flyout = new MenuFlyout();
                if (TonarinkThemeResources.TrayMenuPresenterStyle(theme) is { } presenterStyle)
                    flyout.MenuFlyoutPresenterStyle = presenterStyle;
                var open = new MenuFlyoutItem { Text = t.Message(new("App", "TrayOpen")) };
                open.Click += (_, _) =>
                {
                    TrayFlyoutHost.Dismiss();
                    Restore();
                };
                var exit = new MenuFlyoutItem { Text = t.Message(new("App", "TrayExit")) };
                exit.Click += (_, _) =>
                {
                    TrayFlyoutHost.Close();
                    icon.Dispose();
                    trayIcon.Current = null;
                    ReactorApp.Exit();
                };
                flyout.Items.Add(open);
                flyout.Items.Add(new MenuFlyoutSeparator());
                flyout.Items.Add(exit);
                args.Flyout = flyout;
            };
            icon.IsVisible = true;
            trayIcon.Current = icon;

            return () =>
            {
                icon.Dispose();
                if (ReferenceEquals(trayIcon.Current, icon))
                    trayIcon.Current = null;
            };
        }, minimizeToTray, t.Locale);

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

        static void ApplyTrayHostTheme(WinUIEx.TrayIcon tray, ElementTheme theme)
        {
            try
            {
                if (typeof(WinUIEx.TrayIcon)
                        .GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(tray) is not Window host)
                    return;

                if (host.Content is FrameworkElement content)
                    content.RequestedTheme = theme;
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report("Could not apply the tray menu host theme", exception);
            }
        }
    }
}
