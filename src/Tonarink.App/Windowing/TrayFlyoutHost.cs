using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Tonarink.Windowing;

static class TrayFlyoutHost
{
    public static readonly WindowKey Key = WindowKey.Of("tray-flyout");

    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmNclButtonDown = 0x00A1;
    private const int GaRoot = 2;
    private const int GaRootOwner = 3;

    private static readonly LowLevelMouseProc MouseProc = OnMouse;
    private static DateTime _hiddenUtc;
    private static DateTime _shownUtc;
    private static bool _dismissQueued;
    private static nint _mouseHook;
    private static nint _flyoutHwnd;

    public static bool IsOpen => TryFind() is { IsVisible: true };

    public static void Toggle()
    {
        if (TryFind() is { } existing)
        {
            if (existing.IsVisible)
            {
                Dismiss(existing);
                return;
            }

            if (DateTime.UtcNow - _hiddenUtc < TimeSpan.FromMilliseconds(400))
                return;

            Show(existing);
            return;
        }

        if (DateTime.UtcNow - _hiddenUtc < TimeSpan.FromMilliseconds(400))
            return;

        Open();
    }

    public static void Dismiss()
    {
        if (TryFind() is { IsVisible: true } window)
            Dismiss(window);
    }

    public static void Close()
    {
        StopClickAwayWatch();
        TryFind()?.Close();
    }

    public static void Open()
    {
        if (TryFind() is { } existing)
        {
            Show(existing);
            return;
        }

        var (x, y) = BottomRight();
        var window = ReactorApp.OpenWindow(
            new WindowSpec
            {
                Title = "Tonarink",
                Width = AppLayout.TrayFlyoutWidth,
                Height = AppLayout.TrayFlyoutHeight,
                MinWidth = AppLayout.TrayFlyoutWidth,
                MinHeight = 320,
                MaxWidth = AppLayout.TrayFlyoutWidth,
                Style = WindowStyle.None,
                Level = WindowLevel.AlwaysOnTop,
                CornerStyle = WindowCornerStyle.Rounded,
                ResizeMode = WindowResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowInSwitcher = false,
                IsMaximizable = false,
                IsMinimizable = false,
                IsMovableByBackground = false,
                ActivateOnOpen = false,
                StartPosition = WindowStartPosition.Manual,
                ManualPosition = (x, y),
                Key = Key,
                Backdrop = BackdropChoice.Of(BackdropKind.Mica),
                ExtendsContentIntoTitleBar = true,
            },
            static () => new TrayFlyoutRoot());
        window.Deactivated += OnDeactivated;
        window.Closed += (_, _) =>
        {
            StopClickAwayWatch();
            _hiddenUtc = DateTime.UtcNow;
        };
        Show(window);
    }

    private static void Show(ReactorWindow window)
    {
        var (x, y) = BottomRight();
        window.SetPosition(x, y);
        window.Show();
        FocusFlyout(window);
        _shownUtc = DateTime.UtcNow;
        StartClickAwayWatch(window);
    }

    private static void Dismiss(ReactorWindow window)
    {
        StopClickAwayWatch();
        _hiddenUtc = DateTime.UtcNow;
        if (window.IsVisible)
            window.Hide();
    }

    private static void OnDeactivated(object? sender, EventArgs e)
    {
        if (sender is not ReactorWindow window || !window.IsVisible)
            return;
        if (DateTime.UtcNow - _shownUtc < TimeSpan.FromMilliseconds(250))
            return;

        QueueDismiss(window);
    }

    private static void QueueDismiss(ReactorWindow window)
    {
        if (_dismissQueued)
            return;

        var queue = DispatcherQueue.GetForCurrentThread()
                    ?? window.NativeWindow?.DispatcherQueue;
        if (queue is null)
        {
            Dismiss(window);
            return;
        }

        _dismissQueued = true;
        queue.TryEnqueue(() =>
        {
            _dismissQueued = false;
            if (window.IsVisible)
                Dismiss(window);
        });
    }

    private static void FocusFlyout(ReactorWindow window)
    {
        var native = window.NativeWindow;
        if (native is null)
            return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        window.Activate();
        native.Activate();
        ForceSetForeground(hwnd);
    }

    private static void ForceSetForeground(nint hwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
            return;

        var currentThread = GetCurrentThreadId();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0
                       && foregroundThread != currentThread
                       && AttachThreadInput(foregroundThread, currentThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(foregroundThread, currentThread, false);
        }
    }

    private static void StartClickAwayWatch(ReactorWindow window)
    {
        StopClickAwayWatch();
        var native = window.NativeWindow;
        if (native is null)
            return;

        _flyoutHwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        _mouseHook = SetWindowsHookEx(WhMouseLl, MouseProc, GetModuleHandle(null), 0);
    }

    private static void StopClickAwayWatch()
    {
        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }

        _flyoutHwnd = 0;
    }

    private static nint OnMouse(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0
            && _flyoutHwnd != 0
            && DateTime.UtcNow - _shownUtc >= TimeSpan.FromMilliseconds(250)
            && wParam is (nint)WmLButtonDown or (nint)WmRButtonDown or (nint)WmNclButtonDown)
        {
            var info = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            var target = WindowFromPoint(info.Point);
            if (!IsUnderFlyout(target) && TryFind() is { IsVisible: true } window)
                QueueDismiss(window);
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static bool IsUnderFlyout(nint hwnd) =>
        hwnd != 0
        && (hwnd == _flyoutHwnd
            || GetAncestor(hwnd, GaRoot) == _flyoutHwnd
            || GetAncestor(hwnd, GaRootOwner) == _flyoutHwnd);

    private static ReactorWindow? TryFind()
    {
        foreach (var window in ReactorApp.Windows)
        {
            if (window.Key is { } key && key.Equals(Key))
                return window;
        }

        return null;
    }

    private static (double X, double Y) BottomRight()
    {
        var work = ReactorDisplay.Primary.WorkAreaDip;
        return (
            work.X + work.Width - AppLayout.TrayFlyoutWidth - AppLayout.TrayFlyoutGap,
            work.Y + work.Height - AppLayout.TrayFlyoutHeight - AppLayout.TrayFlyoutGap);
    }

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public NativePoint Point;
        public int MouseData;
        public int Flags;
        public int Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hWnd, int gaFlags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
