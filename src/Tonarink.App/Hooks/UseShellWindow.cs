using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;

sealed record ShellWindowController(Action Restore, Action Hide);

sealed partial class LocalizedAppShell
{
    private ShellWindowController UseShellWindow(
        ReactorWindow? window,
        bool minimizeToTray,
        IntlAccessor t)
    {
        var trayIcon = UseRef<WinUIEx.TrayIcon?>(null);

        UseEffect(() =>
        {
            if (window is null)
                return () => { };

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

        UseEffect(() =>
        {
            ReactorApp.ShutdownPolicy = minimizeToTray
                ? ShutdownPolicy.Explicit
                : ShutdownPolicy.OnLastSurfaceClosed;

            if (!minimizeToTray)
            {
                trayIcon.Current?.Dispose();
                trayIcon.Current = null;
                return () => { };
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (!File.Exists(iconPath))
                iconPath = AppPlatform.ExecutablePath;

            var icon = new WinUIEx.TrayIcon(1, iconPath, t.Message(new("App", "TrayTooltip")));
            icon.Selected += (_, _) => Restore();
            icon.LeftDoubleClick += (_, _) => Restore();
            icon.ContextMenu += (_, args) =>
            {
                var flyout = new MenuFlyout();
                var open = new MenuFlyoutItem { Text = t.Message(new("App", "TrayOpen")) };
                open.Click += (_, _) => Restore();
                var exit = new MenuFlyoutItem { Text = t.Message(new("App", "TrayExit")) };
                exit.Click += (_, _) =>
                {
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
    }
}
