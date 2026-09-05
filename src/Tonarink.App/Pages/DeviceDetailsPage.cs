using System.Net;
using CommunityToolkit.WinUI.Controls;
using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.Toolkit.SegmentedElement;
using static TransferOverlayVisuals;

sealed record DeviceDetailsPageProps(
    AppRuntimeState Runtime,
    LocalSendDevice Device,
    ElementTheme Theme);

sealed class DeviceDetailsPage : Component<DeviceDetailsPageProps>
{
    private static readonly FontFamily MaterialIcons = new(
        "ms-appx:///Assets/MaterialIcons-Regular.ttf#Material Icons");

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
        var (verificationMode, setVerificationMode) = UseState(0);
        var (favoriteTarget, setFavoriteTarget) = UseState<LocalSendDevice?>(null);
        var (favoriteName, setFavoriteName) = UseState(string.Empty);
        var (favoriteAddress, setFavoriteAddress) = UseState(string.Empty);
        var (favoritePort, setFavoritePort) = UseState(string.Empty);
        var (showRemoveFavorite, setShowRemoveFavorite) = UseState(false);
        var verificationModes = UseMemo(() => new object[]
        {
            new SegmentedItem { Content = t.Message(new("App", "VerificationIcons")) },
            new SegmentedItem { Content = t.Message(new("App", "VerificationText")) },
        }, t.Locale);

        var activity = Props.Runtime.DeviceActivity.GetValueOrDefault(currentDevice.Fingerprint)
            ?? Array.Empty<DeviceActivityEntry>();

        var page = FlexColumn(
            Heading(t.Message(new("App", "DeviceDetailsTitle")))
                .HeadingLevel(AutomationHeadingLevel.Level1),
            Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                displayName,
                currentDevice.DeviceModel,
                currentDevice.DeviceType,
                RemoteDeviceNumber(currentDevice),
                ConnectedAnimationKey: DeviceConnectedKey(currentDevice.Fingerprint),
                AnimationRole: DeviceIdentityCardAnimationRole.Destination))
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
                        isSelected: favorite is not null),
                    ActionButton(
                        "\uE73E",
                        t.Message(new("App", "VerifyAction")),
                        () => setShowVerification(true)))
                .HAlign(HorizontalAlignment.Center),
            InfoCard(t, currentDevice),
            ActivityCard(t, activity),
            VerificationDialog(t, currentDevice, Props.Runtime.Identity?.Fingerprint, Props.Theme,
                verificationModes, verificationMode, setVerificationMode,
                showVerification, setShowVerification),
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
        bool isSelected = false)
    {
        var foreground = isSelected
            ? Theme.Ref("TextOnAccentFillColorPrimaryBrush")
            : Theme.PrimaryText;

        return Button(
                VStack(6,
                    Icon(glyph),
                    Caption(label)),
                onClick)
            .MinWidth(104)
            .MinHeight(68)
            .AutomationName(label)
            .Resources(resources => resources
                .Set("ButtonBackground", isSelected ? Theme.Accent : Theme.ControlFill)
                .Set("ButtonBackgroundPointerOver", isSelected ? Theme.AccentSecondary : Theme.ControlFillSecondary)
                .Set("ButtonBackgroundPressed", isSelected ? Theme.AccentTertiary : Theme.ControlFillTertiary)
                .Set("ButtonForeground", foreground)
                .Set("ButtonForegroundPointerOver", foreground)
                .Set("ButtonForegroundPressed", foreground));
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

    private static Element VerificationDialog(
        IntlAccessor t,
        LocalSendDevice device,
        string? localFingerprint,
        ElementTheme theme,
        object[] modes,
        int mode,
        Action<int> setMode,
        bool isOpen,
        Action<bool> setOpen)
    {
        Element content;
        if (localFingerprint is null)
        {
            content = TextBlock(t.Message(new("App", "IdentityLoading")));
        }
        else
        {
            var combined = DeviceVerification.CombineFingerprints(localFingerprint, device.Fingerprint);
            content = FlexColumn(
                Segmented(
                    selectedIndex: mode,
                    onSelectedIndexChanged: setMode,
                    items: modes)
                    .HAlign(HorizontalAlignment.Stretch),
                mode == 0 ? VerificationIcons(combined) : VerificationText(combined, t),
                TextBlock(t.Message(new("App", "VerificationCompareHint")))
                    .Foreground(Theme.SecondaryText)
                    .TextWrapping(TextWrapping.WrapWholeWords),
                device.PreferredEndpoint?.Protocol == LocalSendProtocol.Https
                    ? null
                    : InfoBar(
                        t.Message(new("App", "VerificationUnencryptedTitle")),
                        t.Message(new("App", "VerificationUnencryptedMessage"))) with
                    {
                        Severity = InfoBarSeverity.Warning,
                        IsOpen = true,
                    }) with
            {
                RowGap = 12,
            };
        }

        return (ContentDialog(
            t.Message(new("App", "VerificationTitle")),
            content,
            primaryButtonText: t.Message(new("App", "Close"))) with
        {
            IsOpen = isOpen,
            OnClosed = _ => setOpen(false),
        }).Set(dialog =>
        {
            ApplyDialogTheme(dialog, theme);
            dialog.MaxHeight = 440;
        });
    }

    private static Element VerificationIcons(string combined) =>
        Grid(
            columns: [GridSize.Star(), GridSize.Star(), GridSize.Star(), GridSize.Star()],
            rows: [GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Auto],
            DeviceVerification.GetMaterialIconGlyphs(combined).Select((glyph, index) =>
                TextBlock(glyph)
                    .FontFamily(MaterialIcons)
                    .FontSize(24)
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
                    .AccessibilityHidden()
                    .Grid(row: index / 4, column: index % 4)
                    .WithKey(index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ToArray<Element?>()) with
        {
            RowSpacing = 8,
            ColumnSpacing = 12,
        };

    private static Element VerificationText(string combined, IntlAccessor t) =>
        TextBox(combined, _ => { })
            .IsReadOnly(true)
            .TextWrapping(TextWrapping.Wrap)
            .AutomationName(t.Message(new("App", "VerificationText")));

    private static void ApplyDialogTheme(ContentDialog dialog, ElementTheme theme) =>
        dialog.RequestedTheme = theme;
}
