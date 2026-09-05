using System.Diagnostics;
using System.Text;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

sealed record AppNotificationActivation(
    string Action,
    string Kind,
    Guid? RequestId,
    string? Path);

static class AppNotificationService
{
    private static readonly object Gate = new();
    private static readonly string LogFilePath = Path.Combine(AppPlatform.DataDirectory, "notifications.log");
    private static AppNotificationManager? _manager;
    private static readonly Queue<AppNotificationActivation> PendingActivations = new();
    private static EventHandler? _activated;
    private static bool _registered;
    private static bool _enabled;
    private static bool _pendingActivation;

    public static bool HasPendingBackgroundAction
    {
        get
        {
            lock (Gate)
                return PendingActivations.Any(static activation => activation.Action != "open");
        }
    }

    public static bool HasPendingActivations
    {
        get
        {
            lock (Gate)
                return PendingActivations.Count > 0;
        }
    }

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
        => TryShowCore(new AppNotificationBuilder()
            .AddText(title)
            .AddText(message)
            .AddArgument("action", "open")
            .AddArgument("kind", kind));

    public static bool ShowIncomingRequest(
        string title,
        string message,
        Guid requestId,
        string acceptText,
        string declineText)
    {
        var requestIdText = requestId.ToString("D");
        return TryShowCore(new AppNotificationBuilder()
            .AddText(title)
            .AddText(message)
            .AddArgument("action", "open")
            .AddArgument("kind", "incoming-request")
            .AddArgument("requestId", requestIdText)
            .AddButton(new AppNotificationButton(acceptText)
                .AddArgument("action", "incoming-accept")
                .AddArgument("kind", "incoming-request")
                .AddArgument("requestId", requestIdText))
            .AddButton(new AppNotificationButton(declineText)
                .AddArgument("action", "incoming-decline")
                .AddArgument("kind", "incoming-request")
                .AddArgument("requestId", requestIdText)));
    }

    public static bool ShowTransferComplete(
        string title,
        string message,
        string kind,
        IEnumerable<string> paths,
        NotificationDefaultAction defaultAction,
        string openFileText,
        string showInFolderText)
    {
        var path = paths.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        if (path is null)
            return TryShow(title, message, kind);

        var defaultActionName = defaultAction == NotificationDefaultAction.ShowInFolder
            ? "show-in-folder"
            : "open-file";
        return TryShowCore(new AppNotificationBuilder()
            .AddText(title)
            .AddText(message)
            .AddArgument("action", defaultActionName)
            .AddArgument("kind", kind)
            .AddArgument("path", path)
            .AddButton(new AppNotificationButton(openFileText)
                .AddArgument("action", "open-file")
                .AddArgument("kind", kind)
                .AddArgument("path", path))
            .AddButton(new AppNotificationButton(showInFolderText)
                .AddArgument("action", "show-in-folder")
                .AddArgument("kind", kind)
                .AddArgument("path", path)));
    }

    public static bool TryDequeueActivation(out AppNotificationActivation? activation)
    {
        lock (Gate)
        {
            if (PendingActivations.Count == 0)
            {
                activation = null;
                return false;
            }

            activation = PendingActivations.Dequeue();
            return true;
        }
    }

    private static bool TryShowCore(AppNotificationBuilder builder)
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

            var notification = builder.BuildNotification();

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
        var arguments = args.Arguments;
        arguments.TryGetValue("action", out var action);
        arguments.TryGetValue("kind", out var kind);
        arguments.TryGetValue("requestId", out var requestIdText);
        arguments.TryGetValue("path", out var path);
        var requestId = Guid.TryParse(requestIdText, out var parsedRequestId)
            ? parsedRequestId
            : (Guid?)null;
        var activation = new AppNotificationActivation(
            string.IsNullOrWhiteSpace(action) ? "open" : action,
            kind ?? string.Empty,
            requestId,
            path);
        lock (Gate)
        {
            PendingActivations.Enqueue(activation);
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
