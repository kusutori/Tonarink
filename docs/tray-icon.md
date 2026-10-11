# Tray icon and context menu (Reactor / WinUI 3)

## Current implementation

The App uses Reactor's `UseTrayIcon` for the notification-area icon and its
left-click, double-click, and right-click events. WinUIEx is no longer a dependency.

The right-click menu is a native XAML `MenuFlyout`, hosted by a 1×1 auxiliary
Reactor window. This supplies the `XamlRoot` without rendering the menu inside
a large custom flyout window.

- `Hooks/Shell/UseShellWindow.cs` owns the auxiliary host while minimize-to-tray
  is enabled, and disposes it when disabled, when the locale changes, or on unmount.
- `Windowing/TrayMenuHost.cs` creates a hidden, transparent, borderless window
  that is owned by the main window and absent from the taskbar and Alt+Tab.
- Its root component calls `UseTrayIcon`. Closing the host unmounts that component,
  detaches the callbacks, and unregisters the icon through the hook's cleanup.
- The same component builds the Open / Settings / Exit menu using `UseCommand`,
  `MenuItem`, and `MenuItems`, attached with `WithContextFlyout`. Reactor owns
  the menu and its command bindings; the host only opens the resulting native popup.
- On right-click, the host is positioned at the cursor in physical pixels and
  activated. The menu waits for a valid `XamlRoot`, uses its rasterization scale,
  and sets `ShouldConstrainToRootBounds = false` so it is not clipped to 1×1.
- Menu closure hides the host; deactivation light-dismisses the current menu.
  A deferred dismissal from an older menu cannot dismiss a newer invocation.
- The root and presenter follow the current App theme. High contrast keeps
  Windows' native brushes. Menu labels and text direction use the App's locale.

The left-click tray panel remains separate in `Windowing/TrayFlyoutHost.cs` and
`Components/Tray/TrayFlyoutPanel.cs`. It retains its existing size, pinning,
animations, and click-away behavior.

## Why not `ReactorTrayIcon.ShowFlyout(MenuItems(...))`?

`MenuItems(...)` is flyout content for a control's context-flyout slot, not an
independently mountable visual root. Reactor's generic tray flyout also uses a
larger window for custom content. Neither is needed for this three-item context menu:
`MenuFlyout.ShowAt` on a minimal XAML host supplies the Fluent popup itself.

Open and Settings dispatch typed requests to `AppNavigationService`, the same
navigator used by CLI and deep links. Internal clicks do not launch their own
protocol through Windows. Exit remains a local command, not a public deep link.

Earlier implementations used Win32 `TrackPopupMenuEx` (classic shell appearance)
and then WinUIEx `TrayIcon` (which supplied its own XAML host). The current host
keeps the Fluent menu without that extra package or reflection into a private window.

## Shared app rules

- Intercept `WindowCloseReason.UserClosed` and `Hide()` when "minimize to tray"
  is on. Restore with `Show()` + `Activate()`, and hide from the taskbar while
  in the tray.
- Incoming transfers / share-target activations should restore the window.
- Persist the setting; login-start can pass `--minimized` so the first paint
  goes straight to the tray.

- Use `ShutdownPolicy.Explicit` while minimize-to-tray is enabled. Disabling the
  setting disposes the tray host and returns to normal last-surface shutdown.
- The menu's Exit action dismisses the menu, closes the tray panel, and calls
  `ReactorApp.Exit()` so auxiliary windows and icons are also cleaned up.

This migration is compile-verified only; interactive tray and final Native AOT/MSIX
checks were not run as part of the change.
