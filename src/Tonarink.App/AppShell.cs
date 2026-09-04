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
using Windows.System.UserProfile;

sealed class AppShell : Component
{
    private static readonly ReswResourceProvider Resources = new(defaultLocale: "en-US");

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
                catch
                {
                }
            }
        });

        UseEffect(() =>
        {
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

        var locale = settings.LanguageIndex switch
        {
            1 => "zh-CN",
            2 => "en-US",
            _ => SystemLocale(),
        };
        var theme = settings.ThemeIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        var startHidden = AppPlatform.StartHidden && settings.MinimizeToTray;
        var (splashVisible, _) = UseState(!startHidden);

        var shell = LocaleProvider(
            locale,
            Component<LocalizedAppShell, LocalizedAppShellProps>(new(settings, updateSettings, locale)),
            Resources,
            defaultLocale: "en-US")
            .RequestedTheme(theme);

        if (!splashVisible)
            return shell.Backdrop(BackdropKind.Mica);

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                shell.Grid(row: 0, column: 0),
                Component<StartupSplashOverlay>().Grid(row: 0, column: 0))
            .RequestedTheme(theme)
            .Backdrop(BackdropKind.Mica);
    }

    private static string SystemLocale() => GlobalizationPreferences.Languages.Any(
        static language => language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            ? "zh-CN"
            : "en-US";
}

sealed record LocalizedAppShellProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    string Locale);

sealed class LocalizedAppShell : Component<LocalizedAppShellProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var useTitleBarPaneToggle = !UseBreakpoint(640);
        var settings = Props.Settings;
        var updateSettings = Props.UpdateSettings;
        var navigation = UseNavigation(AppRoute.Receive);
        var navigationViewRef = UseRef<NavigationView?>(null);
        var (runtime, updateRuntime) = UseReducer(AppRuntimeState.Initial);
        var (outgoingTransfer, setOutgoingTransfer) = UseState<OutgoingTransferViewState?>(null);
        var (shareTargetPayload, setShareTargetPayload) = UseState<ShareTargetPayload?>(null);
        var (serverDesired, setServerDesired) = UseState(true);
        var (serverEpoch, updateServerEpoch) = UseReducer(0);
        var (httpsOverride, setHttpsOverride) = UseState<bool?>(null);
        var nodeRef = UseRef<LocalSendNode?>(null);
        var drainingActivationsRef = UseRef(false);
        var trayIcon = UseRef<WinUIEx.TrayIcon?>(null);
        var nodeLifecycleRef = UseRef<SemaphoreSlim?>(null);
        var nodeLifecycle = nodeLifecycleRef.Current ??= new SemaphoreSlim(1, 1);
        var nextNodeSession = UseRef(0);
        var ownerNodeSession = UseRef(0);
        var mouseBackHandler = UseRef<PointerEventHandler?>(null);
        var handleWidgetCommand = UseRef<Action<string>?>(null);

        UseEffect(() =>
        {
            EventHandler activationReceived = (_, _) => ScheduleActivationDrain();
            EventHandler notificationActivated = (_, _) => ScheduleActivationDrain();
            ShareTargetActivationBroker.ActivationReceived += activationReceived;
            AppNotificationService.Activated += notificationActivated;
            ScheduleActivationDrain();
            return () =>
            {
                ShareTargetActivationBroker.ActivationReceived -= activationReceived;
                AppNotificationService.Activated -= notificationActivated;
            };
        });

        UseEffect(() =>
        {
            if (window is null)
                return () => { };

            void OnClosing(object? sender, WindowClosingEventArgs args)
            {
                if (args.Reason != WindowCloseReason.UserClosed || !settings.MinimizeToTray)
                    return;

                args.Cancel = true;
                HideToTray();
            }

            window.Closing += OnClosing;
            return () => window.Closing -= OnClosing;
        }, window, settings.MinimizeToTray);

        UseEffect(() =>
        {
            ReactorApp.ShutdownPolicy = settings.MinimizeToTray
                ? ShutdownPolicy.Explicit
                : ShutdownPolicy.OnLastSurfaceClosed;

            if (!settings.MinimizeToTray)
            {
                trayIcon.Current?.Dispose();
                trayIcon.Current = null;
                return () => { };
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (!File.Exists(iconPath))
                iconPath = AppPlatform.ExecutablePath;

            var icon = new WinUIEx.TrayIcon(1, iconPath, t.Message(new("App", "TrayTooltip")));
            icon.Selected += (_, _) => RestoreWindow();
            icon.LeftDoubleClick += (_, _) => RestoreWindow();
            icon.ContextMenu += (_, args) =>
            {
                var flyout = new MenuFlyout();
                var open = new MenuFlyoutItem { Text = t.Message(new("App", "TrayOpen")) };
                open.Click += (_, _) => RestoreWindow();
                var exit = new MenuFlyoutItem { Text = t.Message(new("App", "TrayExit")) };
                exit.Click += (_, _) =>
                {
                    icon.Dispose();
                    trayIcon.Current = null;
                    ReactorApp.Exit();
                };
                flyout.Items.Add(open);
                flyout.Items.Add(new MenuFlyoutSeparator());
                flyout.Items.Add(exit);
                args.Flyout = flyout;
            };
            icon.IsVisible = true;
            trayIcon.Current = icon;

            return () =>
            {
                icon.Dispose();
                if (ReferenceEquals(trayIcon.Current, icon))
                    trayIcon.Current = null;
            };
        }, settings.MinimizeToTray, t.Locale);

        UseEffect(() =>
        {
            if (runtime.IncomingTransfers.Count > 0)
                RestoreWindow();
        }, runtime.IncomingTransfers.Count);

        UseEffect(() =>
        {
            var session = ++nextNodeSession.Current;
            var cancellation = new CancellationTokenSource();
            _ = RunNodeSessionAsync(session, serverDesired, cancellation.Token);
            return () =>
            {
                cancellation.Cancel();
                _ = CleanupNodeSessionAsync();

                async Task CleanupNodeSessionAsync()
                {
                    try
                    {
                        await DisposeNodeSessionAsync(session).ConfigureAwait(false);
                    }
                    finally
                    {
                        cancellation.Dispose();
                    }
                }
            };
        }, serverDesired, serverEpoch, httpsOverride);

        UseEffect(() => WidgetAppHost.Update(runtime, settings, outgoingTransfer, serverDesired),
            runtime,
            settings,
            serverDesired,
            outgoingTransfer is null,
            outgoingTransfer?.BytesTransferred ?? 0,
            outgoingTransfer?.TotalBytes ?? 0,
            (int?)outgoingTransfer?.State ?? -1);

        UseEffect(() =>
        {
            void OnCommand(string verb) => handleWidgetCommand.Current?.Invoke(verb);
            WidgetAppHost.CommandReceived += OnCommand;
            return () => WidgetAppHost.CommandReceived -= OnCommand;
        });

        handleWidgetCommand.Current = verb =>
        {
            if (string.Equals(verb, "open", StringComparison.OrdinalIgnoreCase))
            {
                RestoreWindow();
                return;
            }

            if (string.Equals(verb, "stop-server", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(verb, "toggle-server", StringComparison.OrdinalIgnoreCase) && serverDesired))
            {
                StopServer();
                return;
            }

            if (string.Equals(verb, "start-server", StringComparison.OrdinalIgnoreCase)
                || string.Equals(verb, "toggle-server", StringComparison.OrdinalIgnoreCase))
            {
                StartOrRestartServer();
            }
        };

        var titleBar = (TitleBar("Tonarink") with
        {
            Subtitle = t.Message(new("App", "Tagline")),
            RightHeader = Caption(NodeStatusText(t, runtime.NodeState, runtime.DiscoveryWarning))
                .Foreground(runtime.Error is not null
                    ? Theme.SystemCritical
                    : runtime.DiscoveryWarning is not null
                        ? Theme.SystemCaution
                        : Theme.SecondaryText),
        })
        .WithNavigation(navigation)
        .PaneToggleButtonVisible(useTitleBarPaneToggle)
        .PaneToggleRequested(() =>
        {
            if (navigationViewRef.Current is { } navigationView)
                navigationView.IsPaneOpen = !navigationView.IsPaneOpen;
        })
        .Tall()
        .Flex(shrink: 0);

        var content = NavigationHost(navigation, route => route switch
        {
            AppRoute.Receive => Component<ReceivePage, ReceivePageProps>(new(
                runtime,
                settings,
                updateSettings)),
            AppRoute.History => Component<HistoryPage, HistoryPageProps>(
                new(settings.DownloadDirectory)),
            AppRoute.Send => Component<SendPage, SendPageProps>(new(
                runtime,
                nodeRef.Current,
                RefreshAsync,
                setOutgoingTransfer,
                shareTargetPayload,
                ConsumeShareTargetPayload)),
            AppRoute.Settings => Component<SettingsPage, SettingsPageProps>(new(
                settings,
                runtime,
                updateSettings,
                StartOrRestartServer,
                StopServer))
                .WithKey($"settings:{Props.Locale}"),
            AppRoute.NetworkInterfaces => Component<NetworkInterfacesPage, NetworkInterfacesPageProps>(
                new(settings, updateSettings)),
            AppRoute.WebShare => Component<WebSharePage, WebSharePageProps>(new(
                nodeRef.Current,
                runtime,
                settings,
                SetHttpsOverride,
                WebShareMode.Send)),
            AppRoute.WebReceive => Component<WebSharePage, WebSharePageProps>(new(
                nodeRef.Current,
                runtime,
                settings,
                SetHttpsOverride,
                WebShareMode.Receive)),
            _ => TextBlock(t.Message(new("App", "PageNotFound"))),
        }) with
        {
            CacheMode = NavigationCacheMode.Enabled,
            CacheSize = 3,
            Transition = AppNavigation.IsDetail(navigation.CurrentRoute)
                ? NavigationTransition.DrillIn()
                : NavigationTransition.Slide(),
        };

        var navigationView = (NavigationView(
            [
                NavItem(t.Message(new("App", "NavReceive")), icon: "\uE701", tag: RouteTag(AppRoute.Receive)),
                NavItem(t.Message(new("App", "NavSend")), icon: "Send", tag: RouteTag(AppRoute.Send)),
                NavItem(t.Message(new("App", "NavSettings")), icon: "Setting", tag: RouteTag(AppRoute.Settings)),
            ],
            content)
            .WithNavigation(navigation, RouteTag, ParseRoute)
            .PaneDisplayMode(NavigationViewPaneDisplayMode.Auto)
            .CompactModeThresholdWidth(640)
            .ExpandedModeThresholdWidth(1008)
            .OpenPaneLength(248)
            .CompactPaneLength(56)
            .PaneToggleButtonVisible(!useTitleBarPaneToggle)
            .AlwaysShowHeader(false)
            .BackButtonVisible(false)
            .TitleBarAutoPadding(false)
            .OnMountAdd(element => navigationViewRef.Current = element as NavigationView)
            .OnUnmountAdd(element =>
            {
                if (ReferenceEquals(navigationViewRef.Current, element))
                    navigationViewRef.Current = null;
            })
            .Flex(grow: 1, basis: 0)) with
        {
            IsSettingsVisible = false,
        };

        var pendingIncoming = runtime.IncomingTransfers.FirstOrDefault();
        Element? transferOverlay = pendingIncoming is not null && nodeRef.Current is { } node
            ? Component<IncomingTransferOverlay, IncomingTransferOverlayProps>(new(
                    node,
                    pendingIncoming,
                    settings.DownloadDirectory,
                    DismissIncoming))
                .WithKey(pendingIncoming.RequestId.ToString("N"))
            : outgoingTransfer is not null
                ? Component<OutgoingTransferOverlay, OutgoingTransferOverlayProps>(new(
                    outgoingTransfer,
                    () => setOutgoingTransfer(null)))
                    .WithKey(outgoingTransfer.Receiver.Fingerprint)
                : null;

        var overlayVisible = transferOverlay is not null;
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
                Border(transferOverlay)
                    .IsHitTestVisible(overlayVisible)
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

        void RestoreWindow()
        {
            if (window is null)
                return;

            if (!window.Spec.ShowInTaskbar)
                window.Update(window.Spec with { ShowInTaskbar = true });
            window.Show();
            window.Activate();
        }

        void HideToTray()
        {
            if (window is null)
                return;

            window.Hide();
            if (window.Spec.ShowInTaskbar)
                window.Update(window.Spec with { ShowInTaskbar = false });
        }

        void DismissIncoming(Guid requestId)
        {
            updateRuntime(current => current with
            {
                IncomingTransfers = current.IncomingTransfers
                    .Where(request => request.RequestId != requestId)
                    .ToArray(),
            });
        }

        void ConsumeShareTargetPayload(Guid payloadId)
        {
            if (shareTargetPayload?.Id == payloadId)
                setShareTargetPayload(null);
        }

        void ScheduleActivationDrain()
        {
            var dispatcher = ReactorApp.UIDispatcher;
            if (dispatcher is null)
                return;

            if (dispatcher.HasThreadAccess)
                DrainActivations();
            else
                dispatcher.TryEnqueue(DrainActivations);
        }

        void DrainActivations()
        {
            if (drainingActivationsRef.Current)
                return;

            drainingActivationsRef.Current = true;
            try
            {
                RestoreWindow();
                while (ShareTargetActivationBroker.TryDequeue(out var payload))
                {
                    if (payload is null)
                        continue;

                    setShareTargetPayload(payload);
                    if (navigation.CurrentRoute != AppRoute.Send)
                        navigation.Navigate(AppRoute.Send);
                    RestoreWindow();
                }
            }
            finally
            {
                drainingActivationsRef.Current = false;
                if (ShareTargetActivationBroker.HasPendingActivations)
                    ScheduleActivationDrain();
            }
        }

        void StartOrRestartServer()
        {
            updateRuntime(current => current with
            {
                NodeState = LocalSendNodeState.Starting,
                Devices = [],
                IncomingTransfers = [],
                Error = null,
                DiscoveryWarning = null,
            });
            setServerDesired(true);
            updateServerEpoch(epoch => epoch + 1);
        }

        void SetHttpsOverride(bool? value)
        {
            if (httpsOverride == value)
                return;
            setHttpsOverride(value);
            if (serverDesired)
                StartOrRestartServer();
        }

        void StopServer()
        {
            if (!serverDesired && runtime.NodeState is LocalSendNodeState.Stopped
                or LocalSendNodeState.Created or LocalSendNodeState.Disposed)
                return;

            updateRuntime(current => current with
            {
                NodeState = LocalSendNodeState.Stopping,
                Devices = [],
                IncomingTransfers = [],
                Error = null,
                DiscoveryWarning = null,
            });
            setServerDesired(false);
        }

        async Task RunNodeSessionAsync(int session, bool desired, CancellationToken cancellationToken)
        {
            await nodeLifecycle.WaitAsync().ConfigureAwait(false);
            LocalSendNode? node = null;
            try
            {
                await DisposeCurrentNodeCoreAsync().ConfigureAwait(false);
                if (!desired || cancellationToken.IsCancellationRequested)
                {
                    updateRuntime(current => current with
                    {
                        NodeState = LocalSendNodeState.Stopped,
                        Devices = [],
                        IncomingTransfers = [],
                        Error = null,
                        DiscoveryWarning = null,
                    });
                    return;
                }

                node = new LocalSendNode(new LocalSendOptions
                {
                    Alias = settings.ResolvedAlias,
                    DeviceModel = settings.ResolvedDeviceModel,
                    DeviceType = settings.DeviceType,
                    DataDirectory = AppPlatform.DataDirectory,
                    DownloadDirectory = settings.DownloadDirectory,
                    Port = settings.Port,
                    EnableHttps = httpsOverride ?? settings.EnableHttps,
                    MulticastAddress = settings.ResolvedMulticastAddress,
                    DiscoveryTimeout = TimeSpan.FromMilliseconds(Math.Max(1, settings.DiscoveryTimeoutMs)),
                    NetworkWhitelist = settings.NetworkWhitelist,
                    NetworkBlacklist = settings.NetworkBlacklist,
                });
                nodeRef.Current = node;
                ownerNodeSession.Current = session;
                updateRuntime(current => current with
                {
                    NodeState = LocalSendNodeState.Starting,
                    Devices = [],
                    IncomingTransfers = [],
                    Error = null,
                    DiscoveryWarning = null,
                });

                await node.StartAsync(cancellationToken).ConfigureAwait(false);
                updateRuntime(current => current with
                {
                    NodeState = node.State,
                    Identity = node.Identity,
                    Devices = node.GetDevices(),
                    Error = null,
                    AppliedMulticastGroup = settings.ResolvedMulticastAddress.ToString(),
                    DiscoveryWarning = node.DiscoveryError,
                    AppliedNetworkWhitelist = settings.NetworkWhitelist,
                    AppliedNetworkBlacklist = settings.NetworkBlacklist,
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                updateRuntime(current => current with
                {
                    NodeState = node?.State ?? LocalSendNodeState.Faulted,
                    Error = exception.Message,
                    DiscoveryWarning = null,
                });
                return;
            }
            finally
            {
                nodeLifecycle.Release();
            }

            if (node is null || cancellationToken.IsCancellationRequested)
                return;

            try
            {
                await Task.WhenAll(
                    WatchDevicesAsync(node, cancellationToken),
                    WatchIncomingTransfersAsync(node, cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        async Task DisposeNodeSessionAsync(int session)
        {
            await nodeLifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ownerNodeSession.Current != session)
                    return;

                await DisposeCurrentNodeCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                nodeLifecycle.Release();
            }
        }

        async Task DisposeCurrentNodeCoreAsync()
        {
            ownerNodeSession.Current = 0;
            if (nodeRef.Current is not { } node)
                return;

            nodeRef.Current = null;
            try
            {
                await node.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        async Task WatchDevicesAsync(LocalSendNode node, CancellationToken cancellationToken)
        {
            await foreach (var _ in node.WatchDeviceChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                updateRuntime(current => current with
                {
                    Devices = node.GetDevices(),
                });
            }
        }

        async Task WatchIncomingTransfersAsync(LocalSendNode node, CancellationToken cancellationToken)
        {
            await foreach (var request in node.WatchIncomingTransfersAsync(cancellationToken).ConfigureAwait(false))
            {
                AppNotificationService.Show(
                    t.Message(
                        new("App", "NotificationIncomingTitle"),
                        ("device", request.Sender.Alias)),
                    TransferOverlayVisuals.IncomingSummary(t, request.Items),
                    "incoming-request");

                var currentSettings = AppSettingsStore.Load();
                var autoAccept = currentSettings.AutoSave switch
                {
                    AutoSaveMode.On => true,
                    AutoSaveMode.Favorites => FavoriteDeviceStore.Contains(request.Sender.Fingerprint),
                    _ => false,
                };
                if (autoAccept)
                {
                    _ = AutoAcceptIncomingAsync(
                        node,
                        request,
                        currentSettings.DownloadDirectory,
                        cancellationToken);
                    continue;
                }

                updateRuntime(current => current with
                {
                    IncomingTransfers = [.. current.IncomingTransfers, request],
                });
            }
        }

        async Task AutoAcceptIncomingAsync(
            LocalSendNode node,
            IncomingTransferRequest request,
            string downloadDirectory,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await node.AcceptAsync(
                    request.RequestId,
                    new AcceptTransferOptions
                    {
                        DestinationDirectory = downloadDirectory,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    var message = result.Failure?.Message ?? t.Message(new("App", "ReceiveFailed"));
                    AppNotificationService.Show(
                        t.Message(new("App", "ReceiveFailed")),
                        message,
                        "receive-failed");
                    updateRuntime(current => current with { Error = message });
                    return;
                }

                ReceiveHistoryStore.Record(request.Sender.Alias, result);
                AppNotificationService.Show(
                    t.Message(new("App", "NotificationReceiveCompleteTitle")),
                    request.Items.Count == 1
                        ? t.Message(
                            new("App", "NotificationReceiveCompleteOne"),
                            ("device", request.Sender.Alias))
                        : t.Message(
                            new("App", "NotificationReceiveCompleteMany"),
                            ("count", request.Items.Count),
                            ("device", request.Sender.Alias)),
                    "receive-complete");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                AppNotificationService.Show(
                    t.Message(new("App", "ReceiveFailed")),
                    exception.Message,
                    "receive-failed");
                updateRuntime(current => current with { Error = exception.Message });
            }
        }

        async Task RefreshAsync()
        {
            var node = nodeRef.Current;
            if (node?.State != LocalSendNodeState.Running)
                return;

            try
            {
                await node.RefreshAsync().ConfigureAwait(false);
                updateRuntime(current => current with
                {
                    Devices = node.GetDevices(),
                    Error = null,
                    DiscoveryWarning = node.DiscoveryError,
                });
            }
            catch (Exception exception)
            {
                updateRuntime(current => current with { Error = exception.Message });
            }
        }


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
        _ => "receive",
    };

    private static AppRoute ParseRoute(string tag) => tag switch
    {
        "send" => AppRoute.Send,
        "settings" => AppRoute.Settings,
        _ => AppRoute.Receive,
    };

    private static string NodeStatusText(IntlAccessor t, LocalSendNodeState state, string? discoveryWarning) => state switch
    {
        LocalSendNodeState.Starting => t.Message(new("App", "NodeStarting")),
        LocalSendNodeState.Running when discoveryWarning is not null => t.Message(new("App", "NodeDiscoveryLimited")),
        LocalSendNodeState.Running => t.Message(new("App", "NodeRunning")),
        LocalSendNodeState.Faulted => t.Message(new("App", "NodeFaulted")),
        LocalSendNodeState.Stopping => t.Message(new("App", "NodeStopping")),
        _ => t.Message(new("App", "NodeDisconnected")),
    };
}
