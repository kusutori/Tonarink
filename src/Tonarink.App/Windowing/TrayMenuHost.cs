using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.Graphics;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Windowing;

// This window only supplies an activated XamlRoot for MenuFlyout. The menu
// remains a XAML popup; it is not rendered inside a tray-panel-sized window.
// Keep it unowned so activating the menu does not raise the main window.
// The shell hook explicitly disposes the host when its lifetime ends.
sealed class TrayMenuHost : IDisposable
{
    private readonly ReactorWindow _window;
    private readonly IntlAccessor _intl;
    private FrameworkElement? _anchor;
    private MenuFlyout? _menu;
    private bool _pendingShow;
    private int _menuEpoch;
    private bool _disposed;

    public TrayMenuHost(IntlAccessor intl)
    {
        _intl = intl;
        _window = ReactorApp.OpenWindow(new WindowSpec
        {
            Key = WindowKey.Of("tray-menu-host"),
            Title = "Tonarink tray menu",
            Width = 1,
            Height = 1,
            MinWidth = 1,
            MinHeight = 1,
            Style = WindowStyle.None,
            ResizeMode = WindowResizeMode.NoResize,
            Level = WindowLevel.AlwaysOnTop,
            ShowInTaskbar = false,
            ShowInSwitcher = false,
            IsMaximizable = false,
            IsMinimizable = false,
            ActivateOnOpen = false,
            ExtendsContentIntoTitleBar = true,
            Opacity = 0,
        }, () => new TrayMenuRoot(this));
        _window.Deactivated += OnDeactivated;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DismissMenu();
        _window.Deactivated -= OnDeactivated;
        if (_anchor is not null)
            _anchor.Loaded -= OnAnchorLoaded;
        _anchor = null;
        _window.Close();
    }

    private void SetAnchor(FrameworkElement anchor)
    {
        if (ReferenceEquals(_anchor, anchor))
            return;
        if (_anchor is not null)
            _anchor.Loaded -= OnAnchorLoaded;
        _anchor = anchor;
        anchor.Loaded += OnAnchorLoaded;
    }

    private void OnAnchorLoaded(object sender, RoutedEventArgs args) =>
        _anchor?.DispatcherQueue.TryEnqueue(TryShowMenu);

    private void OnDeactivated(object? sender, EventArgs args)
    {
        var menu = _menu;
        var epoch = _menuEpoch;
        // Defer light-dismiss until WinUI finishes routing the activation event.
        // A queued dismissal of an old menu must not close a newly opened one.
        _window.NativeWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed && epoch == _menuEpoch && menu is not null && ReferenceEquals(menu, _menu))
                DismissMenu();
        });
    }

    private void ShowMenu()
    {
        if (_disposed || _anchor is null || !GetCursorPos(out var cursor))
            return;

        DismissMenu();
        var theme = AppTheme.ToElementTheme(AppSettingsStore.Load().ThemeIndex);
        _anchor.RequestedTheme = theme;
        _anchor.FlowDirection = _intl.Direction;
        // Reactor owns the menu and its commands. Only popup placement and
        // activation cross the native tray/window boundary here.
        if (_anchor.ContextFlyout is not MenuFlyout menu)
            return;
        menu.ShouldConstrainToRootBounds = false;
        menu.Closed += OnMenuClosed;
        _menu = menu;
        _pendingShow = true;

        // AppWindow coordinates are physical pixels, just like GetCursorPos.
        // The popup uses its destination XamlRoot's DPI and screen constraints.
        _window.AppWindow.MoveAndResize(new RectInt32(cursor.X, cursor.Y, 1, 1));
        _window.Activate();
        _ = SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window.NativeWindow));
        TryShowMenu();
    }

    private void TryShowMenu()
    {
        if (_disposed || !_pendingShow || _menu is not { } menu
            || _anchor?.XamlRoot is not { } xamlRoot || !_window.IsVisible)
            return;

        _pendingShow = false;
        try
        {
            menu.XamlRoot = xamlRoot;
            menu.ShowAt(_anchor, new FlyoutShowOptions
            {
                Position = new Point(0, 0),
                Placement = FlyoutPlacementMode.Auto,
            });
        }
        catch (Exception exception)
        {
            DismissMenu();
            AppDiagnostics.Report("Could not show the tray context menu", exception);
        }
    }

    private void OnMenuClosed(object? sender, object args)
    {
        if (ReferenceEquals(sender, _menu))
            DismissMenu();
    }

    private void DismissMenu()
    {
        _pendingShow = false;
        _menuEpoch++;
        var menu = _menu;
        _menu = null;
        if (menu is not null)
        {
            menu.Closed -= OnMenuClosed;
            menu.Hide();
        }
        _window.Hide();
    }

    private void Restore()
        => Navigate(new());

    private async Task ReportBugAsync()
    {
        try
        {
            AppDiagnostics.Write(LogLevel.Information, "tray", "Opening the issue tracker from the tray menu");
            // Start URI activation on the UI thread while the menu host is
            // still foreground, then dismiss the menu without blocking it.
            var launch = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/kusutori/Tonarink/issues"));
            DismissMenu();
            TrayFlyoutHost.Dismiss();
            if (!await launch)
                AppDiagnostics.Report("Could not open the issue tracker",
                    new InvalidOperationException("Windows could not launch the issue tracker URI."));
        }
        catch (Exception exception)
        {
            AppDiagnostics.Report("Could not open the issue tracker", exception);
        }
    }

    private void Navigate(AppNavigationRequest request)
    {
        DismissMenu();
        TrayFlyoutHost.Dismiss();
        AppNavigationService.Open(request);
    }

    private sealed class TrayMenuRoot(TrayMenuHost host) : Component
    {
        public override Element Render()
        {
            var icon = UseMemo(() => AppPlatform.AppWindowIcon);
            var tray = UseTrayIcon(new TrayIconSpec(icon,
                host._intl.Message(new("App", "TrayTooltip")), WindowKey.Of("main-tray-icon")));
            var open = UseCommand(new Command
            {
                Label = host._intl.Message(new("App", "TrayMenuOpen")),
                Icon = new FontIconData(AppIcons.OpenApp),
                Execute = host.Restore,
            });
            var settings = UseCommand(new Command
            {
                Label = host._intl.Message(new("App", "NavSettings")),
                Icon = new FontIconData(AppIcons.Settings),
                Execute = () => host.Navigate(AppDeepLinks.Resolve("tonarink://settings")),
            });
            var reportBug = UseCommand(new Command
            {
                Label = host._intl.Message(new("App", "TrayReportBug")),
                Icon = new FontIconData(AppIcons.ReportBug),
                // UseCommand dispatches ExecuteAsync on the thread pool;
                // window operations and URI activation must start on the UI thread.
                Execute = () => { _ = host.ReportBugAsync(); },
            });
            var exit = UseCommand(new Command
            {
                Label = host._intl.Message(new("App", "TrayExit")),
                Icon = new FontIconData(AppIcons.Cancel),
                Execute = () =>
                {
                    host.DismissMenu();
                    TrayFlyoutHost.Close();
                    ReactorApp.Exit();
                },
            });
            UseEffect(() =>
            {
                if (tray is null)
                    return () => { };

                void OnClick(object? sender, EventArgs args)
                {
                    host.DismissMenu();
                    if (AppSettingsStore.Load().TrayClickOpensFlyout)
                        TrayFlyoutHost.Toggle();
                    else
                        host.Restore();
                }
                void OnDoubleClick(object? sender, EventArgs args) => host.Restore();
                void OnRightClick(object? sender, EventArgs args) => host.ShowMenu();
                tray.Click += OnClick;
                tray.DoubleClick += OnDoubleClick;
                tray.RightClick += OnRightClick;
                return () =>
                {
                    tray.Click -= OnClick;
                    tray.DoubleClick -= OnDoubleClick;
                    tray.RightClick -= OnRightClick;
                };
            }, tray);

            return Grid(columns: [], rows: [])
                .WithContextFlyout(MenuItems(MenuItem(open), MenuItem(settings), MenuItem(reportBug), MenuSeparator(), MenuItem(exit)))
                .Set(host.SetAnchor);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointInt32 point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
