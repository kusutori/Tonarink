using CommunityToolkit.WinUI.Controls;
using LocalSendDotNet;
using Tonarink.Application;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Tonarink.Components.Animations;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SettingsCardElement;
using static Tonarink.Controls.SettingsExpanderElement;

namespace Tonarink.Pages.Settings;

sealed class SettingsPage : Component<SettingsPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var (_, refreshCachedPage) = UseReducer(0);
        // NavigationHost updates the cached component's props without reconciling its
        // element tree. Match SendPage and DeviceDetailsPage: request one render before
        // the page transition so the restored page uses the latest settings props.
        UseNavigationLifecycle(onNavigatingTo: _ =>
            refreshCachedPage(value => value + 1));

        var storagePicker = new StoragePicker(
            window?.NativeWindow,
            t.Message(new("App", "WindowUnavailable")));
        var navigation = UseNavigation<AppRoute>();
        var (statusMessage, setStatusMessage) = UseState<string?>(null);
        var floatingInfo = UseContext(FloatingInfoBarHost.Slot);
        floatingInfo.Owner = AppRoute.Settings;
        floatingInfo.Build = statusMessage is null
                             && Props.Runtime.Error is null
                             && Props.Runtime.DiscoveryWarning is null
            ? null
            : () => VStack(4,
                    statusMessage is null
                        ? null
                        : Component<FloatingInfoBarItem, FloatingInfoBarItemProps>(new(
                                (InfoBar(t.Message(new("App", "SettingsTitle")), statusMessage) with
                                {
                                    IsOpen = true,
                                    IsClosable = true,
                                    OnClosed = () => setStatusMessage(null),
                                }).Severity(InfoBarSeverity.Error)))
                            .WithKey("settings-status"),
                    Props.Runtime.Error is null
                        ? null
                        : Component<FloatingInfoBarItem, FloatingInfoBarItemProps>(new(
                                (InfoBar(t.Message(new("App", "NetworkStartFailed")), Props.Runtime.Error) with
                                {
                                    IsOpen = true,
                                    IsClosable = false,
                                }).Severity(InfoBarSeverity.Error)))
                            .WithKey("settings-runtime-error"),
                    Props.Runtime.DiscoveryWarning is null
                        ? null
                        : Component<FloatingInfoBarItem, FloatingInfoBarItemProps>(new(
                                (InfoBar(
                                    t.Message(new("App", "NodeDiscoveryLimited")),
                                    Props.Runtime.DiscoveryWarning) with
                                {
                                    IsOpen = true,
                                    IsClosable = false,
                                }).Severity(InfoBarSeverity.Warning)))
                            .WithKey("settings-discovery-warning"));
        UseEffect(
            () => floatingInfo.Invalidate(),
            statusMessage,
            Props.Runtime.Error,
            Props.Runtime.DiscoveryWarning);
        var (encryptionNoticeOpen, setEncryptionNoticeOpen) = UseState(false);
        var (previewOverridePath, setPreviewOverridePath) = UseState("");
        var nodeState = Props.Runtime.NodeState;
        var serverBusy = nodeState is LocalSendNodeState.Starting or LocalSendNodeState.Stopping;
        var serverRunning = nodeState == LocalSendNodeState.Running;
        var serverOnline = nodeState is LocalSendNodeState.Running or LocalSendNodeState.Starting;
        var needsRestart = serverRunning
                           && Props.Runtime.Identity is { } identity
                           && (
                               !string.Equals(identity.Alias, Props.Settings.ResolvedAlias, StringComparison.Ordinal)
                               || identity.DeviceType != Props.Settings.DeviceType
                               || !string.Equals(identity.DeviceModel ?? "", Props.Settings.ResolvedDeviceModel,
                                   StringComparison.Ordinal)
                               || identity.Port != Props.Settings.Port
                               || (identity.Protocol == LocalSendProtocol.Https) != Props.Settings.EnableHttps
                               || !string.Equals(
                                   Props.Runtime.AppliedMulticastGroup,
                                   Props.Settings.ResolvedMulticastAddress.ToString(),
                                   StringComparison.Ordinal)
                               || !string.Equals(
                                   Props.Runtime.AppliedReceivePin,
                                   Props.Settings.ResolvedReceivePin,
                                   StringComparison.Ordinal)
                               || !SameStringList(Props.Runtime.AppliedNetworkWhitelist,
                                   Props.Settings.NetworkWhitelist)
                               || !SameStringList(Props.Runtime.AppliedNetworkBlacklist,
                                   Props.Settings.NetworkBlacklist));
        Element[] deviceTypeOptions =
        [
            DeviceTypeOption(LocalSendDeviceType.Desktop, t.Message(new("App", "DeviceDesktop"))),
            DeviceTypeOption(LocalSendDeviceType.Mobile, t.Message(new("App", "DeviceMobile"))),
            DeviceTypeOption(LocalSendDeviceType.Web, t.Message(new("App", "DeviceWeb"))),
            DeviceTypeOption(LocalSendDeviceType.Headless, t.Message(new("App", "DeviceHeadless"))),
            DeviceTypeOption(LocalSendDeviceType.Server, t.Message(new("App", "DeviceServer"))),
        ];
        string[] themeOptions =
        [
            t.Message(new("App", "OptionSystem")),
            t.Message(new("App", "ThemeLight")),
            t.Message(new("App", "ThemeDark")),
        ];
        string[] languageOptions =
        [
            .. AppLanguages.Choices.Select(choice => t.Message(new("App", choice.NameKey))),
        ];
        string[] notificationDefaultActionOptions =
        [
            t.Message(new("App", "SettingsNotificationsDefaultOpenFile")),
            t.Message(new("App", "SettingsNotificationsDefaultShowInFolder")),
        ];
        string[] filePreviewProviderOptions =
        [
            t.Message(new("App", "SettingsFilePreviewProviderPowerToysPeek")),
            t.Message(new("App", "SettingsFilePreviewProviderQuickLook")),
        ];
        var generalCards = SettingsGroup(
            t.Message(new("App", "SettingsGeneral")),
            SettingsCard(
                header: t.Message(new("App", "SettingsTheme")),
                description: t.Message(new("App", "SettingsThemeDescription")),
                headerIcon: HeaderGlyph(AppIcons.Theme),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ComboBox(themeOptions, Props.Settings.ThemeIndex, index =>
                    {
                        if (index is >= 0 and <= 2 && index != Props.Settings.ThemeIndex)
                            Props.UpdateSettings(settings => settings with { ThemeIndex = index });
                    })
                    .AutomationName(t.Message(new("App", "SettingsTheme")))
                    .HelpText(t.Message(new("App", "SettingsThemeDescription")))
                    .MinWidth(180)),
            SettingsCard(
                header: t.Message(new("App", "SettingsLanguage")),
                description: t.Message(new("App", "SettingsLanguageDescription")),
                headerIcon: HeaderGlyph(AppIcons.Language),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ComboBox(languageOptions, Props.Settings.LanguageIndex, index =>
                    {
                        if (AppLanguages.IsValidIndex(index) && index != Props.Settings.LanguageIndex)
                            Props.UpdateSettings(settings => settings with { LanguageIndex = index });
                    })
                    .AutomationName(t.Message(new("App", "SettingsLanguage")))
                    .HelpText(t.Message(new("App", "SettingsLanguageDescription")))
                    .MinWidth(180)),
            SettingsCard(
                header: t.Message(new("App", "SettingsMinimizeToTray")),
                description: t.Message(new("App", "SettingsMinimizeToTrayDescription")),
                headerIcon: HeaderGlyph(AppIcons.MinimizeToTray),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.MinimizeToTray, value =>
                        Props.UpdateSettings(settings => settings with { MinimizeToTray = value }))
                    .AutomationName(t.Message(new("App", "SettingsMinimizeToTray")))
                    .HelpText(t.Message(new("App", "SettingsMinimizeToTrayDescription")))),
            SettingsCard(
                header: t.Message(new("App", "SettingsTrayClickOpensFlyout")),
                description: t.Message(new("App", "SettingsTrayClickOpensFlyoutDescription")),
                headerIcon: HeaderGlyph(AppIcons.TrayFlyout),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.TrayClickOpensFlyout, value =>
                    Props.UpdateSettings(settings => settings with { TrayClickOpensFlyout = value }))
                    .AutomationName(t.Message(new("App", "SettingsTrayClickOpensFlyout")))
                    .HelpText(t.Message(new("App", "SettingsTrayClickOpensFlyoutDescription")))
                    .IsEnabled(Props.Settings.MinimizeToTray)),
            SettingsCard(
                header: t.Message(new("App", "SettingsStartWithWindows")),
                description: t.Message(new("App", "SettingsStartWithWindowsDescription")),
                headerIcon: AnimatedButtons.SettingsStartupIcon(Props.Settings.StartWithWindows)
                    .WithKey("settings-startup-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.StartWithWindows, value =>
                        _ = SetStartupAsync(value))
                    .AutomationName(t.Message(new("App", "SettingsStartWithWindows")))
                    .HelpText(t.Message(new("App", "SettingsStartWithWindowsDescription")))),
            SettingsCard(
                header: t.Message(new("App", "SettingsWindowsShareSuggestions")),
                description: t.Message(new("App", "SettingsWindowsShareSuggestionsDescription")),
                headerIcon: HeaderGlyph(AppIcons.Contact),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.ShowFavoriteDevicesInWindowsShare, value =>
                    Props.UpdateSettings(settings => settings with
                    {
                        ShowFavoriteDevicesInWindowsShare = value,
                    }))
                    .AutomationName(t.Message(new("App", "SettingsWindowsShareSuggestions")))
                    .HelpText(t.Message(new("App", "SettingsWindowsShareSuggestionsDescription")))
                    .IsEnabled(AppPlatform.HasPackageIdentity())),
            SettingsExpander(
                    headerIcon: HeaderGlyph(AppIcons.Notification),
                    items:
                    [
                        SettingsCard(
                            header: t.Message(new("App", "SettingsNotificationsEnabled")),
                            description: t.Message(new("App", "SettingsNotificationsEnabledDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            ToggleSwitch(Props.Settings.NotificationsEnabled, value =>
                            {
                                Props.UpdateSettings(settings => settings with { NotificationsEnabled = value });
                                AppNotificationService.SetEnabled(value);
                            })
                                .AutomationName(t.Message(new("App", "SettingsNotificationsEnabled")))
                                .HelpText(t.Message(new("App", "SettingsNotificationsEnabledDescription")))),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsNotificationsDefaultAction")),
                            description: t.Message(new("App", "SettingsNotificationsDefaultActionDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            ComboBox(
                                    notificationDefaultActionOptions,
                                    (int)Props.Settings.NotificationDefaultAction,
                                    index =>
                                    {
                                        if (Enum.IsDefined(typeof(NotificationDefaultAction), index))
                                        {
                                            Props.UpdateSettings(settings => settings with
                                            {
                                                NotificationDefaultAction = (NotificationDefaultAction)index,
                                            });
                                        }
                                    })
                                .AutomationName(t.Message(new("App", "SettingsNotificationsDefaultAction")))
                                .HelpText(t.Message(new("App", "SettingsNotificationsDefaultActionDescription")))
                                .MinWidth(180)),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsNotificationsTest")),
                            description: t.Message(new("App", "SettingsNotificationsTestDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            Button(t.Message(new("App", "SettingsNotificationsSendTest")), () =>
                                {
                                    var shown = AppNotificationService.TryShow(
                                        t.Message(new("App", "SettingsNotificationsTestTitle")),
                                        t.Message(new("App", "SettingsNotificationsTestMessage")),
                                        "test");
                                    setStatusMessage(shown
                                        ? null
                                        : t.Message(new("App", "SettingsNotificationsTestFailed")));
                                })
                                .AutomationName(t.Message(new("App", "SettingsNotificationsSendTest")))
                                .IsEnabled(Props.Settings.NotificationsEnabled)),
                    ])
                .Set(expander =>
                {
                    expander.Header = t.Message(new("App", "SettingsNotifications"));
                    expander.Description = t.Message(new("App", "SettingsNotificationsDescription"));
                }),
            SettingsCard(
                header: t.Message(new("App", "SettingsExplorerContextMenu")),
                description: t.Message(new("App", "SettingsExplorerContextMenuDescription")),
                headerIcon: HeaderGlyph(AppIcons.ContextMenu),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.ShowExplorerContextMenu, value =>
                        Props.UpdateSettings(settings => settings with { ShowExplorerContextMenu = value }))
                    .AutomationName(t.Message(new("App", "SettingsExplorerContextMenu")))
                    .HelpText(t.Message(new("App", "SettingsExplorerContextMenuDescription")))
                    .IsEnabled(AppPlatform.HasPackageIdentity())));

        var receiveCards = SettingsGroup(
            t.Message(new("App", "SettingsReceive")),
            SettingsCard(
                header: t.Message(new("App", "SettingsSaveLocation")),
                description: Props.Settings.DownloadDirectory,
                headerIcon: HeaderGlyph(AppIcons.Folder),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                AnimatedButtons.OpenFolder(
                    t.Message(new("App", "ChangeSaveLocation")),
                    () => _ = PickDownloadDirectoryAsync(),
                    label: t.Message(new("App", "Change")))),
            SettingsCard(
                header: t.Message(new("App", "SettingsSaveReceiveHistory")),
                description: t.Message(new("App", "SettingsSaveReceiveHistoryDescription")),
                headerIcon: AnimatedButtons.SettingsHistoryIcon(Props.Settings.SaveReceiveHistory)
                    .WithKey("settings-history-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.SaveReceiveHistory, value =>
                        Props.UpdateSettings(settings => settings with { SaveReceiveHistory = value }))
                    .AutomationName(t.Message(new("App", "SettingsSaveReceiveHistory")))
                    .HelpText(t.Message(new("App", "SettingsSaveReceiveHistoryDescription")))),
            SettingsCard(
                header: t.Message(new("App", "SettingsVerifyChecksumsOnReceive")),
                description: t.Message(new("App", "SettingsVerifyChecksumsOnReceiveDescription")),
                headerIcon: HeaderGlyph(AppIcons.Checksum),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.VerifyChecksumsOnReceive, value =>
                        Props.UpdateSettings(settings => settings with { VerifyChecksumsOnReceive = value }))
                    .AutomationName(t.Message(new("App", "SettingsVerifyChecksumsOnReceive")))
                    .HelpText(t.Message(new("App", "SettingsVerifyChecksumsOnReceiveDescription")))),
            SettingsExpander(
                    headerIcon: HeaderGlyph(AppIcons.PinCode),
                    items:
                    [
                        SettingsCard(
                            header: t.Message(new("App", "SettingsReceivePinEnabled")),
                            description: t.Message(new("App", "SettingsReceivePinEnabledDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            ToggleSwitch(Props.Settings.ReceivePinEnabled, value =>
                                    Props.UpdateSettings(settings => settings with { ReceivePinEnabled = value }))
                                .AutomationName(t.Message(new("App", "SettingsReceivePinEnabled")))
                                .HelpText(t.Message(new("App", "SettingsReceivePinEnabledDescription")))),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsReceivePin")),
                            description: t.Message(new("App", "SettingsReceivePinDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            Component<DeferredPasswordSetting, DeferredPasswordSettingProps>(new(
                                    Props.Settings.ReceivePin,
                                    value => Props.UpdateSettings(settings => settings with { ReceivePin = value }),
                                    t.Message(new("App", "SettingsReceivePin")),
                                    t.Message(new("App", "PinPlaceholder")),
                                    MinWidth: 180))
                                .IsEnabled(Props.Settings.ReceivePinEnabled)),
                    ])
                .Set(expander =>
                {
                    expander.Header = t.Message(new("App", "SettingsReceivePinProtection"));
                    expander.Description = t.Message(new("App", "SettingsReceivePinProtectionDescription"));
                }));

        var sendCards = SettingsGroup(
            t.Message(new("App", "SettingsSend")),
            SettingsCard(
                header: t.Message(new("App", "SettingsExpandDragDrop")),
                description: t.Message(new("App", "SettingsExpandDragDropDescription")),
                headerIcon: HeaderGlyph(AppIcons.DragDrop),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.ExpandDragDropToEntireApp, value =>
                        Props.UpdateSettings(settings => settings with { ExpandDragDropToEntireApp = value }))
                    .AutomationName(t.Message(new("App", "SettingsExpandDragDrop")))
                    .HelpText(t.Message(new("App", "SettingsExpandDragDropDescription")))),
            SettingsCard(
                header: t.Message(new("App", "SettingsVerifyChecksumsOnSend")),
                description: t.Message(new("App", "SettingsVerifyChecksumsOnSendDescription")),
                headerIcon: HeaderGlyph(AppIcons.Checksum),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.VerifyChecksumsOnSend, value =>
                        Props.UpdateSettings(settings => settings with { VerifyChecksumsOnSend = value }))
                    .AutomationName(t.Message(new("App", "SettingsVerifyChecksumsOnSend")))
                    .HelpText(t.Message(new("App", "SettingsVerifyChecksumsOnSendDescription")))));

        var startOrRestartName = serverOnline
            ? t.Message(new("App", "SettingsRestartServer"))
            : t.Message(new("App", "SettingsStartServer"));
        var stopName = t.Message(new("App", "SettingsStopServer"));
        var serverDescription = needsRestart
            ? t.Message(new("App", "SettingsNeedRestart"))
            : nodeState switch
            {
                LocalSendNodeState.Running => t.Message(new("App", "SettingsServerRunning")),
                LocalSendNodeState.Starting => t.Message(new("App", "NodeStarting")),
                LocalSendNodeState.Stopping => t.Message(new("App", "NodeStopping")),
                _ => t.Message(new("App", "SettingsServerStopped")),
            };
        var networkCards = SettingsGroup(
            t.Message(new("App", "SettingsNetwork")),
            SettingsCard(
                header: serverOnline
                    ? t.Message(new("App", "DeviceServer"))
                    : t.Message(new("App", "SettingsServerOffline")),
                description: serverDescription,
                headerIcon: HeaderGlyph(AppIcons.Server),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                AnimatedButtons.SettingsServiceActions(
                    serverOnline,
                    nodeState == LocalSendNodeState.Starting,
                    serverBusy,
                    serverRunning,
                    Props.StartOrRestartServer,
                    Props.StopServer,
                    startOrRestartName,
                    stopName)),
            SettingsCard(
                header: t.Message(new("App", "SettingsDeviceName")),
                description: t.Message(new("App", "SettingsDeviceNameDescription")),
                headerIcon: HeaderGlyph(AppIcons.Rename),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                Component<DeferredTextSetting, DeferredTextSettingProps>(new(
                    Props.Settings.Alias,
                    value => Props.UpdateSettings(settings => settings with { Alias = value }),
                    t.Message(new("App", "SettingsDeviceName")),
                    MinWidth: 240))),
            SettingsExpander(
                    headerIcon: HeaderGlyph(AppIcons.Fingerprint),
                    items:
                    [
                        SettingsCard(
                            header: t.Message(new("App", "SettingsDeviceType")),
                            description: t.Message(new("App", "SettingsDeviceTypeDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            ComboBox(deviceTypeOptions, DeviceTypeIndex(Props.Settings.DeviceType), index =>
                                {
                                    var type = DeviceTypeFromIndex(index);
                                    if (type != Props.Settings.DeviceType)
                                        Props.UpdateSettings(settings => settings with { DeviceType = type });
                                })
                                .AutomationName(t.Message(new("App", "SettingsDeviceType")))
                                .HelpText(t.Message(new("App", "SettingsDeviceTypeDescription")))
                                .MinWidth(180)),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsDeviceModel")),
                            description: t.Message(new("App", "SettingsDeviceModelDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            Component<DeferredTextSetting, DeferredTextSettingProps>(new(
                                Props.Settings.DeviceModel,
                                value => Props.UpdateSettings(settings => settings with { DeviceModel = value }),
                                t.Message(new("App", "SettingsDeviceModel")),
                                PlaceholderText: Environment.MachineName,
                                MinWidth: 240))),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsPort")),
                            description: Props.Settings.Port == LocalSendOptions.DefaultPort
                                ? t.Message(new("App", "SettingsPortDescription"))
                                : t.Message(new("App", "SettingsPortWarning"), ("port", LocalSendOptions.DefaultPort)),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            Component<DeferredNumberSetting, DeferredNumberSettingProps>(new(
                                Props.Settings.Port,
                                value =>
                                {
                                    var port = (int)Math.Round(value);
                                    if (port is >= 1 and <= ushort.MaxValue && port != Props.Settings.Port)
                                        Props.UpdateSettings(settings => settings with { Port = port });
                                },
                                t.Message(new("App", "SettingsPort")),
                                1,
                                ushort.MaxValue,
                                MinWidth: 160))),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsNetworkInterfaces")),
                            description: NetworkInterfacesSummary(t, Props.Settings),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            Button(t.Message(new("App", "Change")),
                                    () => navigation.Navigate(AppRoute.NetworkInterfaces, AppNavigation.DrillIn))
                                .AutomationName(t.Message(new("App", "SettingsNetworkInterfaces")))),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsDiscoveryTimeout")),
                            description: t.Message(new("App", "SettingsDiscoveryTimeoutDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            Component<DeferredNumberSetting, DeferredNumberSettingProps>(new(
                                Props.Settings.DiscoveryTimeoutMs,
                                value =>
                                {
                                    var timeout = (int)Math.Round(value);
                                    if (timeout > 0 && timeout != Props.Settings.DiscoveryTimeoutMs)
                                        Props.UpdateSettings(settings =>
                                            settings with { DiscoveryTimeoutMs = timeout });
                                },
                                t.Message(new("App", "SettingsDiscoveryTimeout")),
                                1,
                                60_000,
                                MinWidth: 160))),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsEncryption")),
                            description: t.Message(new("App", "SettingsEncryptionDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            ToggleSwitch(Props.Settings.EnableHttps, value =>
                            {
                                Props.UpdateSettings(settings => settings with { EnableHttps = value });
                                if (!value)
                                    setEncryptionNoticeOpen(true);
                            })
                                .AutomationName(t.Message(new("App", "SettingsEncryption")))
                                .HelpText(t.Message(new("App", "SettingsEncryptionDescription")))),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsMulticast")),
                            description: string.Equals(
                                Props.Settings.ResolvedMulticastAddress.ToString(),
                                LocalSendOptions.DefaultMulticastAddress.ToString(),
                                StringComparison.Ordinal)
                                ? t.Message(new("App", "SettingsMulticastDescription"))
                                : t.Message(
                                    new("App", "SettingsMulticastWarning"),
                                    ("group", LocalSendOptions.DefaultMulticastAddress)),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            Component<DeferredTextSetting, DeferredTextSettingProps>(new(
                                Props.Settings.MulticastGroup,
                                value => Props.UpdateSettings(settings => settings with { MulticastGroup = value }),
                                t.Message(new("App", "SettingsMulticast")),
                                MinWidth: 180))),
                    ])
                .Set(expander =>
                {
                    expander.Header = t.Message(new("App", "SettingsAdvanced"));
                    expander.Description = t.Message(new("App", "SettingsAdvancedDescription"));
                }));

        var experimentalCards = SettingsGroup(
            t.Message(new("App", "SettingsExperimental")),
            SettingsExpander(
                    headerIcon: HeaderGlyph(AppIcons.Preview),
                    items:
                    [
                        SettingsCard(
                            header: t.Message(new("App", "SettingsFilePreviewEnabled")),
                            description: t.Message(new("App", "SettingsFilePreviewEnabledDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            ToggleSwitch(Props.Settings.FilePreviewEnabled, value =>
                                    Props.UpdateSettings(settings => settings with
                                    {
                                        FilePreviewEnabled = value,
                                    }))
                                .AutomationName(t.Message(new("App", "SettingsFilePreviewEnabled")))
                                .HelpText(t.Message(new("App", "SettingsFilePreviewEnabledDescription")))),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsFilePreviewProvider")),
                            description: t.Message(new("App", "SettingsFilePreviewProviderDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            ComboBox(filePreviewProviderOptions, (int)Props.Settings.PreviewProvider, index =>
                                {
                                    if (Enum.IsDefined(typeof(FilePreviewProvider), index))
                                    {
                                        Props.UpdateSettings(settings => settings with
                                        {
                                            PreviewProvider = (FilePreviewProvider)index,
                                        });
                                    }
                                })
                                .AutomationName(t.Message(new("App", "SettingsFilePreviewProvider")))
                                .HelpText(t.Message(new("App", "SettingsFilePreviewProviderDescription")))
                                .MinWidth(180)
                                .IsEnabled(Props.Settings.FilePreviewEnabled)),
                        SettingsCard(
                            header: t.Message(new("App", "SettingsFilePreviewPath")),
                            description: t.Message(new("App", "SettingsFilePreviewPathDescription")),
                            isClickEnabled: false,
                            isActionIconVisible: false,
                            content:
                            TextBox(
                                    previewOverridePath,
                                    setPreviewOverridePath,
                                    t.Message(new("App", "SettingsFilePreviewPathPlaceholder")))
                                .OnLostFocus((_, _) => ApplyPreviewOverride())
                                .AutomationName(t.Message(new("App", "SettingsFilePreviewPath")))
                                .MinWidth(280)
                                .IsEnabled(Props.Settings.FilePreviewEnabled)),
                    ])
                .Set(expander =>
                {
                    expander.Header = t.Message(new("App", "SettingsFilePreview"));
                    expander.Description = t.Message(new("App", "SettingsFilePreviewDescription"));
                }));

        var version = typeof(SettingsPage).Assembly.GetName().Version is { } assemblyVersion
            ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
            : "dev";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        var aboutLinks = VStack(12,
            TextBlock(t.Message(new("App", "SettingsAboutRelationship")))
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.WrapWholeWords),
            VStack(0,
                HyperlinkButton(
                    t.Message(new("App", "SettingsAboutGitHub")),
                    new Uri("https://github.com/kusutori/Tonarink")),
                HyperlinkButton(
                    t.Message(new("App", "SettingsAboutLocalSend")),
                    new Uri("https://localsend.org")),
                HyperlinkButton(
                    t.Message(new("App", "SettingsAboutIssues")),
                    new Uri("https://github.com/kusutori/Tonarink/issues")),
                HyperlinkButton(
                    t.Message(new("App", "SettingsAboutThirdPartyLicenses")),
                    new Uri("https://github.com/kusutori/Tonarink/blob/main/NOTICE"))));
        var aboutSection = VStack(4,
            Subtitle(t.Message(new("App", "SettingsAbout")))
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .Margin(bottom: 8),
            SettingsExpander(
                    headerIcon: Icon(ImageIcon(new Uri(iconPath, UriKind.Absolute))).AccessibilityHidden(),
                    items:
                    [
                        SettingsCard(
                                contentAlignment: ContentAlignment.Left,
                                isClickEnabled: false,
                                isActionIconVisible: false,
                                content: aboutLinks)
                            .HAlign(HorizontalAlignment.Stretch),
                    ])
                .Set(expander =>
                {
                    expander.Header = "Tonarink";
                    expander.Description = t.Message(
                        new("App", "SettingsAboutCopyright"),
                        ("year", DateTime.Now.Year));
                    expander.Content = t.Message(
                        new("App", "SettingsAboutVersion"),
                        ("version", version));
                })
                .HAlign(HorizontalAlignment.Stretch),
            HStack(8,
                    HyperlinkButton(
                        t.Message(new("App", "SettingsAboutFeedback")),
                        new Uri("https://github.com/kusutori/Tonarink/issues")),
                    HyperlinkButton(
                        t.Message(new("App", "SettingsAboutViewLogs")),
                        onClick: () =>
                        {
                            AppDiagnostics.EnsureLogFile();
                            ShellLauncher.Reveal(AppDiagnostics.LogFilePath);
                        })
                )
                .HAlign(HorizontalAlignment.Left)
                .Margin(top: 8));

        return ScrollView(
                VStack(24,
                        generalCards,
                        receiveCards,
                        sendCards,
                        networkCards,
                        experimentalCards,
                        aboutSection,
                        ContentDialog(
                                t.Message(new("App", "SettingsEncryptionDisabledTitle")),
                                TextBlock(t.Message(new("App", "SettingsEncryptionDisabledNotice")))
                                    .TextWrapping(TextWrapping.WrapWholeWords),
                                primaryButtonText: t.Message(new("App", "Close"))) with
                        {
                            IsOpen = encryptionNoticeOpen,
                            DefaultButton = ContentDialogButton.Close,
                            OnClosed = _ => setEncryptionNoticeOpen(false),
                        })
                    .Padding(AppLayout.PagePadding))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName(t.Message(new("App", "SettingsTitle")))
            .Landmark(AutomationLandmarkType.Main);

        async Task SetStartupAsync(bool enabled)
        {
            try
            {
                await WindowsStartup.SetEnabledAsync(enabled, Props.Settings.MinimizeToTray);
                Props.UpdateSettings(settings => settings with { StartWithWindows = enabled });
                setStatusMessage(null);
            }
            catch (StartupDisabledException exception)
            {
                setStatusMessage(t.Message(new("App", exception.ResourceKey)));
            }
            catch (Exception exception)
            {
                setStatusMessage(t.Message(
                    new("App", "StartupFailed"),
                    ("error", exception.Message)));
            }
        }

        async Task PickDownloadDirectoryAsync()
        {
            try
            {
                var folder = await storagePicker.PickFolderAsync(t.Message(new("App", "Change")));
                if (folder is null)
                    return;

                Props.UpdateSettings(settings => settings with { DownloadDirectory = folder.Path });
                setStatusMessage(null);
            }
            catch (Exception exception)
            {
                setStatusMessage(t.Message(
                    new("App", "PickFolderFailed"),
                    ("error", exception.Message)));
            }
        }

        void ApplyPreviewOverride()
        {
            if (string.IsNullOrWhiteSpace(previewOverridePath))
                return;

            if (!FilePreviewLauncher.TryResolveOverride(
                    previewOverridePath,
                    out var provider,
                    out var executablePath))
            {
                setStatusMessage(t.Message(new("App", "SettingsFilePreviewPathInvalid")));
                return;
            }

            Props.UpdateSettings(settings => provider switch
            {
                FilePreviewProvider.PowerToysPeek => settings with
                {
                    PreviewProvider = provider,
                    PowerToysPeekExecutablePath = executablePath,
                },
                FilePreviewProvider.QuickLook => settings with
                {
                    PreviewProvider = provider,
                    QuickLookExecutablePath = executablePath,
                },
                _ => settings,
            });
            setPreviewOverridePath("");
            setStatusMessage(null);
        }
    }

    private static Element HeaderGlyph(string glyph) =>
        Icon(glyph).AccessibilityHidden();

    private static Element DeviceTypeOption(LocalSendDeviceType type, string label) =>
        HStack(10,
                Icon(FontIcon(DeviceVisuals.DeviceTypeGlyph(type), fontSize: 16))
                    .Width(20)
                    .AccessibilityHidden(),
                TextBlock(label)
                    .VAlign(VerticalAlignment.Center))
            .WithKey(type.ToString());

    private static Element SettingsGroup(string title, params Element[] cards) =>
        VStack(4,
        [
            Subtitle(title)
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .Margin(bottom: 8),
            .. cards.Select(card => card.HAlign(HorizontalAlignment.Stretch)),
        ]);

    private static readonly LocalSendDeviceType[] DeviceTypes =
    [
        LocalSendDeviceType.Desktop,
        LocalSendDeviceType.Mobile,
        LocalSendDeviceType.Web,
        LocalSendDeviceType.Headless,
        LocalSendDeviceType.Server,
    ];

    private static int DeviceTypeIndex(LocalSendDeviceType type)
    {
        var index = Array.IndexOf(DeviceTypes, type);
        return index < 0 ? 0 : index;
    }

    private static LocalSendDeviceType DeviceTypeFromIndex(int index) =>
        index is >= 0 and < 5 ? DeviceTypes[index] : LocalSendDeviceType.Desktop;

    private static string NetworkInterfacesSummary(IntlAccessor t, AppSettings settings)
    {
        if (settings.NetworkWhitelist is not null)
            return t.Message(new("App", "SettingsNetworkInterfacesWhitelist"));
        if (settings.NetworkBlacklist is not null)
            return t.Message(new("App", "SettingsNetworkInterfacesBlacklist"));
        return t.Message(new("App", "SettingsNetworkInterfacesAll"));
    }

    private static bool SameStringList(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }
}
