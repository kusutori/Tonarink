using LocalSendDotNet;

namespace Tonarink.Utilities;

static class DeviceIconAssets
{
    // Share contacts and jump-list favorites use the same packaged device icons.
    // Older favorites may not have a device type; unknown values also fall back
    // to Desktop rather than referencing an asset that does not exist.
    public static Uri ForDeviceType(LocalSendDeviceType? type)
    {
        var deviceType = type is { } value && Enum.IsDefined(value)
            ? value
            : LocalSendDeviceType.Desktop;
        return new Uri($"ms-appx:///Assets/JumpList/JumpList.Device.{deviceType}.png");
    }
}
