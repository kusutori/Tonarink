using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
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
        bool isWideLayout)
    {
        var description = state switch
        {
            LocalSendNodeState.Running =>
                t.Message(new("App", discoveryWarning is not null ? "DiscoveryScanHint" : "SameNetworkHint")),
            LocalSendNodeState.Faulted => t.Message(new("App", "PortInUseHint")),
            LocalSendNodeState.Created or LocalSendNodeState.Stopped or LocalSendNodeState.Disposed =>
                t.Message(new("App", "StartServerToDiscover")),
            LocalSendNodeState.Starting or LocalSendNodeState.Stopping => null,
        };

        // Keep text independent of the illustration's anchor. Wide cards place
        // that anchor lower; compact cards retain their existing proportions.
        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star(isWideLayout ? 3 : 1), GridSize.Star(isWideLayout ? 2 : 1)],
                Component<SendDiscoveryVisual, SendDiscoveryVisualProps>(new(state))
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Bottom)
                    .Grid(row: 0),
                VStack(12,
                        Subtitle(state switch
                        {
                            LocalSendNodeState.Running => t.Message(new("App", "SearchingDevices")),
                            LocalSendNodeState.Faulted => t.Message(new("App", "NetworkStartFailed")),
                            LocalSendNodeState.Starting => t.Message(new("App", "NodeStarting")),
                            LocalSendNodeState.Stopping => t.Message(new("App", "NodeStopping")),
                            LocalSendNodeState.Created or LocalSendNodeState.Stopped or LocalSendNodeState.Disposed =>
                                t.Message(new("App", "SettingsServerStopped")),
                        })
                            .TextAlignment(TextAlignment.Center)
                            .TextWrapping(TextWrapping.WrapWholeWords),
                        description is null ? null : TextBlock(description)
                            .Foreground(Theme.SecondaryText)
                            .TextAlignment(TextAlignment.Center)
                            .TextWrapping(TextWrapping.WrapWholeWords))
                    .Margin(top: 12)
                    .VAlign(VerticalAlignment.Top)
                    .Grid(row: 1));
    }
}
