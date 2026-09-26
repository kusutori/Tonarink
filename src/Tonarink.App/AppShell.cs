using LocalSendDotNet;
using Microsoft.UI.Input;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Tonarink.Hooks;
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
        var reduceMotion = UseReducedMotion();
        var startHidden = AppPlatform.StartHidden && settings.MinimizeToTray;
        var (splashVisible, setSplashVisible) = UseState(!startHidden);
        var (splashDismissing, setSplashDismissing) = UseState(false);

        var shell = LocaleProvider(
                locale,
                Component<LocalizedAppShell, LocalizedAppShellProps>(new(
                    settings,
                    updateSettings,
                    locale,
                    splashVisible)),
                Resources,
                defaultLocale: "en-US")
            .RequestedTheme(theme)
            .Opacity(splashVisible && !splashDismissing ? 0 : 1)
            .OpacityTransition(reduceMotion ? TimeSpan.Zero : StartupSplashOverlay.FadeDuration)
            .IsHitTestVisible(!splashVisible);

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                shell.Grid(row: 0, column: 0),
                splashVisible
                    ? Component<StartupSplashOverlay, StartupSplashOverlayProps>(
                            new(
                                () => setSplashDismissing(true),
                                () => setSplashVisible(false)))
                        .Grid(row: 0, column: 0)
                    : null)
            .RequestedTheme(theme)
            .Backdrop(BackdropKind.Mica);
    }
}

sealed record LocalizedAppShellProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    string Locale,
    bool IsSplashVisible);

sealed class LocalizedAppShell : Component<LocalizedAppShellProps>
{
    public override Element Render() => RenderEachTime(context =>
    {
        var t = context.UseIntl();
        var window = context.UseWindow();
        var useTitleBarPaneToggle = !context.UseBreakpoint(AppLayout.CompactBreakpoint);
        var settings = Props.Settings;
        var contentTheme = AppTheme.ToElementTheme(settings.ThemeIndex);
        var updateSettings = Props.UpdateSettings;
        var navigation = context.UseNavigation(AppRoute.Receive);
        var favoriteRevision = context.UseExternalStore(
            listener =>
            {
                FavoriteDeviceStore.Changed += listener;
                return () => FavoriteDeviceStore.Changed -= listener;
            },
            static () => FavoriteDeviceStore.Revision);
        context.UsePeopleSuggestions(
            settings.ShowFavoriteDevicesInWindowsShare,
            favoriteRevision);
        var (headerEpoch, bumpHeader) = context.UseReducer(0);
        var headerRight = context.UseRef<PageHeaderRightSlot?>();
        headerRight.Current ??= new PageHeaderRightSlot
        {
            Invalidate = () => bumpHeader(epoch => epoch + 1),
        };
        _ = headerEpoch;
        var navigationViewRef = context.UseRef<NavigationView?>();
        var (isNavigationPaneOpen, setNavigationPaneOpen) = context.UseState(false);
        var (detailsDevice, setDetailsDevice) = context.UseState<LocalSendDevice?>(null);
        var (selectedSendItems, updateSelectedSendItems) =
            context.UseReducer<IReadOnlyList<SelectedSendItem>>([]);
        var (isAppDropActive, setAppDropActive) = context.UseState(false);
        var (dropFeedbacks, updateDropFeedbacks) =
            context.UseReducer<IReadOnlyList<TransientInfoBarMessage>>([]);
        var (outgoingTransfer, setOutgoingTransfer) = context.UseState<OutgoingTransferViewState?>(null);
        var mouseBackHandler = context.UseRef<PointerEventHandler?>();
        var nodeSession = context.UseLocalSendNode(settings, t);
        var runtime = nodeSession.Runtime;
        var windowController = context.UseShellWindow(window, settings.MinimizeToTray, t);

        var activations = context.UseShellActivations(
            navigation,
            nodeSession,
            windowController.Restore,
            canPresentDialogs: !Props.IsSplashVisible);
        context.UseJumpListIntegration(t, Props.Locale);

        TrayFlyoutStore.Restore = windowController.Restore;
        TrayFlyoutStore.StartServer = nodeSession.StartOrRestart;
        TrayFlyoutStore.StopServer = nodeSession.Stop;
        TrayFlyoutStore.SendAsync = request => TraySendCoordinator.SendAsync(
            request,
            nodeSession.Node,
            runtime.Identity,
            settings,
            t,
            setOutgoingTransfer);
        context.UseEffect(
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

        context.UseWidgetIntegration(
            runtime,
            settings,
            outgoingTransfer,
            nodeSession.IsServerDesired,
            windowController.Restore,
            nodeSession.StartOrRestart,
            nodeSession.Stop);

        context.UseEffect(() =>
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
                    new(
                        settings.DownloadDirectory,
                        contentTheme,
                        activations.JumpListHistoryId,
                        activations.ConsumeJumpListHistory)),
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
                    settings.ExpandDragDropToEntireApp,
                    device =>
                    {
                        setDetailsDevice(device);
                        navigation.Navigate(AppRoute.DeviceDetails, AppNavigation.DrillIn);
                    },
                    activations.JumpListFavoriteFingerprint,
                    activations.ConsumeJumpListFavorite)),
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
                .Landmark(AutomationLandmarkType.Navigation)
                .AutomationName(t.Message(new("App", "MainNavigation")))
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

        var shellContent = FlexColumn(titleBar, contentLayer)
            .Opacity(isAppDropActive ? 0.2 : 1)
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

        Element root = Grid(
            columns: [GridSize.Star()],
            rows: [GridSize.Star()],
            shellContent
                .WithKey("shell-content")
                .Grid(row: 0, column: 0),
            isAppDropActive
                ? Grid(
                        columns: [GridSize.Star()],
                        rows: [GridSize.Star()],
                        Border(null)
                            .Background(Theme.Ref("SmokeFillColorDefaultBrush"))
                            .Grid(row: 0, column: 0),
                        Card(
                                HStack(12,
                                    Icon("\uF413").AccessibilityHidden(),
                                    BodyStrong(t.Message(new("App", "DropFilesAnywherePrompt")))))
                            .Padding(20)
                            .HAlign(HorizontalAlignment.Center)
                            .VAlign(VerticalAlignment.Center)
                            .Grid(row: 0, column: 0))
                    .IsHitTestVisible(false)
                    .WithKey("app-drop-overlay")
                    .Grid(row: 0, column: 0)
                : null,
            dropFeedbacks.Count == 0
                ? null
                : Component<TransientInfoBarStack, TransientInfoBarStackProps>(new(
                        dropFeedbacks,
                        id => updateDropFeedbacks(current =>
                            (TransientInfoBarMessage[])
                            [.. current.Select(message => message.Id == id
                                ? message with { HasEntered = true }
                                : message)]),
                        id => updateDropFeedbacks(current =>
                            (TransientInfoBarMessage[])
                            [.. current.Where(message => message.Id != id)])))
                    .WithKey("drop-feedback-stack")
                    .Grid(row: 0, column: 0));

        if (settings.ExpandDragDropToEntireApp && !Props.IsSplashVisible)
        {
            root = root
                .OnDragEnter(args =>
                {
                    if (!args.Data.HasFormat(StandardDataFormats.StorageItems))
                        return;

                    args.AcceptedOperation = DragOperations.Copy;
                    setAppDropActive(true);
                })
                .OnDragOver(args =>
                {
                    if (!args.Data.HasFormat(StandardDataFormats.StorageItems))
                        return;

                    args.AcceptedOperation = DragOperations.Copy;
                    args.UIOverride.Caption = t.Message(new("App", "DropFilesCaption"));
                    args.UIOverride.IsCaptionVisible = true;
                    args.UIOverride.IsGlyphVisible = true;
                })
                .OnDragLeave(_ => setAppDropActive(false))
                .OnDrop(args =>
                {
                    setAppDropActive(false);
                    args.AcceptedOperation = DragOperations.Copy;
                    _ = AddDroppedItemsAsync(args.Data);
                }, acceptedOps: DragOperations.Copy);
        }

        return root;

        async Task AddDroppedItemsAsync(DragData dragData)
        {
            try
            {
                var selected = await SelectedSendItemReader.ReadDroppedAsync(dragData);
                if (selected.Count == 0)
                {
                    AddDropFeedback(
                        Guid.NewGuid(),
                        t.Message(new("App", "DroppedItemsEmpty")),
                        InfoBarSeverity.Warning);
                    return;
                }

                updateSelectedSendItems(current => (SelectedSendItem[])[.. current, .. selected]);
                AddDropFeedback(
                    Guid.NewGuid(),
                    t.Message(new("App", "ItemsAdded"), ("count", selected.Count)),
                    InfoBarSeverity.Success);
                if (navigation.CurrentRoute != AppRoute.Send)
                    navigation.Navigate(AppRoute.Send);
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report("Could not add items dropped on the application", exception);
                AddDropFeedback(
                    Guid.NewGuid(),
                    t.Message(new("App", "DropItemsFailed"), ("error", exception.Message)),
                    InfoBarSeverity.Error);
            }
        }

        void AddDropFeedback(Guid id, string message, InfoBarSeverity severity) =>
            updateDropFeedbacks(current =>
                (TransientInfoBarMessage[])
                [
                    new TransientInfoBarMessage(
                        id,
                        t.Message(new("App", "DropFilesCaption")),
                        message,
                        severity,
                        Environment.TickCount64 + TransientInfoBarMessage.LifetimeMilliseconds),
                    .. current,
                ]);
    });

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
