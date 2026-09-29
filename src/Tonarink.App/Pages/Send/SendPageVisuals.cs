using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Components.Devices.DeviceVisuals;
using static Tonarink.Components.Transfers.TransferOverlayVisuals;

namespace Tonarink.Pages.Send;

static class SendPageVisuals
{
    public static Element SelectionTile(string label, string icon, Action onClick, IntlAccessor t) =>
        Button(
                VStack(8,
                    Icon(icon).AccessibilityHidden(),
                    BodyStrong(label)),
                onClick)
            .MinHeight(104)
            .HAlign(HorizontalAlignment.Stretch)
            .AutomationName(t.Message(new("App", "ChooseItem"), ("item", label)));

    public static Element EmptySelection(
        bool isDropActive,
        string pickerMessage,
        bool expandDragDropToEntireApp,
        IntlAccessor t)
    {
        var nothingSelected = t.Message(new("App", "NothingSelected"));
        var dropText = isDropActive
            ? t.Message(new("App", "ReleaseFilesToAdd"))
            : t.Message(new("App", expandDragDropToEntireApp
                ? "DropFilesOrFoldersAnywhere"
                : "DropFilesOrFolders"));

        return (FlexColumn(
                    Image("ms-appx:///Assets/FileDrop.svg")
                        .Size(192, 112)
                        .AccessibilityHidden(),
                    Subtitle(dropText),
                    pickerMessage == nothingSelected
                        ? null
                        : Caption(pickerMessage)
                            .Foreground(Theme.SecondaryText)
                            .TextWrapping(TextWrapping.WrapWholeWords)) with
        {
            RowGap = 12,
            AlignItems = FlexAlign.Center,
            JustifyContent = FlexJustify.Center,
        })
            .MinHeight(280)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch);
    }

    public static Element DeviceCard(
        LocalSendDevice device,
        FavoriteDevice? favorite,
        bool isEnabled,
        Action<FrameworkElement?> onClick,
        Action onDetails,
        Action onFavorite,
        Action onVerify,
        IntlAccessor t)
    {
        var displayName = favorite?.Name ?? device.Alias;
        var favoriteCommand = new Command
        {
            Label = t.Message(new("App", favorite is null
                ? "FavoriteAction"
                : "RemoveFavoriteAction")),
            Icon = new FontIconData(favorite is null
                ? AppIcons.FavoriteOutline
                : AppIcons.Favorite),
            Execute = onFavorite,
        };
        var verifyCommand = new Command
        {
            Label = t.Message(new("App", "VerifyAction")),
            Icon = new FontIconData(AppIcons.Verify),
            Execute = onVerify,
        };

        return Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                displayName,
                device.DeviceModel,
                device.DeviceType,
                RemoteDeviceNumber(device),
                DeviceConnectedKey(device.Fingerprint),
                onClick,
                t.Message(new("App", "SendToDevice"), ("device", displayName)),
                isEnabled,
                TrailingReserve: 64,
                AnimationRole: DeviceIdentityCardAnimationRole.Source,
                SecondaryGlyph: AppIcons.Details,
                SecondaryAutomationName: t.Message(
                    new("App", "OpenDeviceDetails"),
                    ("device", displayName)),
                OnSecondaryClick: _ => onDetails(),
                IsFavorite: favorite is not null))
            .WithContextFlyout(MenuItems(
                MenuItem(favoriteCommand),
                MenuItem(verifyCommand)));
    }

    public static FavoriteDevice CreateFavorite(LocalSendDevice device)
    {
        var endpoint = device.PreferredEndpoint;
        return new FavoriteDevice(
            device.Fingerprint,
            device.Alias,
            endpoint?.Address.ToString() ?? string.Empty,
            endpoint?.Port ?? LocalSendOptions.DefaultPort,
            device.DeviceType);
    }

    public static Element EmptyDevices(
        IntlAccessor t,
        LocalSendNodeState state,
        string? discoveryWarning,
        Element searchingAnimation) =>
        FlexColumn(
                state switch
                {
                    LocalSendNodeState.Faulted => Icon(AppIcons.Error).AccessibilityHidden(),
                    _ => searchingAnimation,
                },
                Subtitle(state switch
                {
                    LocalSendNodeState.Faulted => t.Message(new("App", "NetworkStartFailed")),
                    _ => t.Message(new("App", "SearchingDevices")),
                }),
                TextBlock(state switch
                {
                    LocalSendNodeState.Faulted => t.Message(new("App", "PortInUseHint")),
                    _ when discoveryWarning is not null => t.Message(new("App", "DiscoveryScanHint")),
                    _ => t.Message(new("App", "SameNetworkHint")),
                })
                    .Foreground(Theme.SecondaryText)
                    .TextWrapping(TextWrapping.WrapWholeWords)) with
        {
            RowGap = 12,
            AlignItems = FlexAlign.Center,
            JustifyContent = FlexJustify.Center,
        };

    public static void PlaySearchingAnimation(AnimatedVisualPlayer? player, bool play)
    {
        if (player is null)
            return;

        player.Source = new SearchingDevices();
        if (play)
            _ = player.PlayAsync(fromProgress: 0, toProgress: 1, looped: true);
    }
}
