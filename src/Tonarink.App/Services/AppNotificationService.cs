using System.Diagnostics;
using System.Text;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

static class AppNotificationService
{
    private static readonly object Gate = new();
    private static readonly string LogFilePath = Path.Combine(AppPlatform.DataDirectory, "notifications.log");
    private static AppNotificationManager? _manager;
    private static EventHandler? _activated;
    private static bool _registered;
    private static bool _enabled;
    private static bool _pendingActivation;

    public static event EventHandler? Activated
    {
        add
        {
            bool notifyPending;
            lock (Gate)
            {
                _activated += value;
                notifyPending = _pendingActivation;
                _pendingActivation = false;
            }

            if (notifyPending)
                value?.Invoke(null, EventArgs.Empty);
        }
        remove
        {
            lock (Gate)
                _activated -= value;
        }
    }

    public static void Initialize(bool enabled) => SetEnabled(enabled);

    public static void SetEnabled(bool enabled)
    {
        Exception? failure = null;
        lock (Gate)
        {
            _enabled = enabled;
            if (!enabled)
            {
                UnregisterCore();
                return;
            }

            if (_registered)
                return;

            try
            {
                if (!AppNotificationManager.IsSupported())
                    throw new NotSupportedException("App notifications are not supported by the current Windows App Runtime configuration.");

                _manager = AppNotificationManager.Default;
                _manager.NotificationInvoked += OnNotificationInvoked;
                _manager.Register();
                _registered = true;
                WriteDiagnostic(
                    $"Registration succeeded. PackageIdentity={AppPlatform.HasPackageIdentity()}; Setting={_manager.Setting}.");

                if (_manager.Setting != AppNotificationSetting.Enabled)
                {
                    WriteDiagnostic($"Windows notification setting is {_manager.Setting}; notifications may not be displayed.");
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                UnregisterCore();
            }
        }

        if (failure is not null)
            WriteDiagnostic("Registration failed.", failure);
    }

    public static void Show(string title, string message, string kind)
        => _ = TryShow(title, message, kind);

    public static bool TryShow(string title, string message, string kind)
    {
        AppNotificationManager? manager;
        lock (Gate)
        {
            if (!_enabled)
                return false;

            if (!_registered)
            {
                WriteDiagnostic("Show skipped because the notification service is not registered.");
                return false;
            }

            manager = _manager;
        }

        try
        {
            if (manager is null)
                return false;

            if (manager.Setting != AppNotificationSetting.Enabled)
            {
                WriteDiagnostic($"Show skipped because the Windows notification setting is {manager.Setting}.");
                return false;
            }

            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .AddArgument("action", "open")
                .AddArgument("kind", kind)
                .BuildNotification();

            manager.Show(notification);
            return true;
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Show failed.", exception);
            return false;
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            _enabled = false;
            UnregisterCore();
        }
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        EventHandler? activated;
        lock (Gate)
        {
            activated = _activated;
            if (activated is null)
                _pendingActivation = true;
        }

        activated?.Invoke(null, EventArgs.Empty);
    }

    private static void UnregisterCore()
    {
        if (_manager is null)
            return;

        try
        {
            _manager.NotificationInvoked -= OnNotificationInvoked;
            if (_registered)
                _manager.Unregister();
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Unregistration failed.", exception);
        }
        finally
        {
            _registered = false;
            _manager = null;
        }
    }

    private static void WriteDiagnostic(string message, Exception? exception = null)
    {
        var text = exception is null
            ? $"[notification] {message}"
            : $"[notification] {message} {exception.GetType().Name}: {exception.Message}";
        Trace.WriteLine(text);

        try
        {
            Directory.CreateDirectory(AppPlatform.DataDirectory);
            File.AppendAllText(
                LogFilePath,
                $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}
