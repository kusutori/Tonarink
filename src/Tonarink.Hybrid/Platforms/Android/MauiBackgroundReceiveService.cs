using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Tonarink.Application;

namespace Tonarink.Hybrid;

[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class MauiBackgroundReceiveService : Service
{
    private const string ChannelId = "tonarink.background-receive";
    private const int NotificationId = 17041;
    private TonarinkAppState? _state;
    private string? _notificationLocale;

    public static void Start()
    {
        var context = Android.App.Application.Context;
        var intent = new Intent(context, typeof(MauiBackgroundReceiveService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (_state is null)
        {
            _state = IPlatformApplication.Current?.Services.GetService<TonarinkAppState>();
            if (_state is not null) _state.Changed += OnStateChanged;
        }
        _notificationLocale = MauiLanguage.Locale;
        StartForeground(NotificationId, CreateNotification());
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        if (_state is not null) _state.Changed -= OnStateChanged;
        _state = null;
        base.OnDestroy();
    }

    private void OnStateChanged()
    {
        var locale = MauiLanguage.Locale;
        if (locale == _notificationLocale) return;
        _notificationLocale = locale;
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.Notify(NotificationId, CreateNotification());
    }

    public override Android.OS.IBinder? OnBind(Intent? intent) => null;

    private Notification CreateNotification()
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService)
            ?? throw new InvalidOperationException(MauiLanguage.Text("NotificationServiceUnavailable"));
        Notification.Builder builder;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(ChannelId, MauiLanguage.Text("BackgroundReceive"), NotificationImportance.Low)
            {
                Description = MauiLanguage.Text("BackgroundReceiveDescription"),
            };
            channel.SetShowBadge(false);
            manager.CreateNotificationChannel(channel);
            builder = new Notification.Builder(this, ChannelId);
        }
        else
        {
            builder = new Notification.Builder(this);
        }

        var launchIntent = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
        var pendingIntent = launchIntent is null
            ? null
            : PendingIntent.GetActivity(this, 0, launchIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        builder
            .SetSmallIcon(Android.Resource.Drawable.StatSysDownload)
            .SetContentTitle("Tonarink")
            .SetContentText(MauiLanguage.Text("BackgroundReceiveStatus"))
            .SetCategory(Notification.CategoryService)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false);
        if (pendingIntent is not null)
            builder.SetContentIntent(pendingIntent);
        return builder.Build();
    }
}
