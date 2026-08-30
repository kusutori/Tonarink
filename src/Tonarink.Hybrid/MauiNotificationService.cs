namespace Tonarink.Hybrid;

internal static class MauiNotificationService
{
    public static async Task EnsureBackgroundPermissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var status = await Permissions.CheckStatusAsync<NotificationPermission>();
            if (status != PermissionStatus.Granted)
                await Permissions.RequestAsync<NotificationPermission>();
        }
#else
        await Task.CompletedTask;
#endif
    }

    public static async Task NotifyAsync(string title, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var status = await Permissions.CheckStatusAsync<NotificationPermission>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<NotificationPermission>();
            if (status != PermissionStatus.Granted)
                return;
        }
        const string channelId = "tonarink.transfers";
        var context = Android.App.Application.Context;
        var manager = (Android.App.NotificationManager?)context.GetSystemService(Android.Content.Context.NotificationService);
        if (manager is null)
            return;
        Android.App.Notification.Builder builder;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            manager.CreateNotificationChannel(new Android.App.NotificationChannel(channelId, "传输", Android.App.NotificationImportance.Default));
            builder = new Android.App.Notification.Builder(context, channelId);
        }
        else
        {
            builder = new Android.App.Notification.Builder(context);
        }
        var notification = builder
            .SetSmallIcon(Android.Resource.Drawable.StatSysDownloadDone)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetAutoCancel(true)
            .Build();
        manager.Notify(Random.Shared.Next(1, int.MaxValue), notification);
#elif IOS || MACCATALYST
        var center = UserNotifications.UNUserNotificationCenter.Current;
        var settings = await center.GetNotificationSettingsAsync();
        if (settings.AuthorizationStatus == UserNotifications.UNAuthorizationStatus.NotDetermined)
            await center.RequestAuthorizationAsync(UserNotifications.UNAuthorizationOptions.Alert | UserNotifications.UNAuthorizationOptions.Sound);
        var content = new UserNotifications.UNMutableNotificationContent { Title = title, Body = message };
        var request = UserNotifications.UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString("N"), content, null);
        await center.AddNotificationRequestAsync(request);
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    private sealed class NotificationPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            [(Android.Manifest.Permission.PostNotifications, true)];
    }
#endif
}
