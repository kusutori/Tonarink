using System.Net;
using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;
using static TransferOverlayVisuals;

sealed record DeviceDetailsPageProps(
    AppRuntimeState Runtime,
    LocalSendDevice Device,
    ElementTheme Theme);

sealed class DeviceDetailsPage : Component<DeviceDetailsPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var favorites = UseExternalStore<IReadOnlyDictionary<string, FavoriteDevice>>(
            listener =>
            {
                FavoriteDeviceStore.Changed += listener;
                return () => FavoriteDeviceStore.Changed -= listener;
            },
            static () => FavoriteDeviceStore.Entries);
        var currentDevice = Props.Runtime.Devices.FirstOrDefault(
            candidate => candidate.Fingerprint == Props.Device.Fingerprint) ?? Props.Device;
        var favorite = favorites.GetValueOrDefault(currentDevice.Fingerprint);
        var displayName = favorite?.Name ?? currentDevice.Alias;
        var (showVerification, setShowVerification) = UseState(false);
        var (favoriteTarget, setFavoriteTarget) = UseState<LocalSendDevice?>(null);
        var (favoriteName, setFavoriteName) = UseState(string.Empty);
        var (favoriteAddress, setFavoriteAddress) = UseState(string.Empty);
        var (favoritePort, setFavoritePort) = UseState(string.Empty);
        var (showRemoveFavorite, setShowRemoveFavorite) = UseState(false);
        var activity = Props.Runtime.DeviceActivity.GetValueOrDefault(currentDevice.Fingerprint)
            ?? Array.Empty<DeviceActivityEntry>();
        var page = FlexColumn(
            Heading(t.Message(new("App", "DeviceDetailsTitle")))
                .HeadingLevel(AutomationHeadingLevel.Level1),
            Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                displayName,
                currentDevice.DeviceModel,
                currentDevice.DeviceType,
                RemoteDeviceNumber(currentDevice)))
                .MaxWidth(560)
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
            FavoriteDialog(t),
            RemoveFavoriteDialog(t, currentDevice, displayName)) with
        {
            RowGap = 20,
        };

        return ScrollView(
                Border(page)
                    .Padding(36)
                    .MaxWidth(760)
                    .HAlign(HorizontalAlignment.Stretch)
                    .Landmark(AutomationLandmarkType.Main))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch);

        Element FavoriteDialog(IntlAccessor intl)
        {
            var validAddress = IPAddress.TryParse(favoriteAddress, out _);
            var validPort = int.TryParse(favoritePort, out var parsedPort)
                && parsedPort is >= 1 and <= ushort.MaxValue;
            var canSave = favoriteTarget is not null
                && !string.IsNullOrWhiteSpace(favoriteName)
                && validAddress
                && validPort;

            return (ContentDialog(
                intl.Message(new("App", "AddFavoriteTitle")),
                VStack(12,
                    FormField(
                        TextBox(favoriteName, setFavoriteName)
                            .AutomationName(intl.Message(new("App", "FavoriteDeviceName"))),
                        label: intl.Message(new("App", "Name")),
                        required: true),
                    FormField(
                        TextBox(favoriteAddress, setFavoriteAddress, placeholderText: "192.168.1.72")
                            .AutomationName(intl.Message(new("App", "FavoriteIpAddress"))),
                        label: intl.Message(new("App", "IpAddress")),
                        required: true,
                        description: validAddress || string.IsNullOrWhiteSpace(favoriteAddress)
                            ? null
                            : intl.Message(new("App", "InvalidIpAddress"))),
                    FormField(
                        TextBox(favoritePort, setFavoritePort, placeholderText: "53317")
                            .NumericInput()
                            .AutomationName(intl.Message(new("App", "FavoritePort"))),
                        label: intl.Message(new("App", "Port")),
                        required: true,
                        description: validPort || string.IsNullOrWhiteSpace(favoritePort)
                            ? null
                            : intl.Message(new("App", "InvalidPort")))),
                primaryButtonText: intl.Message(new("App", "Save"))) with
            {
                IsOpen = favoriteTarget is not null,
                SecondaryButtonText = intl.Message(new("App", "Cancel")),
                DefaultButton = ContentDialogButton.Primary,
                OnClosed = result =>
                {
                    var target = favoriteTarget;
                    if (result == ContentDialogResult.Primary && target is not null && canSave)
                    {
                        FavoriteDeviceStore.Upsert(new FavoriteDevice(
                            target.Fingerprint,
                            favoriteName.Trim(),
                            IPAddress.Parse(favoriteAddress).ToString(),
                            parsedPort));
                    }

                    setFavoriteTarget(null);
                },
            })
                .IsPrimaryButtonEnabled(canSave)
                .Set(dialog => ApplyDialogTheme(dialog, Props.Theme));
        }

        Element RemoveFavoriteDialog(IntlAccessor intl, LocalSendDevice device, string name) =>
            (ContentDialog(
                intl.Message(new("App", "DeleteFavoriteTitle")),
                TextBlock(intl.Message(
                        new("App", "DeleteFavoriteConfirm"),
                        ("device", name)))
                    .TextWrapping(TextWrapping.WrapWholeWords),
                primaryButtonText: intl.Message(new("App", "Delete"))) with
            {
                IsOpen = showRemoveFavorite,
                SecondaryButtonText = intl.Message(new("App", "Cancel")),
                DefaultButton = ContentDialogButton.Primary,
                OnClosed = result =>
                {
                    setShowRemoveFavorite(false);
                    if (result == ContentDialogResult.Primary)
                        FavoriteDeviceStore.Remove(device.Fingerprint);
                },
            }).Set(dialog => ApplyDialogTheme(dialog, Props.Theme));

        void OpenFavoriteDialog(LocalSendDevice device)
        {
            var endpoint = device.PreferredEndpoint;
            setFavoriteName(device.Alias);
            setFavoriteAddress(endpoint?.Address.ToString() ?? string.Empty);
            setFavoritePort(endpoint?.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "53317");
            setFavoriteTarget(device);
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
                        activity.Reverse().Select((entry, index) =>
                            ActivityRow(t, entry)
                                .PositionInSet(index + 1, activity.Count)
                                .WithKey($"{entry.Timestamp.UtcTicks}:{index}"))
                            .ToArray<Element?>())) with
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

    private static void ApplyDialogTheme(ContentDialog dialog, ElementTheme theme) =>
        dialog.RequestedTheme = theme;
}
