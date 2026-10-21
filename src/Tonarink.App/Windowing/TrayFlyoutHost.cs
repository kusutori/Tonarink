using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace Tonarink.Windowing;

static partial class TrayFlyoutHost
{
    public static readonly WindowKey Key = WindowKey.Of("tray-flyout");

    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmNclButtonDown = 0x00A1;
    private const int GaRoot = 2;
    private const int GaRootOwner = 3;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const double AnimationOffsetDip = 20;
    private static readonly TimeSpan ShowAnimationDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan HideAnimationDuration = TimeSpan.FromMilliseconds(300);

    private static readonly LowLevelMouseProc MouseProc = OnMouse;
    private static DateTime _hiddenUtc;
    private static DateTime _shownUtc;
    private static bool _dismissQueued;
    private static nint _mouseHook;
    private static nint _flyoutHwnd;
    private static DispatcherQueueTimer? _pendingShowAnimation;

    public static bool IsOpen => TryFind() is { IsVisible: true };

    public static bool IsPinned { get; private set; }

    public static void SetPinned(bool pinned) => IsPinned = pinned;

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
        StopPendingShowAnimation();
        StopClickAwayWatch();
        IsPinned = false;
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
        var animate = AnimationsEnabled();
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
            StopPendingShowAnimation();
            StopClickAwayWatch();
            IsPinned = false;
            _hiddenUtc = DateTime.UtcNow;
        };
        Show(window, animate);
    }

    private static void Show(ReactorWindow window, bool? animate = null)
    {
        StopPendingShowAnimation();
        var (x, y) = BottomRight();
        var shouldAnimate = animate ?? AnimationsEnabled();
        window.Hide();
        window.SetPosition(x, y);
        if (!shouldAnimate || !TryStartShowAnimation(window))
        {
            SetPanelOpacity(window, 1);
            window.Show();
        }

        FocusFlyout(window);
        _shownUtc = DateTime.UtcNow;
        StartClickAwayWatch(window);
    }

    private static void Dismiss(ReactorWindow window)
    {
        StopPendingShowAnimation();
        StopClickAwayWatch();
        _hiddenUtc = DateTime.UtcNow;
        if (window.IsVisible && (!AnimationsEnabled() || !TryStartHideAnimation(window)))
            window.Hide();
    }

    private static bool TryStartShowAnimation(ReactorWindow window)
    {
        var native = window.NativeWindow;
        var queue = native?.DispatcherQueue;
        if (native is null || queue is null)
            return false;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        if (hwnd == 0 || !GetWindowRect(hwnd, out var finalBounds))
            return false;

        var offset = AnimationOffset(hwnd);
        if (!MoveWindow(hwnd, finalBounds.Left, finalBounds.Top + offset))
            return false;

        SetPanelOpacity(window, 0);
        window.Show();
        // Give XAML one frame to create its surface before the compositor fades the content in.
        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(32);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(_pendingShowAnimation, timer))
                _pendingShowAnimation = null;
            if (!window.IsVisible || !IsWindow(hwnd))
                return;

            StartPanelOpacityAnimation(window, 0, 1, ShowAnimationDuration, entering: true);
            StartWindowAnimation(
                hwnd,
                finalBounds.Left,
                finalBounds.Top + offset,
                finalBounds.Top,
                ShowAnimationDuration,
                entering: true);
        };
        _pendingShowAnimation = timer;
        timer.Start();
        return true;
    }

    private static bool TryStartHideAnimation(ReactorWindow window)
    {
        var native = window.NativeWindow;
        if (native is null)
            return false;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        if (hwnd == 0 || !GetWindowRect(hwnd, out var bounds))
            return false;

        var offset = AnimationOffset(hwnd);
        StartPanelOpacityAnimation(window, 1, 0, HideAnimationDuration, entering: false);
        StartWindowAnimation(
            hwnd,
            bounds.Left,
            bounds.Top,
            bounds.Top + offset,
            HideAnimationDuration,
            entering: false);
        window.Hide();
        SetPanelOpacity(window, 1);
        return true;
    }

    private static int AnimationOffset(nint hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1 : dpi / 96d;
        return (int)Math.Round(AnimationOffsetDip * scale);
    }

    private static void StartPanelOpacityAnimation(
        ReactorWindow window,
        float from,
        float to,
        TimeSpan duration,
        bool entering)
    {
        if (TryGetPanelVisual(window) is not { } visual)
            return;

        visual.StopAnimation(nameof(visual.Opacity));
        visual.Opacity = from;

        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(
            1,
            to,
            visual.Compositor.CreateCubicBezierEasingFunction(entering
                ? new Vector2(0.25f, 0.46f)
                : new Vector2(0.55f, 0.085f),
                entering
                    ? new Vector2(0.45f, 0.94f)
                    : new Vector2(0.68f, 0.53f)));
        visual.StartAnimation(nameof(visual.Opacity), animation);
    }

    private static void SetPanelOpacity(ReactorWindow window, float opacity)
    {
        if (TryGetPanelVisual(window) is not { } visual)
            return;

        visual.StopAnimation(nameof(visual.Opacity));
        visual.Opacity = opacity;
    }

    private static Visual? TryGetPanelVisual(ReactorWindow window) =>
        window.NativeWindow?.Content is UIElement content
            ? ElementCompositionPreview.GetElementVisual(content)
            : null;

    private static void StartWindowAnimation(
        nint hwnd,
        int x,
        int startY,
        int finalY,
        TimeSpan duration,
        bool entering)
    {
        AnimateWindowPosition(hwnd, x, startY, finalY, duration, entering);
    }

    private static void AnimateWindowPosition(
        nint hwnd,
        int x,
        int startY,
        int finalY,
        TimeSpan duration,
        bool entering)
    {
        var started = Stopwatch.GetTimestamp();
        while (IsWindow(hwnd))
        {
            var progress = Math.Clamp(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds / duration.TotalMilliseconds,
                0,
                1);
            var eased = Ease(progress, entering);
            var y = startY + (int)Math.Round((finalY - startY) * eased);
            MoveWindow(hwnd, x, y);

            if (progress >= 1)
                return;

            // Synchronize HWND movement with the desktop compositor instead of a UI-thread timer.
            if (DwmFlush() < 0)
                Thread.Sleep(1);
        }
    }

    private static double Ease(double progress, bool entering) => entering
        ? 1 - (1 - progress) * (1 - progress)
        : progress * progress;

    private static void StopPendingShowAnimation()
    {
        _pendingShowAnimation?.Stop();
        _pendingShowAnimation = null;
    }

    private static bool MoveWindow(nint hwnd, int x, int y) =>
        SetWindowPos(
            hwnd,
            0,
            x,
            y,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);

    private static bool AnimationsEnabled()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Report("Could not read the Windows animation preference", exception);
            return false;
        }
    }

    private static void OnDeactivated(object? sender, EventArgs e)
    {
        if (sender is not ReactorWindow window || !window.IsVisible)
            return;
        if (IsPinned)
            return;
        if (DateTime.UtcNow - _shownUtc < TimeSpan.FromMilliseconds(250))
            return;

        Dismiss(window);
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
            if (!IsPinned && window.IsVisible)
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
        _mouseHook = SetWindowsHookExW(WhMouseLl, MouseProc, GetModuleHandleW(null), 0);
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
            && !IsPinned
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
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

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowsHookExW(
        int idHook, [MarshalAs(UnmanagedType.FunctionPtr)] LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? lpModuleName);

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hWnd, int gaFlags);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hWnd);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmFlush();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
