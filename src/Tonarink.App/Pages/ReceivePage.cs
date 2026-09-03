using CommunityToolkit.WinUI.Controls;
using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.Toolkit.SegmentedElement;

sealed record ReceivePageProps(
    AppRuntimeState Runtime,
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings);

sealed class ReceivePage : Component<ReceivePageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var navigation = UseNavigation<AppRoute>();
        var autoSaveItems = UseMemo(() => new object[]
        {
            new SegmentedItem { Content = t.Message(new("App", "AutoSaveOff")) },
            new SegmentedItem { Content = t.Message(new("App", "AutoSaveFavorites")) },
            new SegmentedItem { Content = t.Message(new("App", "AutoSaveOn")) },
        }, t.Locale);
        var identity = Props.Runtime.Identity;
        var stored = AppSettingsStore.Load();
        var (alias, setAlias) = UseState(stored.ResolvedAlias);
        var idleLogoPlayerRef = UseRef<AnimatedVisualPlayer?>(null);
        UseNavigationLifecycle(onNavigatedTo: _ =>
        {
            var current = AppSettingsStore.Load();
            setAlias(current.ResolvedAlias);
            PlayIdleLogoAnimation(idleLogoPlayerRef.Current);
        });
        var fingerprint = identity?.Fingerprint;
        var fingerprintPreview = fingerprint is null ? null : fingerprint[..12];
        var shortId = fingerprint is null
            ? t.Message(new("App", "IdentityLoading"))
            : $"#{Convert.ToInt32(fingerprint[..4], 16) % 1000:D3}  #1";

        var identityPanel = FlexColumn(
            Props.Runtime.IncomingTransfers.Count > 0
                ? null
                : (AnimatedVisualPlayer() with { AutoPlay = false })
                .Size(144, 144)
                .HAlign(HorizontalAlignment.Center)
                .AccessibilityHidden()
                .OnMountAdd(element =>
                {
                    if (element is not AnimatedVisualPlayer player)
                        return;

                    idleLogoPlayerRef.Current = player;
                    PlayIdleLogoAnimation(player);
                })
                .OnUnmountAdd(element =>
                {
                    if (element is AnimatedVisualPlayer player
                        && ReferenceEquals(idleLogoPlayerRef.Current, player))
                    {
                        idleLogoPlayerRef.Current = null;
                    }
                }),
            Title(alias).HAlign(HorizontalAlignment.Center),
            BodyLarge(shortId)
                .Foreground(Theme.SecondaryText)
                .HAlign(HorizontalAlignment.Center),
            fingerprint is null
                ? null
                : Caption(t.Message(
                        new("App", "Fingerprint"),
                        ("fingerprint", fingerprintPreview!)))
                    .Foreground(Theme.TertiaryText)
                    .HAlign(HorizontalAlignment.Center),
            Button(
                    HStack(8,
                        Icon("\uE774"),
                        TextBlock(t.Message(new("App", "WebReceiveTitle")))),
                    () => navigation.Navigate(AppRoute.WebReceive, AppNavigation.DrillIn))
                .HAlign(HorizontalAlignment.Center)
                .Margin(top: 12)
                .IsEnabled(Props.Runtime.NodeState == LocalSendNodeState.Running)
                .AutomationName(t.Message(new("App", "WebReceiveTitle")))
                .ToolTip(t.Message(new("App", "WebReceiveTitle")))) with
        {
            RowGap = 12,
            AlignItems = FlexAlign.Center,
        };

        var autoSave = Card(
            FlexColumn(
                FlexRow(
                    VStack(4,
                        Subtitle(t.Message(new("App", "AutoSaveTitle"))),
                        TextBlock(t.Message(new("App", "AutoSaveDescription")))
                            .Foreground(Theme.SecondaryText))
                        .Flex(grow: 1, basis: 0),
                    Props.Runtime.IncomingTransfers.Count > 0
                        ? InfoBadge(Props.Runtime.IncomingTransfers.Count)
                            .AutomationName(t.Message(
                                new("App", "PendingRequests"),
                                ("count", Props.Runtime.IncomingTransfers.Count)))
                        : null) with
                {
                    AlignItems = FlexAlign.Center,
                    ColumnGap = 12,
                },
                Segmented(
                    selectedIndex: (int)Props.Settings.AutoSave,
                    onSelectedIndexChanged: index => Props.UpdateSettings(settings => settings with
                    {
                        AutoSave = (AutoSaveMode)index,
                        FavoritesOnly = (AutoSaveMode)index == AutoSaveMode.Favorites,
                    }),
                    items: autoSaveItems)
                    .HAlign(HorizontalAlignment.Stretch)) with
            { RowGap = 20 })
            .MaxWidth(560)
            .HAlign(HorizontalAlignment.Stretch);

        var page = ScrollView(
            FlexColumn(
                FlexRow(
                        Heading(t.Message(new("App", "ReceiveTitle")))
                            .HeadingLevel(AutomationHeadingLevel.Level1)
                            .Flex(grow: 1, basis: 0),
                        Button(Icon(FontIcon("\uE121")), () => navigation.Navigate(AppRoute.History, AppNavigation.DrillIn))
                            .SubtleButton()
                            .AutomationName(t.Message(new("App", "HistoryOpenReceiveHistory")))
                            .MinWidth(40)
                            .MinHeight(40),
                        Button(Icon("\uF167"), null)
                            .SubtleButton()
                            .AutomationName(t.Message(new("App", "DeviceInfo")))
                            .MinWidth(40)
                            .MinHeight(40)
                            .WithFlyout(ContentFlyout(
                                DeviceInfoFlyout(t, alias, Props.Settings, identity),
                                FlyoutPlacementMode.BottomEdgeAlignedRight)))
                    with
                { AlignItems = FlexAlign.Center, ColumnGap = 8 },
                identityPanel.Flex(grow: 1, basis: 0),
                autoSave) with
            {
                RowGap = 32,
                AlignItems = FlexAlign.Stretch,
            })
            .Padding(36)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);

        return page;
    }

    private static Element DeviceInfoFlyout(
        IntlAccessor t,
        string alias,
        AppSettings settings,
        LocalSendIdentity? identity)
    {
        var addresses = AppNetworkAddresses.ListIpv4(settings);
        var ipText = addresses.Count == 0
            ? t.Message(new("App", "DeviceInfoNoAddress"))
            : string.Join(Environment.NewLine, addresses);
        var port = identity?.Port ?? settings.Port;

        return (Grid(
            columns: [GridSize.Auto, GridSize.Star()],
            rows: [GridSize.Auto, GridSize.Auto, GridSize.Auto],
            DeviceInfoLabel(t.Message(new("App", "DeviceInfoAlias"))).Grid(row: 0, column: 0),
            DeviceInfoValue(alias).Grid(row: 0, column: 1),
            DeviceInfoLabel(t.Message(new("App", "DeviceInfoIp"))).Grid(row: 1, column: 0),
            DeviceInfoValue(ipText).Grid(row: 1, column: 1),
            DeviceInfoLabel(t.Message(new("App", "DeviceInfoPort"))).Grid(row: 2, column: 0),
            DeviceInfoValue(port.ToString()).Grid(row: 2, column: 1)) with
        {
            ColumnSpacing = 24,
            RowSpacing = 8,
        })
            .MinWidth(280)
            .Padding(8);
    }

    private static Element DeviceInfoLabel(string text) =>
        TextBlock(text)
            .Foreground(Theme.SecondaryText)
            .VAlign(VerticalAlignment.Center);

    private static Element DeviceInfoValue(string text) =>
        TextBlock(text)
            .TextAlignment(TextAlignment.Right)
            .TextWrapping(TextWrapping.WrapWholeWords)
            .HAlign(HorizontalAlignment.Right)
            .VAlign(VerticalAlignment.Center);

    private static void PlayIdleLogoAnimation(AnimatedVisualPlayer? player)
    {
        if (player is null)
            return;

        player.Source = new Tonarink.IdleLogo();
        _ = player.PlayAsync(fromProgress: 0, toProgress: 1, looped: true);
    }
}
