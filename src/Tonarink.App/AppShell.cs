using LocalSendDotNet;
using Microsoft.UI.Input;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink;

sealed class AppShell : Component
{
    internal static readonly ReswResourceProvider Resources = new(defaultLocale: "en-US");

    public override Element Render()
    {
        var (settings, updateSettings) = UseReducer(AppSettingsStore.Load());
        var window = UseWindow();

        UseEffect(() => AppSettingsStore.Save(settings), settings);
        UseEffect(() =>
        {
            if (settings.StartWithWindows)
                WindowsStartup.UpdateLaunchCommand(settings.MinimizeToTray);
        }, settings.StartWithWindows, settings.MinimizeToTray);
        UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            _ = SyncStartupAsync(cts.Token);
            return () => cts.Cancel();

            async Task SyncStartupAsync(CancellationToken cancellationToken)
            {
                try
                {
                    var enabled = await WindowsStartup.IsEnabledAsync().ConfigureAwait(true);
                    if (cancellationToken.IsCancellationRequested || enabled == settings.StartWithWindows)
                        return;
                    updateSettings(current => current with { StartWithWindows = enabled });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The effect was disposed before the startup state query completed.
                }
                catch (Exception exception)
                {
                    AppDiagnostics.Report("Could not synchronize the Windows startup state", exception);
                }
            }
        });

        UseEffect(() =>
        {
            NativeSystemMenuTheme.Apply(settings.ThemeIndex);
            if (window is not null)
            {
                window.AppWindow.TitleBar.PreferredTheme = settings.ThemeIndex switch
                {
                    1 => TitleBarTheme.Light,
                    2 => TitleBarTheme.Dark,
                    _ => TitleBarTheme.UseDefaultAppMode,
                };
            }
        }, settings.ThemeIndex);

        var locale = AppLocale.Resolve(settings.LanguageIndex);
        var theme = AppTheme.ToElementTheme(settings.ThemeIndex);
        var startHidden = AppPlatform.StartHidden && settings.MinimizeToTray;
        var (splashVisible, setSplashVisible) = UseState(!startHidden);

        var shell = LocaleProvider(
                locale,
                Component<LocalizedAppShell, LocalizedAppShellProps>(new(settings, updateSettings, locale)),
                Resources,
                defaultLocale: "en-US")
            .RequestedTheme(theme);

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                shell.Grid(row: 0, column: 0),
                splashVisible
                    ? Component<StartupSplashOverlay, StartupSplashOverlayProps>(
                            new(setSplashVisible))
                        .Grid(row: 0, column: 0)
                    : null)
            .RequestedTheme(theme)
            .Backdrop(BackdropKind.Mica);
    }
}

sealed record LocalizedAppShellProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    string Locale);

sealed partial class LocalizedAppShell : Component<LocalizedAppShellProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var useTitleBarPaneToggle = !UseBreakpoint(AppLayout.CompactBreakpoint);
        var settings = Props.Settings;
        var contentTheme = AppTheme.ToElementTheme(settings.ThemeIndex);
        var updateSettings = Props.UpdateSettings;
        var navigation = UseNavigation(AppRoute.Receive);
        var favoriteRevision = UseExternalStore(
            listener =>
            {
                FavoriteDeviceStore.Changed += listener;
                return () => FavoriteDeviceStore.Changed -= listener;
            },
            static () => FavoriteDeviceStore.Revision);
        var (headerEpoch, bumpHeader) = UseReducer(0);
        var headerRight = UseRef<PageHeaderRightSlot?>();
        headerRight.Current ??= new PageHeaderRightSlot
        {
            Invalidate = () => bumpHeader(epoch => epoch + 1),
        };
        _ = headerEpoch;
        var navigationViewRef = UseRef<NavigationView?>();
        var (isNavigationPaneOpen, setNavigationPaneOpen) = UseState(false);
        var (detailsDevice, setDetailsDevice) = UseState<LocalSendDevice?>(null);
        var (selectedSendItems, updateSelectedSendItems) =
            UseReducer<IReadOnlyList<SelectedSendItem>>([]);
        var (outgoingTransfer, setOutgoingTransfer) = UseState<OutgoingTransferViewState?>(null);
        var mouseBackHandler = UseRef<PointerEventHandler?>();
        var nodeSession = UseLocalSendNode(settings, t);
        var runtime = nodeSession.Runtime;
        var windowController = UseShellWindow(window, settings.MinimizeToTray, t);
        var activations = UseShellActivations(
            navigation,
            nodeSession,
            windowController.Restore);

        TrayFlyoutStore.Restore = windowController.Restore;
        TrayFlyoutStore.StartServer = nodeSession.StartOrRestart;
        TrayFlyoutStore.StopServer = nodeSession.Stop;
        UseEffect(
            () => TrayFlyoutStore.Publish(
                runtime,
                outgoingTransfer,
                settings,
                nodeSession.IsServerDesired),
            runtime,
            settings,
            nodeSession.IsServerDesired,
            outgoingTransfer is null,
            outgoingTransfer?.BytesTransferred ?? 0,
            outgoingTransfer?.TotalBytes ?? 0,
            (int?)outgoingTransfer?.State ?? -1);

        UseWidgetIntegration(
            runtime,
            settings,
            outgoingTransfer,
            nodeSession.IsServerDesired,
            windowController.Restore,
            nodeSession.StartOrRestart,
            nodeSession.Stop);

        UseEffect(() =>
        {
            if (runtime.IncomingTransfers.Count > 0)
                windowController.Restore();
        }, runtime.IncomingTransfers.Count);

        var paneStatus = Component<NetworkStatusPane, NetworkStatusPaneProps>(new(
            isNavigationPaneOpen,
            runtime.NodeState,
            runtime.Error,
            runtime.DiscoveryWarning));

        var titleBar = Component<ShellTitleBar, ShellTitleBarProps>(new(
            navigation,
            useTitleBarPaneToggle,
            () =>
            {
                if (navigationViewRef.Current is { } navigationView)
                    navigationView.IsPaneOpen = !navigationView.IsPaneOpen;
            }));

        var content = (NavigationHost(navigation, route => route switch
            {
                AppRoute.Receive => Component<ReceivePage, ReceivePageProps>(new(
                    runtime,
                    settings,
                    updateSettings)),
                AppRoute.History => Component<HistoryPage, HistoryPageProps>(
                    new(settings.DownloadDirectory, contentTheme)),
                AppRoute.Send => Component<SendPage, SendPageProps>(new(
                    runtime,
                    nodeSession.Node,
                    contentTheme,
                    nodeSession.RefreshAsync,
                    setOutgoingTransfer,
                    activations.ShareTargetPayload,
                    activations.ConsumeShareTargetPayload,
                    selectedSendItems,
                    updateSelectedSendItems,
                    settings.KeepItemsForMultipleReceivers,
                    value => updateSettings(current => current with
                    {
                        KeepItemsForMultipleReceivers = value,
                    }),
                    settings.VerifyChecksumsOnSend,
                    device =>
                    {
                        setDetailsDevice(device);
                        navigation.Navigate(AppRoute.DeviceDetails, AppNavigation.DrillIn);
                    })),
                AppRoute.Settings => Component<SettingsPage, SettingsPageProps>(new(
                    settings,
                    runtime,
                    updateSettings,
                    nodeSession.StartOrRestart,
                    nodeSession.Stop)),
                AppRoute.NetworkInterfaces => Component<NetworkInterfacesPage, NetworkInterfacesPageProps>(
                    new(settings, updateSettings)),
                AppRoute.WebShare => Component<WebSharePage, WebSharePageProps>(new(
                    nodeSession.Node,
                    runtime,
                    settings,
                    nodeSession.SetHttpsOverride,
                    WebShareMode.Send)),
                AppRoute.WebReceive => Component<WebSharePage, WebSharePageProps>(new(
                    nodeSession.Node,
                    runtime,
                    settings,
                    nodeSession.SetHttpsOverride,
                    WebShareMode.Receive)),
                AppRoute.DeviceDetails when detailsDevice is not null =>
                    Component<DeviceDetailsPage, DeviceDetailsPageProps>(new(
                            runtime,
                            detailsDevice,
                            contentTheme))
                        .WithKey(detailsDevice.Fingerprint),
                _ => TextBlock(t.Message(new("App", "PageNotFound"))),
            }) with
        {
            CacheMode = NavigationCacheMode.Enabled,
            CacheSize = 3,
            Transition = AppNavigation.IsDetail(navigation.CurrentRoute)
                    ? NavigationTransition.DrillIn()
                    : NavigationTransition.Slide(),
        }).WithKey($"navigation:{Props.Locale}:{favoriteRevision}");

        var navigationView = ((NavigationView(
                    [
                        NavItem(t.Message(new("App", "NavReceive")), icon: "\uE701", tag: RouteTag(AppRoute.Receive)),
                        NavItem(t.Message(new("App", "NavSend")), icon: "Send", tag: RouteTag(AppRoute.Send)),
                        NavItem(t.Message(new("App", "NavSettings")), icon: "Setting",
                            tag: RouteTag(AppRoute.Settings)),
                    ],
                    content)
                .WithNavigation(navigation, RouteTag, ParseRoute)
                .PaneDisplayMode(NavigationViewPaneDisplayMode.Auto)
                .CompactModeThresholdWidth(AppLayout.CompactBreakpoint)
                .ExpandedModeThresholdWidth(AppLayout.ExpandedBreakpoint)
                .OpenPaneLength(AppLayout.NavigationOpenPaneLength)
                .CompactPaneLength(AppLayout.NavigationCompactPaneLength)
                .PaneFooter(paneStatus)
                .PaneOpenChanged(setNavigationPaneOpen)
                .PaneToggleButtonVisible(!useTitleBarPaneToggle)
                .AlwaysShowHeader()
                .BackButtonVisible(false)
                .TitleBarAutoPadding(false)
                .Set(static navigationView =>
                {
                    navigationView.Resources["NavigationViewHeaderMargin"] = AppLayout.PageHeaderMargin;
                    navigationView.Resources["NavigationViewMinimalHeaderMargin"] =
                        AppLayout.PageHeaderMargin;
                })
                .OnMountAdd(element =>
                {
                    if (element is not NavigationView navigationView)
                        return;

                    navigationViewRef.Current = navigationView;
                    setNavigationPaneOpen(navigationView.IsPaneOpen);
                })
                .OnUnmountAdd(element =>
                {
                    if (ReferenceEquals(navigationViewRef.Current, element))
                        navigationViewRef.Current = null;
                })
                .Flex(grow: 1, basis: 0)) with
        {
            IsSettingsVisible = false,
            Header = Component<PageHeader, PageHeaderProps>(new(
                    navigation.CurrentRoute,
                    navigation,
                    headerRight.Current.Owner == navigation.CurrentRoute
                        ? headerRight.Current.Build?.Invoke()
                        : null)),
        }).Provide(PageHeader.RightSlot, headerRight.Current!);

        var pendingIncoming = runtime.IncomingTransfers.FirstOrDefault();
        var overlayVisible = pendingIncoming is not null && nodeSession.Node is not null
                             || outgoingTransfer is not null;
        var navigationLayer = Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                navigationView.Grid(row: 0, column: 0))
            .Opacity(overlayVisible ? 0 : 1)
            .IsVisible(pendingIncoming is null)
            .IsHitTestVisible(!overlayVisible);
        // Sending still needs this fade: instant Opacity(0) on the ancestor
        // crashes WinUI connected animation. Incoming has no connected animation.
        if (pendingIncoming is null)
            navigationLayer = navigationLayer.OpacityTransition(TimeSpan.FromMilliseconds(300));

        var contentLayer = Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                navigationLayer.Grid(row: 0, column: 0),
                Component<TransferOverlayHost, TransferOverlayHostProps>(new(
                        nodeSession.Node,
                        pendingIncoming,
                        outgoingTransfer,
                        settings,
                        contentTheme,
                        nodeSession.DismissIncoming,
                        () => setOutgoingTransfer(null)))
                    .Grid(row: 0, column: 0))
            .Flex(grow: 1, basis: 0);

        var root = FlexColumn(titleBar, contentLayer)
            .OnMountAdd(element =>
            {
                void OnPointerPressed(object sender, PointerRoutedEventArgs e)
                {
                    if (e.GetCurrentPoint(element).Properties.PointerUpdateKind
                        != PointerUpdateKind.XButton1Pressed)
                        return;

                    if (!navigation.CanGoBack)
                        return;

                    e.Handled = true;
                    navigation.GoBack();
                }

                PointerEventHandler handler = OnPointerPressed;
                mouseBackHandler.Current = handler;
                element.AddHandler(UIElement.PointerPressedEvent, handler, handledEventsToo: true);
            })
            .OnUnmountAdd(element =>
            {
                if (mouseBackHandler.Current is { } handler)
                    element.RemoveHandler(UIElement.PointerPressedEvent, handler);
            });

        return root;
    }

    private static string RouteTag(AppRoute route) => route switch
    {
        AppRoute.Receive => "receive",
        AppRoute.History => "receive",
        AppRoute.Send => "send",
        AppRoute.Settings => "settings",
        AppRoute.NetworkInterfaces => "settings",
        AppRoute.WebShare => "send",
        AppRoute.WebReceive => "receive",
        AppRoute.DeviceDetails => "send",
        _ => "receive",
    };

    private static AppRoute ParseRoute(string tag) => tag switch
    {
        "send" => AppRoute.Send,
        "settings" => AppRoute.Settings,
        _ => AppRoute.Receive,
    };
}
