using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Tonarink.Hybrid;

[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class MauiBackgroundReceiveService : Service
{
    private const string ChannelId = "tonarink.background-receive";
    private const int NotificationId = 17041;

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
        StartForeground(NotificationId, CreateNotification());
        return StartCommandResult.Sticky;
    }

    public override Android.OS.IBinder? OnBind(Intent? intent) => null;

    private Notification CreateNotification()
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService)
            ?? throw new InvalidOperationException("Android notification service is unavailable.");
        Notification.Builder builder;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(ChannelId, "后台接收", NotificationImportance.Low)
            {
                Description = "保持 Tonarink 在后台发现设备并接收传输请求。",
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
            .SetContentText("正在后台发现设备并等待接收")
            .SetCategory(Notification.CategoryService)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false);
        if (pendingIntent is not null)
            builder.SetContentIntent(pendingIntent);
        return builder.Build();
    }
}
