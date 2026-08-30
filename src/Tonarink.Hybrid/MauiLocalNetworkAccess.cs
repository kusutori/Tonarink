namespace Tonarink.Hybrid;

public sealed class MauiLocalNetworkAccess : IDisposable
{
#if ANDROID
    private Android.Net.Wifi.WifiManager.MulticastLock? _multicastLock;
#endif

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var storageStatus = await Permissions.CheckStatusAsync<LegacyStorageWritePermission>();
            if (storageStatus != PermissionStatus.Granted)
                storageStatus = await Permissions.RequestAsync<LegacyStorageWritePermission>();
            if (storageStatus != PermissionStatus.Granted)
                throw new UnauthorizedAccessException("需要存储权限才能把收到的文件保存到下载目录。");
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var status = await Permissions.CheckStatusAsync<NearbyWifiDevicesPermission>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<NearbyWifiDevicesPermission>();
            if (status != PermissionStatus.Granted)
                throw new UnauthorizedAccessException("需要“附近的设备”权限才能发现和连接局域网设备。");
        }

        if (_multicastLock is null)
        {
            var manager = (Android.Net.Wifi.WifiManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.WifiService)
                ?? throw new InvalidOperationException("无法访问 Android Wi-Fi 服务。");
            var multicastLock = manager.CreateMulticastLock("Tonarink.LocalSend")
                ?? throw new InvalidOperationException("无法创建 Android 多播锁。");
            multicastLock.SetReferenceCounted(false);
            multicastLock.Acquire();
            _multicastLock = multicastLock;
        }
#else
        await Task.CompletedTask;
#endif
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
#if ANDROID
        if (_multicastLock?.IsHeld == true)
            _multicastLock.Release();
        _multicastLock?.Dispose();
        _multicastLock = null;
#endif
        GC.SuppressFinalize(this);
    }

#if ANDROID
    private sealed class LegacyStorageWritePermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            [("android.permission.WRITE_EXTERNAL_STORAGE", true)];
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    private sealed class NearbyWifiDevicesPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            [(Android.Manifest.Permission.NearbyWifiDevices, true)];
    }
#endif
}
