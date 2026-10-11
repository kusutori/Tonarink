using System.Runtime.InteropServices;
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
sealed class TrayMenuHost : IDisposable
{
    private readonly ReactorWindow _window;
    private readonly IntlAccessor _intl;
    private readonly Action _restore;
    private FrameworkElement? _anchor;
    private MenuFlyout? _menu;
    private bool _pendingShow;
    private bool _disposed;

    public TrayMenuHost(ReactorWindow owner, IntlAccessor intl, Action restore)
    {
        _intl = intl;
        _restore = restore;
        _window = ReactorApp.OpenWindow(new WindowSpec
        {
            Key = WindowKey.Of("tray-menu-host"),
            Owner = owner,
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
        // Defer light-dismiss until WinUI finishes routing the activation event.
        // A queued dismissal of an old menu must not close a newly opened one.
        _window.NativeWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed && menu is not null && ReferenceEquals(menu, _menu))
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
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };
        if (TonarinkThemeResources.TrayMenuPresenterStyle(theme) is { } style)
            menu.MenuFlyoutPresenterStyle = style;

        var open = new MenuFlyoutItem { Text = _intl.Message(new("App", "TrayOpen")) };
        open.Click += (_, _) => Restore();
        var exit = new MenuFlyoutItem { Text = _intl.Message(new("App", "TrayExit")) };
        exit.Click += (_, _) =>
        {
            DismissMenu();
            TrayFlyoutHost.Close();
            ReactorApp.Exit();
        };
        menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);
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
    {
        DismissMenu();
        TrayFlyoutHost.Dismiss();
        _restore();
    }

    private sealed class TrayMenuRoot(TrayMenuHost host) : Component
    {
        public override Element Render()
        {
            var icon = UseMemo(() => AppPlatform.AppWindowIcon);
            var tray = UseTrayIcon(new TrayIconSpec(icon,
                host._intl.Message(new("App", "TrayTooltip")), WindowKey.Of("main-tray-icon")));
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

            return Grid(columns: [], rows: []).Set(host.SetAnchor);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointInt32 point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
