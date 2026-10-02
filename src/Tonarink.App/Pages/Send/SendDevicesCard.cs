using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Pages.Send.SendPageVisuals;

namespace Tonarink.Pages.Send;

sealed record SendDevicesCardProps(
    AppRuntimeState Runtime,
    IReadOnlyDictionary<string, FavoriteDevice> Favorites,
    bool IsWideLayout,
    bool IsSending,
    bool IsResolvingAddress,
    bool IsAddressDialogOpen,
    bool IsFavoritesDialogOpen,
    bool KeepItemsForMultipleReceivers,
    Action<bool> SetKeepItemsForMultipleReceivers,
    Element SearchingAnimation,
    Func<Task> RefreshAsync,
    Action OpenAddress,
    Action OpenFavorites,
    Action OpenWebShare,
    Action<LocalSendDevice, FrameworkElement?> SelectDevice,
    Action<LocalSendDevice> OpenDetails,
    Action<LocalSendDevice, FavoriteDevice?> ChangeFavorite,
    Action<LocalSendDevice> VerifyDevice);

sealed class SendDevicesCard : Component<SendDevicesCardProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var devices = Props.Runtime.Devices;
        Element deviceBody = devices switch
        {
            [] => EmptyDevices(
                    t,
                    Props.Runtime.NodeState,
                    Props.Runtime.DiscoveryWarning,
                    Props.SearchingAnimation)
                .VAlign(VerticalAlignment.Stretch),
            _ => VStack(8,
            [
                .. devices.Select((device, index) =>
                {
                    var favorite = Props.Favorites.GetValueOrDefault(device.Fingerprint);
                    return DeviceCard(
                            device,
                            favorite,
                            isEnabled: Props.Runtime.NodeState == LocalSendNodeState.Running
                                       && !Props.IsSending,
                            onClick: source => Props.SelectDevice(device, source),
                            onDetails: () => Props.OpenDetails(device),
                            onFavorite: () => Props.ChangeFavorite(device, favorite),
                            onVerify: () => Props.VerifyDevice(device),
                            t)
                        .PositionInSet(index + 1, devices.Count)
                        .WithKey(device.Fingerprint);
                })
            ]),
        };

        Element deviceContent = Props.IsWideLayout
            ? ScrollView(deviceBody)
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .VerticalContentAlignment(VerticalAlignment.Stretch)
                .Flex(grow: 1, basis: 0)
            : deviceBody;
        var canUseDeviceActions = !Props.IsSending
                                  && !Props.IsResolvingAddress
                                  && Props.Runtime.NodeState == LocalSendNodeState.Running;
        var card = Card(
                FlexColumn(
                        FlexRow(
                                BodyStrong(t.Message(new("App", "NearbyDevices")))
                                    .Flex(grow: 1, basis: 0),
                                AnimatedButtons.Refresh(
                                    t.Message(new("App", "RefreshDevices")),
                                    () => _ = Props.RefreshAsync(),
                                    isEnabled: !Props.IsSending),
                                AnimatedButtons.AddressTarget(
                                    t.Message(new("App", "SendToAddress")),
                                    Props.OpenAddress,
                                    isEnabled: canUseDeviceActions,
                                    isDialogOpen: Props.IsAddressDialogOpen),
                                AnimatedButtons.Favorites(
                                    t.Message(new("App", "FavoritesTitle")),
                                    Props.OpenFavorites,
                                    isEnabled: canUseDeviceActions,
                                    isDialogOpen: Props.IsFavoritesDialogOpen),
                                AnimatedButtons.Link(
                                    t.Message(new("App", "WebShareTitle")),
                                    Props.OpenWebShare,
                                    isEnabled: !Props.IsSending
                                               && Props.Runtime.NodeState == LocalSendNodeState.Running),
                                AnimatedButtons.MultipleReceivers(
                                    Props.KeepItemsForMultipleReceivers,
                                    Props.SetKeepItemsForMultipleReceivers,
                                    t.Message(new("App", "MultipleReceivers")),
                                    t.Message(new("App", "MultipleReceiversDescription")),
                                    isEnabled: !Props.IsSending)) with
                        {
                            AlignItems = FlexAlign.Center,
                            ColumnGap = 8,
                        },
                        deviceContent) with
                {
                    RowGap = 12,
                })
            .VAlign(VerticalAlignment.Stretch);
        return card.HAlign(HorizontalAlignment.Stretch);
    }
}
