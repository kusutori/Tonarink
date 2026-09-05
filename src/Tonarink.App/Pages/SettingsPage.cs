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
using Windows.Storage.Pickers;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.Toolkit.SettingsCardElement;
using static Tonarink.Controls.Toolkit.SettingsExpanderElement;

sealed record SettingsPageProps(
    AppSettings Settings,
    AppRuntimeState Runtime,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    Action StartOrRestartServer,
    Action StopServer);

sealed class SettingsPage : Component<SettingsPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var navigation = UseNavigation<AppRoute>();
        var (statusMessage, setStatusMessage) = UseState<string?>(null);
        var (encryptionNoticeOpen, setEncryptionNoticeOpen) = UseState(false);
        var nodeState = Props.Runtime.NodeState;
        var serverBusy = nodeState is LocalSendNodeState.Starting or LocalSendNodeState.Stopping;
        var serverRunning = nodeState == LocalSendNodeState.Running;
        var serverOnline = nodeState is LocalSendNodeState.Running or LocalSendNodeState.Starting;
        var needsRestart = serverRunning
            && Props.Runtime.Identity is { } identity
            && (
                !string.Equals(identity.Alias, Props.Settings.ResolvedAlias, StringComparison.Ordinal)
                || identity.DeviceType != Props.Settings.DeviceType
                || !string.Equals(identity.DeviceModel ?? "", Props.Settings.ResolvedDeviceModel, StringComparison.Ordinal)
                || identity.Port != Props.Settings.Port
                || (identity.Protocol == LocalSendProtocol.Https) != Props.Settings.EnableHttps
                || !string.Equals(
                    Props.Runtime.AppliedMulticastGroup,
                    Props.Settings.ResolvedMulticastAddress.ToString(),
                    StringComparison.Ordinal)
                || !SameStringList(Props.Runtime.AppliedNetworkWhitelist, Props.Settings.NetworkWhitelist)
                || !SameStringList(Props.Runtime.AppliedNetworkBlacklist, Props.Settings.NetworkBlacklist));
        var deviceTypeOptions = new[]
        {
            t.Message(new("App", "DeviceDesktop")),
            t.Message(new("App", "DeviceMobile")),
            t.Message(new("App", "DeviceWeb")),
            t.Message(new("App", "DeviceHeadless")),
            t.Message(new("App", "DeviceServer")),
        };
        string[] themeOptions =
        [
            t.Message(new("App", "OptionSystem")),
            t.Message(new("App", "ThemeLight")),
            t.Message(new("App", "ThemeDark")),
        ];
        string[] languageOptions =
        [
            t.Message(new("App", "OptionSystem")),
            t.Message(new("App", "LanguageChinese")),
            t.Message(new("App", "LanguageEnglish")),
        ];
        string[] notificationDefaultActionOptions =
        [
            t.Message(new("App", "SettingsNotificationsDefaultOpenFile")),
            t.Message(new("App", "SettingsNotificationsDefaultShowInFolder")),
        ];

        var generalCards = SettingsGroup(
            t.Message(new("App", "SettingsGeneral")),
            SettingsCard(
                header: t.Message(new("App", "SettingsTheme")),
                description: t.Message(new("App", "SettingsThemeDescription")),
                headerIcon: HeaderGlyph("\uE771"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ComboBox(themeOptions, Props.Settings.ThemeIndex, index =>
                {
                    if (index is >= 0 and <= 2 && index != Props.Settings.ThemeIndex)
                        Props.UpdateSettings(settings => settings with { ThemeIndex = index });
                })
                    .MinWidth(180)),
            SettingsCard(
                header: t.Message(new("App", "SettingsLanguage")),
                description: t.Message(new("App", "SettingsLanguageDescription")),
                headerIcon: HeaderGlyph("\uF2B7"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ComboBox(languageOptions, Props.Settings.LanguageIndex, index =>
                {
                    if (index is >= 0 and <= 2 && index != Props.Settings.LanguageIndex)
                        Props.UpdateSettings(settings => settings with { LanguageIndex = index });
                })
                    .MinWidth(180)),
            SettingsCard(
                header: t.Message(new("App", "SettingsMinimizeToTray")),
                description: t.Message(new("App", "SettingsMinimizeToTrayDescription")),
                headerIcon: HeaderGlyph("\uED1A"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.MinimizeToTray, value =>
                    Props.UpdateSettings(settings => settings with { MinimizeToTray = value }))),
            SettingsCard(
                header: t.Message(new("App", "SettingsStartWithWindows")),
                description: t.Message(new("App", "SettingsStartWithWindowsDescription")),
                headerIcon: HeaderGlyph("\uEC4A"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.StartWithWindows, value =>
                    _ = SetStartupAsync(value))),
            SettingsExpander(
                headerIcon: HeaderGlyph("\uEA8F"),
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
                        })),
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
                headerIcon: HeaderGlyph("\uE7AC"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                ToggleSwitch(Props.Settings.ShowExplorerContextMenu, value =>
                    Props.UpdateSettings(settings => settings with { ShowExplorerContextMenu = value }))));

        var receiveCards = SettingsGroup(
            t.Message(new("App", "SettingsReceive")),
            SettingsCard(
                header: t.Message(new("App", "SettingsSaveLocation")),
                description: Props.Settings.DownloadDirectory,
                headerIcon: HeaderGlyph("\uE8B7"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                Button(
                    HStack(8,
                        Icon("\uE8DA").AccessibilityHidden(),
                        TextBlock(t.Message(new("App", "Change")))),
                    () => _ = PickDownloadDirectoryAsync())
                    .AutomationName(t.Message(new("App", "ChangeSaveLocation")))));

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
                headerIcon: HeaderGlyph("\uE703"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                HStack(4,
                    Button(Icon(serverOnline ? "Refresh" : "Play"), Props.StartOrRestartServer)
                        .SubtleButton()
                        .AutomationName(startOrRestartName)
                        .ToolTip(startOrRestartName)
                        .IsEnabled(!serverBusy)
                        .MinWidth(40)
                        .MinHeight(40),
                    Button(Icon("Stop"), Props.StopServer)
                        .SubtleButton()
                        .AutomationName(stopName)
                        .ToolTip(stopName)
                        .IsEnabled(serverRunning && !serverBusy)
                        .MinWidth(40)
                        .MinHeight(40))),
            SettingsCard(
                header: t.Message(new("App", "SettingsDeviceName")),
                description: t.Message(new("App", "SettingsDeviceNameDescription")),
                headerIcon: HeaderGlyph("\uE8AC"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                TextBox(Props.Settings.Alias, value =>
                    Props.UpdateSettings(settings => settings with { Alias = value }))
                    .AutomationName(t.Message(new("App", "SettingsDeviceName")))
                    .MinWidth(240)),
            SettingsExpander(
                headerIcon: HeaderGlyph("\uE756"),
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
                            .MinWidth(180)),
                    SettingsCard(
                        header: t.Message(new("App", "SettingsDeviceModel")),
                        description: t.Message(new("App", "SettingsDeviceModelDescription")),
                        isClickEnabled: false,
                        isActionIconVisible: false,
                        content:
                        TextBox(
                            Props.Settings.DeviceModel,
                            value => Props.UpdateSettings(settings => settings with { DeviceModel = value }),
                            placeholderText: Environment.MachineName)
                            .AutomationName(t.Message(new("App", "SettingsDeviceModel")))
                            .MinWidth(240)),
                    SettingsCard(
                        header: t.Message(new("App", "SettingsPort")),
                        description: Props.Settings.Port == LocalSendOptions.DefaultPort
                            ? t.Message(new("App", "SettingsPortDescription"))
                            : t.Message(new("App", "SettingsPortWarning"), ("port", LocalSendOptions.DefaultPort)),
                        isClickEnabled: false,
                        isActionIconVisible: false,
                        content:
                        NumberBox(Props.Settings.Port, value =>
                        {
                            var port = (int)Math.Round(value);
                            if (port is >= 1 and <= ushort.MaxValue && port != Props.Settings.Port)
                                Props.UpdateSettings(settings => settings with { Port = port });
                        })
                            .Range(1, ushort.MaxValue)
                            .SpinButtons()
                            .AutomationName(t.Message(new("App", "SettingsPort")))
                            .MinWidth(160)),
                    SettingsCard(
                        header: t.Message(new("App", "SettingsNetworkInterfaces")),
                        description: NetworkInterfacesSummary(t, Props.Settings),
                        isClickEnabled: false,
                        isActionIconVisible: false,
                        content:
                        Button(t.Message(new("App", "Change")), () => navigation.Navigate(AppRoute.NetworkInterfaces, AppNavigation.DrillIn))
                            .AutomationName(t.Message(new("App", "SettingsNetworkInterfaces")))),
                    SettingsCard(
                        header: t.Message(new("App", "SettingsDiscoveryTimeout")),
                        description: t.Message(new("App", "SettingsDiscoveryTimeoutDescription")),
                        isClickEnabled: false,
                        isActionIconVisible: false,
                        content:
                        NumberBox(Props.Settings.DiscoveryTimeoutMs, value =>
                        {
                            var timeout = (int)Math.Round(value);
                            if (timeout > 0 && timeout != Props.Settings.DiscoveryTimeoutMs)
                                Props.UpdateSettings(settings => settings with { DiscoveryTimeoutMs = timeout });
                        })
                            .Range(1, 60_000)
                            .SpinButtons()
                            .AutomationName(t.Message(new("App", "SettingsDiscoveryTimeout")))
                            .MinWidth(160)),
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
                        })),
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
                        TextBox(Props.Settings.MulticastGroup, value =>
                            Props.UpdateSettings(settings => settings with { MulticastGroup = value }))
                            .AutomationName(t.Message(new("App", "SettingsMulticast")))
                            .MinWidth(180)),
                ])
                .Set(expander =>
                {
                    expander.Header = t.Message(new("App", "SettingsAdvanced"));
                    expander.Description = t.Message(new("App", "SettingsAdvancedDescription"));
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
                new Uri("https://github.com/kusutori/Tonarink/issues"))));
        var aboutSection = VStack(4,
            Subtitle(t.Message(new("App", "SettingsAbout")))
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .Margin(bottom: 8),
            SettingsExpander(
                headerIcon: Icon(ImageIcon(new Uri(iconPath, UriKind.Absolute))),
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
            HyperlinkButton(
                t.Message(new("App", "SettingsAboutFeedback")),
                new Uri("https://github.com/kusutori/Tonarink/issues"))
                .HAlign(HorizontalAlignment.Left)
                .Margin(top: 8));

        return ScrollView(
            VStack(24,
                Heading(t.Message(new("App", "SettingsTitle")))
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                statusMessage is null
                    ? null
                    : (InfoBar(t.Message(new("App", "SettingsTitle")), statusMessage) with
                    {
                        IsOpen = true,
                        IsClosable = true,
                        OnClosed = () => setStatusMessage(null),
                    }).Severity(InfoBarSeverity.Error),
                Props.Runtime.Error is null
                    ? null
                    : (InfoBar(t.Message(new("App", "NetworkStartFailed")), Props.Runtime.Error) with
                    {
                        IsOpen = true,
                        IsClosable = false,
                    }).Severity(InfoBarSeverity.Error),
                Props.Runtime.DiscoveryWarning is null
                    ? null
                    : (InfoBar(t.Message(new("App", "NodeDiscoveryLimited")), Props.Runtime.DiscoveryWarning) with
                    {
                        IsOpen = true,
                        IsClosable = false,
                    }).Severity(InfoBarSeverity.Warning),
                generalCards,
                receiveCards,
                networkCards,
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
            .Padding(36))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
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
                var picker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.Downloads,
                    CommitButtonText = t.Message(new("App", "Change")),
                };
                picker.FileTypeFilter.Add("*");
                var nativeWindow = window?.NativeWindow
                    ?? throw new InvalidOperationException(t.Message(new("App", "WindowUnavailable")));
                WinRT.Interop.InitializeWithWindow.Initialize(
                    picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow));
                var folder = await picker.PickSingleFolderAsync();
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
    }

    private static Element HeaderGlyph(string glyph) =>
        Icon(glyph).AccessibilityHidden();

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
