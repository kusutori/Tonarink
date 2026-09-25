using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Tonarink.Services;

union AppNotificationActivation(
    AppNotificationActivation.Open,
    AppNotificationActivation.IncomingAccept,
    AppNotificationActivation.IncomingDecline,
    AppNotificationActivation.OpenFile,
    AppNotificationActivation.ShowInFolder)
{
    public sealed record Open;

    public sealed record IncomingAccept(Guid RequestId);

    public sealed record IncomingDecline(Guid RequestId);

    public sealed record OpenFile(string Path);

    public sealed record ShowInFolder(string Path);
}

static class AppNotificationService
{
    private const string TransferProgressGroup = "transfer-progress";
    private static readonly Lock Gate = new();
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
                return PendingActivations.Any(static activation =>
                    activation is not AppNotificationActivation.Open);
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
                    throw new NotSupportedException(
                        "App notifications are not supported by the current Windows App Runtime configuration.");

                _manager = AppNotificationManager.Default;
                _manager.NotificationInvoked += OnNotificationInvoked;
                _manager.Register();
                _registered = true;
                WriteDiagnostic(
                    $"Registration succeeded. PackageIdentity={AppPlatform.HasPackageIdentity()}; Setting={_manager.Setting}.");

                if (_manager.Setting != AppNotificationSetting.Enabled)
                {
                    WriteDiagnostic(
                        $"Windows notification setting is {_manager.Setting}; notifications may not be displayed.");
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

    public static TransferProgressNotification? StartTransferProgress(
        Guid transferId,
        string title,
        string progressTitle,
        string status,
        long bytesTransferred,
        long totalBytes,
        string valueText,
        string kind)
    {
        AppNotificationManager? manager;
        lock (Gate)
        {
            if (!_enabled || !_registered)
                return null;

            manager = _manager;
        }

        try
        {
            if (manager is null || manager.Setting != AppNotificationSetting.Enabled)
                return null;

            var tag = transferId.ToString("N");
            var progress = new TransferProgressNotification(
                manager,
                tag,
                TransferProgressGroup,
                bytesTransferred,
                totalBytes,
                status);
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddArgument("action", "open")
                .AddArgument("kind", kind)
                .AddProgressBar(new AppNotificationProgressBar()
                    .BindTitle()
                    .BindValue()
                    .BindValueStringOverride()
                    .BindStatus())
                .BuildNotification();
            notification.Tag = tag;
            notification.Group = TransferProgressGroup;
            notification.Progress = progress.CreateInitialData(progressTitle, status, valueText);
            manager.Show(notification);
            return progress;
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Showing transfer progress failed.", exception);
            return null;
        }
    }

    public static bool TryDequeueActivation(out AppNotificationActivation activation)
    {
        lock (Gate)
        {
            if (PendingActivations.Count == 0)
            {
                activation = default;
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
        arguments.TryGetValue("requestId", out var requestIdText);
        arguments.TryGetValue("path", out var path);
        AppNotificationActivation activation = action switch
        {
            "incoming-accept" when Guid.TryParse(requestIdText, out var requestId) =>
                new AppNotificationActivation.IncomingAccept(requestId),
            "incoming-decline" when Guid.TryParse(requestIdText, out var requestId) =>
                new AppNotificationActivation.IncomingDecline(requestId),
            "open-file" when !string.IsNullOrWhiteSpace(path) =>
                new AppNotificationActivation.OpenFile(path),
            "show-in-folder" when !string.IsNullOrWhiteSpace(path) =>
                new AppNotificationActivation.ShowInFolder(path),
            _ => new AppNotificationActivation.Open(),
        };
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

    internal static void WriteDiagnostic(string message, Exception? exception = null)
    {
        AppDiagnostics.Write(
            exception is null ? LogLevel.Information : LogLevel.Warning,
            "notification",
            message,
            exception);
    }
}

sealed class TransferProgressNotification
{
    private static readonly TimeSpan MinimumUpdateInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaximumUpdateInterval = TimeSpan.FromSeconds(2);
    private const double MinimumVisibleProgressDelta = 0.005;

    private readonly Lock _gate = new();
    private readonly AppNotificationManager _manager;
    private readonly string _tag;
    private readonly string _group;
    private DateTimeOffset _lastUpdatedAt = DateTimeOffset.UtcNow;
    private double _lastValue;
    private string _lastStatus;
    private uint _sequenceNumber = 1;
    private Task _updateTail = Task.CompletedTask;
    private bool _removed;

    internal TransferProgressNotification(
        AppNotificationManager manager,
        string tag,
        string group,
        long bytesTransferred,
        long totalBytes,
        string status)
    {
        _manager = manager;
        _tag = tag;
        _group = group;
        _lastValue = Fraction(bytesTransferred, totalBytes);
        _lastStatus = status;
    }

    internal AppNotificationProgressData CreateInitialData(
        string title,
        string status,
        string valueText) => new(_sequenceNumber)
        {
            Title = title,
            Value = _lastValue,
            ValueStringOverride = valueText,
            Status = status,
        };

    public void Report(
        string title,
        string status,
        long bytesTransferred,
        long totalBytes,
        string valueText)
    {
        var value = Fraction(bytesTransferred, totalBytes);
        lock (_gate)
        {
            if (_removed)
                return;

            var now = DateTimeOffset.UtcNow;
            var elapsed = now - _lastUpdatedAt;
            var completed = totalBytes > 0 && bytesTransferred >= totalBytes;
            var statusChanged = !string.Equals(status, _lastStatus, StringComparison.Ordinal);
            if (!completed && !statusChanged && elapsed < MinimumUpdateInterval)
                return;

            if (!completed
                && !statusChanged
                && Math.Abs(value - _lastValue) < MinimumVisibleProgressDelta
                && elapsed < MaximumUpdateInterval)
            {
                return;
            }

            _lastUpdatedAt = now;
            _lastValue = value;
            _lastStatus = status;
            var data = new AppNotificationProgressData(++_sequenceNumber)
            {
                Title = title,
                Value = value,
                ValueStringOverride = valueText,
                Status = status,
            };
            _updateTail = UpdateAfterAsync(_updateTail, data);
        }
    }

    public async Task RemoveAsync()
    {
        Task pendingUpdate;
        lock (_gate)
        {
            if (_removed)
                return;

            _removed = true;
            pendingUpdate = _updateTail;
        }

        try
        {
            await pendingUpdate.ConfigureAwait(false);
            await _manager.RemoveByTagAndGroupAsync(_tag, _group);
        }
        catch (Exception exception)
        {
            AppNotificationService.WriteDiagnostic("Removing transfer progress failed.", exception);
        }
    }

    private async Task UpdateAfterAsync(Task previousUpdate, AppNotificationProgressData data)
    {
        try
        {
            await previousUpdate.ConfigureAwait(false);
            await _manager.UpdateAsync(data, _tag, _group);
        }
        catch (Exception exception)
        {
            AppNotificationService.WriteDiagnostic("Updating transfer progress failed.", exception);
        }
    }

    private static double Fraction(long bytesTransferred, long totalBytes) => totalBytes <= 0
        ? 0
        : Math.Clamp((double)bytesTransferred / totalBytes, 0, 1);
}
