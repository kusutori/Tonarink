using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using System.Net.Sockets;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components;

static class DeviceVisuals
{
    public const double AvatarSize = 64;
    public const double OverlayAvatarSize = 88;

    public static Element DeviceAvatar(LocalSendDeviceType type, double size = AvatarSize) =>
        Border(
                Icon(FontIcon(DeviceTypeGlyph(type), fontSize: size * 0.5))
                    .AccessibilityHidden()
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center))
            .Size(size, size)
            .CornerRadius(size / 2)
            .Background(Theme.SubtleFill);

    public static Element DeviceTag(string text) =>
        Border(Caption(text))
            .Padding(horizontal: 8, vertical: 4)
            .CornerRadius(4)
            .Background(Theme.SubtleFill);

    public static string DeviceTypeGlyph(LocalSendDeviceType type) => type switch
    {
        LocalSendDeviceType.Mobile => "\uE8EA",
        LocalSendDeviceType.Web => "\uE12B",
        LocalSendDeviceType.Server => "\uE968",
        LocalSendDeviceType.Headless => "\uE950",
        _ => "\uE977",
    };

    public static string DeviceModel(IntlAccessor t, string? model, LocalSendDeviceType type) =>
        string.IsNullOrWhiteSpace(model)
            ? type switch
            {
                LocalSendDeviceType.Mobile => t.Message(new("App", "DeviceMobile")),
                LocalSendDeviceType.Web => t.Message(new("App", "DeviceWeb")),
                LocalSendDeviceType.Headless => t.Message(new("App", "DeviceHeadless")),
                LocalSendDeviceType.Server => t.Message(new("App", "DeviceServer")),
                _ => t.Message(new("App", "DeviceDesktop")),
            }
            : model;

    public static string LocalDeviceNumber(LocalSendIdentity? identity)
    {
        if (identity is null || identity.Fingerprint.Length < 4)
            return "#—";
        return $"#{Convert.ToInt32(identity.Fingerprint[..4], 16) % 1000}";
    }

    public static string RemoteDeviceNumber(LocalSendDevice device)
    {
        var address = device.PreferredEndpoint?.Address;
        if (address is null)
            return "#—";
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? $"#{address.GetAddressBytes()[^1]}"
            : "#—";
    }
}
