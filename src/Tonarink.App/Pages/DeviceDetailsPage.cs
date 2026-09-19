using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Components.DeviceVisuals;

namespace Tonarink.Pages;

sealed record DeviceDetailsPageProps(
    AppRuntimeState Runtime,
    LocalSendDevice Device,
    ElementTheme Theme);

sealed class DeviceDetailsPage : Component<DeviceDetailsPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var (_, refreshCachedPage) = UseReducer(0);
        // NavigationHost caches this page by AppRoute.DeviceDetails. A cache hit restores
        // its previous element tree without calling the route factory, so request one
        // reconciliation before the transition to pick up the newly selected device props.
        UseNavigationLifecycle(onNavigatingTo: _ => refreshCachedPage(value => value + 1));
        var favorites = UseExternalStore(
            listener =>
            {
                FavoriteDeviceStore.Changed += listener;
                return () => FavoriteDeviceStore.Changed -= listener;
            },
            static () => FavoriteDeviceStore.Entries);
        var currentDevice =
            Props.Runtime.Devices.FirstOrDefault(candidate => candidate.Fingerprint == Props.Device.Fingerprint) ??
            Props.Device;
        var favorite = favorites.GetValueOrDefault(currentDevice.Fingerprint);
        var displayName = favorite?.Name ?? currentDevice.Alias;
        var (showVerification, setShowVerification) = UseState(false);
        var (favoriteDraft, setFavoriteDraft) = UseState<FavoriteDevice?>(null);
        var (showRemoveFavorite, setShowRemoveFavorite) = UseState(false);
        var activity = Props.Runtime.DeviceActivity.GetValueOrDefault(currentDevice.Fingerprint)
                       ?? [];
        var page = FlexColumn(
                Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                        displayName,
                        currentDevice.DeviceModel,
                        currentDevice.DeviceType,
                        RemoteDeviceNumber(currentDevice)))
                    .MaxWidth(AppLayout.NarrowContentWidth)
                    .HAlign(HorizontalAlignment.Stretch),
                HStack(12,
                        ActionButton(
                            favorite is null ? "\uEB51" : "\uEB52",
                            favorite is null
                                ? t.Message(new("App", "FavoriteAction"))
                                : t.Message(new("App", "RemoveFavoriteAction")),
                            () =>
                            {
                                if (favorite is null)
                                    OpenFavoriteDialog(currentDevice);
                                else
                                    setShowRemoveFavorite(true);
                            },
                            accent: favorite is not null),
                        ActionButton(
                            "\uF760",
                            t.Message(new("App", "VerifyAction")),
                            () => setShowVerification(true)))
                    .HAlign(HorizontalAlignment.Center),
                InfoCard(t, currentDevice),
                ActivityCard(t, activity),
                Component<DeviceVerificationDialog, DeviceVerificationDialogProps>(new(
                    currentDevice,
                    Props.Runtime.Identity?.Fingerprint,
                    Props.Theme,
                    showVerification,
                    () => setShowVerification(false))),
                favoriteDraft is null
                    ? null
                    : Component<FavoriteDeviceDialog, FavoriteDeviceDialogProps>(new(
                        favoriteDraft,
                        IsNew: true,
                        Props.Theme,
                        FavoriteDeviceStore.Upsert,
                        () => setFavoriteDraft(null))),
                Component<DeleteFavoriteDialog, DeleteFavoriteDialogProps>(new(
                    displayName,
                    Props.Theme,
                    showRemoveFavorite,
                    () => FavoriteDeviceStore.Remove(currentDevice.Fingerprint),
                    () => setShowRemoveFavorite(false)))) with
            {
                RowGap = 20,
            };

        return ScrollView(
                Border(page)
                    .Padding(AppLayout.PagePadding)
                    .MaxWidth(AppLayout.DetailsContentWidth)
                    .HAlign(HorizontalAlignment.Stretch)
                    .Landmark(AutomationLandmarkType.Main))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch);

        void OpenFavoriteDialog(LocalSendDevice device)
        {
            var endpoint = device.PreferredEndpoint;
            setFavoriteDraft(new FavoriteDevice(
                device.Fingerprint,
                device.Alias,
                endpoint?.Address.ToString() ?? string.Empty,
                endpoint?.Port ?? LocalSendOptions.DefaultPort));
        }
    }

    private static Element ActionButton(
        string glyph,
        string label,
        Action onClick,
        bool accent = false)
    {
        var button = Button(
                VStack(6,
                    Icon(glyph),
                    Caption(label)),
                onClick)
            .MinWidth(104)
            .MinHeight(68)
            .AutomationName(label);

        return accent ? button.AccentButton() : button;
    }

    private static Element InfoCard(IntlAccessor t, LocalSendDevice device)
    {
        var endpoints = device.Endpoints.Count == 0
            ? t.Message(new("App", "DeviceInfoNoAddress"))
            : string.Join(Environment.NewLine, device.Endpoints.Select(static endpoint =>
                $"{endpoint.Protocol.ToString().ToUpperInvariant()}  {endpoint.Address}:{endpoint.Port}"));

        return Card(
            FlexColumn(
                    Subtitle(t.Message(new("App", "DeviceInformation")))
                        .HeadingLevel(AutomationHeadingLevel.Level2),
                    DetailRow(t.Message(new("App", "Name")), device.Alias),
                    DetailRow(t.Message(new("App", "DeviceModelLabel")),
                        DeviceModel(t, device.DeviceModel, device.DeviceType)),
                    DetailRow(t.Message(new("App", "ProtocolVersion")), device.ProtocolVersion),
                    DetailRow(t.Message(new("App", "Address")), endpoints),
                    DetailRow(t.Message(new("App", "LastSeen")), device.LastSeen.ToLocalTime().ToString("G")),
                    DetailRow(t.Message(new("App", "FingerprintLabel")), device.Fingerprint)) with
                {
                    RowGap = 12,
                });
    }

    private static Element DetailRow(string label, string value) =>
        Grid(
            columns: [GridSize.Px(128), GridSize.Star()],
            rows: [GridSize.Auto],
            BodyStrong(label).Grid(column: 0),
            TextBlock(value)
                .TextWrapping(TextWrapping.WrapWholeWords)
                .Grid(column: 1));

    private static Element ActivityCard(IntlAccessor t, IReadOnlyList<DeviceActivityEntry> activity) =>
        Card(
            FlexColumn(
                    Subtitle(t.Message(new("App", "DeviceLog")))
                        .HeadingLevel(AutomationHeadingLevel.Level2),
                    activity.Count == 0
                        ? TextBlock(t.Message(new("App", "DeviceLogEmpty")))
                            .Foreground(Theme.SecondaryText)
                        : VStack(8,
                        [
                            .. activity.Reverse().Select((entry, index) =>
                                ActivityRow(t, entry)
                                    .PositionInSet(index + 1, activity.Count)
                                    .WithKey($"{entry.Timestamp.UtcTicks}:{index}"))
                        ])) with
                {
                    RowGap = 12,
                });

    private static Element ActivityRow(IntlAccessor t, DeviceActivityEntry entry)
    {
        var action = entry.Kind switch
        {
            DeviceChangeKind.Added => t.Message(new("App", "DeviceLogAdded")),
            DeviceChangeKind.Removed => t.Message(new("App", "DeviceLogRemoved")),
            _ => t.Message(new("App", "DeviceLogUpdated")),
        };
        var endpoints = entry.Endpoints.Count == 0
            ? string.Empty
            : " · " + string.Join(", ", entry.Endpoints.Select(static endpoint =>
                $"{endpoint.Protocol.ToString().ToUpperInvariant()} {endpoint.Address}:{endpoint.Port}"));

        return Border(
                TextBlock($"[{entry.Timestamp.ToLocalTime():HH:mm:ss}] {action}{endpoints}")
                    .TextWrapping(TextWrapping.WrapWholeWords))
            .Padding(horizontal: 0, vertical: 4);
    }
}
