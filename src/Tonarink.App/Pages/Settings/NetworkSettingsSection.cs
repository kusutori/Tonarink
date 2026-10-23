using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SettingsCardElement;
using static Tonarink.Controls.SettingsExpanderElement;
using static Tonarink.Pages.Settings.SettingsSectionVisuals;

namespace Tonarink.Pages.Settings;

sealed record NetworkSettingsSectionProps(
    AppSettings Settings,
    AppRuntimeState Runtime,
    SettingsInputDraft Draft,
    Action<Func<AppSettings, AppSettings>> UpdateSettings,
    Action<Func<SettingsInputDraft, SettingsInputDraft>> UpdateDraft,
    Action ApplyAndRestart,
    Action StopServer);

sealed class NetworkSettingsSection : Component<NetworkSettingsSectionProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var navigation = UseNavigation<AppRoute>();
        var (deviceNameFocused, setDeviceNameFocused) = UseState(false);
        var (encryptionNoticeOpen, setEncryptionNoticeOpen) = UseState(false);
        var draft = Props.Draft;
        var updateDraft = Props.UpdateDraft;
        var applyAndRestart = Props.ApplyAndRestart;
        var hasDraftChanges = draft != SettingsInputDraft.FromSettings(Props.Settings);
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
        var startOrRestartName = hasDraftChanges
            ? t.Message(new("App", serverOnline ? "SettingsApplyAndRestart" : "SettingsApplyAndStart"))
            : t.Message(new("App", serverOnline ? "SettingsRestartServer" : "SettingsStartServer"));
        var stopName = t.Message(new("App", "SettingsStopServer"));
        var serverDescription = hasDraftChanges
            ? t.Message(new("App", "SettingsInputsPending"))
            : needsRestart
                ? t.Message(new("App", "SettingsNeedRestart"))
                : nodeState switch
                {
                    LocalSendNodeState.Running => t.Message(new("App", "SettingsServerRunning")),
                    LocalSendNodeState.Starting => t.Message(new("App", "NodeStarting")),
                    LocalSendNodeState.Stopping => t.Message(new("App", "NodeStopping")),
                    LocalSendNodeState.Created or LocalSendNodeState.Stopped
                        or LocalSendNodeState.Faulted or LocalSendNodeState.Disposed =>
                        t.Message(new("App", "SettingsServerStopped")),
                };
        var cards = SettingsGroup(
            t.Message(new("App", "SettingsNetwork")),
            SettingsCard(
                header: serverOnline
                    ? t.Message(new("App", "DeviceServer"))
                    : t.Message(new("App", "SettingsServerOffline")),
                description: serverDescription,
                headerIcon: AnimatedButtons.SettingsServerIcon(nodeState)
                    .WithKey("settings-server-icon"),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                AnimatedButtons.SettingsServiceActions(
                    serverOnline,
                    nodeState == LocalSendNodeState.Starting,
                    serverBusy,
                    serverRunning,
                    applyAndRestart,
                    Props.StopServer,
                    startOrRestartName,
                    stopName)),
            SettingsCard(
                header: t.Message(new("App", "SettingsDeviceName")),
                description: t.Message(new("App", "SettingsDeviceNameDescription")),
                headerIcon: AnimatedButtons.SettingsRenameIcon(deviceNameFocused),
                isClickEnabled: false,
                isActionIconVisible: false,
                content:
                TextBox(draft.Alias, value => updateDraft(current => current with { Alias = value }))
                    .OnGotFocus((_, _) => setDeviceNameFocused(true))
                    .OnLostFocus((_, _) => setDeviceNameFocused(false))
                    .AutomationName(t.Message(new("App", "SettingsDeviceName")))
                    .AutomationId("Settings.DeviceName")
                    .MinWidth(240)),
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
                                TextBox(draft.DeviceModel,
                                        value => updateDraft(current => current with { DeviceModel = value }),
                                        Environment.MachineName)
                                    .AutomationName(t.Message(new("App", "SettingsDeviceModel")))
                                    .MinWidth(240)),
                            SettingsCard(
                                header: t.Message(new("App", "SettingsPort")),
                                description: Props.Settings.Port == LocalSendOptions.DefaultPort
                                    ? t.Message(new("App", "SettingsPortDescription"))
                                    : t.Message(new("App", "SettingsPortWarning"),
                                        ("port", LocalSendOptions.DefaultPort)),
                                isClickEnabled: false,
                                isActionIconVisible: false,
                                content:
                                NumberBox(draft.Port, value => updateDraft(current => current with { Port = value }))
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
                                Button(t.Message(new("App", "Change")),
                                        () => navigation.Navigate(AppRoute.NetworkInterfaces, AppNavigation.DrillIn))
                                    .AutomationName(t.Message(new("App", "SettingsNetworkInterfaces")))),
                            SettingsCard(
                                header: t.Message(new("App", "SettingsDiscoveryTimeout")),
                                description: t.Message(new("App", "SettingsDiscoveryTimeoutDescription")),
                                isClickEnabled: false,
                                isActionIconVisible: false,
                                content:
                                NumberBox(draft.DiscoveryTimeoutMs,
                                        value => updateDraft(current => current with { DiscoveryTimeoutMs = value }))
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
                                TextBox(draft.MulticastGroup,
                                        value => updateDraft(current => current with { MulticastGroup = value }))
                                    .AutomationName(t.Message(new("App", "SettingsMulticast")))
                                    .MinWidth(180)),
                        ])
                    .AutomationId("Settings.Advanced.Expander") with
                {
                    Header = t.Message(new("App", "SettingsAdvanced")),
                    Description = t.Message(new("App", "SettingsAdvancedDescription")),
                });

        return VStack(0, cards,
            Component<EncryptionDisabledDialog, EncryptionDisabledDialogProps>(new(
                encryptionNoticeOpen,
                AppTheme.ToElementTheme(Props.Settings.ThemeIndex),
                () => setEncryptionNoticeOpen(false))));
    }

    private static Element DeviceTypeOption(LocalSendDeviceType type, string label) =>
        HStack(10,
                Icon(FontIcon(DeviceVisuals.DeviceTypeGlyph(type), fontSize: 16))
                    .Width(20)
                    .AccessibilityHidden(),
                TextBlock(label)
                    .VAlign(VerticalAlignment.Center))
            .WithKey(type.ToString());

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
