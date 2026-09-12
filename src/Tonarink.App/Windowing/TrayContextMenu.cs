using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Tonarink.Windowing;

static class TrayContextMenu
{
    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmReturnCmd = 0x0100;
    private const uint WmNull = 0x0000;
    private const nuint OpenCommand = 1;
    private const nuint ExitCommand = 2;

    public static void Show(Window? nativeWindow, string openText, Action onOpen, string exitText, Action onExit)
    {
        if (nativeWindow is null)
            return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MfString, OpenCommand, openText);
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, ExitCommand, exitText);

            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            var command = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmBottomAlign | TpmReturnCmd,
                point.X,
                point.Y,
                hwnd,
                0);
            PostMessage(hwnd, WmNull, 0, 0);

            if (command == (int)OpenCommand)
                onOpen();
            else if (command == (int)ExitCommand)
                onExit();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
