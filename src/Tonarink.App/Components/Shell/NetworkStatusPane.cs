using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Shell;

sealed record NetworkStatusPaneProps(
    bool IsPaneOpen,
    LocalSendNodeState NodeState,
    string? Error,
    string? DiscoveryWarning,
    bool IsServerDesired,
    Action StartServer,
    Action StopServer);

sealed class NetworkStatusPane : Component<NetworkStatusPaneProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var statusText = StatusText(t, Props.NodeState, Props.DiscoveryWarning);
        var statusColor = Props.Error is not null || Props.NodeState == LocalSendNodeState.Faulted
            ? Theme.SystemCritical
            : Props.DiscoveryWarning is not null
                ? Theme.SystemCaution
                : Props.NodeState == LocalSendNodeState.Running
                    ? Theme.SystemSuccess
                    : Props.NodeState is LocalSendNodeState.Starting or LocalSendNodeState.Stopping
                        ? Theme.SystemAttention
                        : Theme.SecondaryText;
        var toggleName = t.Message(new(
            "App",
            Props.IsServerDesired ? "SettingsStopServer" : "SettingsStartServer"));
        var isBusy = Props.NodeState is LocalSendNodeState.Starting or LocalSendNodeState.Stopping;

        return Props.IsPaneOpen
            ? VStack(0,
                Divider(),
                Grid(
                        columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                        rows: [GridSize.Auto],
                        Icon(AppIcons.Network)
                            .VAlign(VerticalAlignment.Center)
                            .AccessibilityHidden()
                            .Grid(column: 0),
                        VStack(2,
                                Caption(t.Message(new("App", "NetworkStatus"))).SemiBold(),
                                Caption(statusText)
                                    .Foreground(Theme.SecondaryText)
                                    .LiveRegion(AutomationLiveSetting.Polite)
                                    .TextWrapping(TextWrapping.WrapWholeWords))
                            .Margin(horizontal: 12, vertical: 0)
                            .Grid(column: 1),
                        StatusButton(compact: true).Grid(column: 2))
                    .Padding(horizontal: 16, vertical: 14)
                    .AutomationName(statusText)
                    .ToolTip(statusText))
            : VStack(0,
                Divider(),
                Border(StatusButton(compact: false))
                    .Size(56, 44)
                    .HAlign(HorizontalAlignment.Center));

        Element StatusButton(bool compact) =>
            Button(
                    StatusDot(statusColor),
                    Props.IsServerDesired ? Props.StopServer : Props.StartServer)
                .SubtleButton()
                .Size(compact ? 32 : 40, compact ? 32 : 40)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .AutomationName(toggleName)
                .HelpText(statusText)
                .ToolTip(toggleName)
                .IsEnabled(!isBusy);
    }

    private static Element Divider() => Border(null)
        .Height(1)
        .Margin(horizontal: 12, vertical: 0)
        .Background(Theme.DividerStroke)
        .AccessibilityHidden();

    private static Element StatusDot(ThemeRef color) => Border(null)
        .Size(8, 8)
        .CornerRadius(4)
        .Background(color)
        .VAlign(VerticalAlignment.Center)
        .AccessibilityHidden();

    private static string StatusText(
        IntlAccessor t,
        LocalSendNodeState state,
        string? discoveryWarning) => state switch
        {
            LocalSendNodeState.Starting => t.Message(new("App", "NodeStarting")),
            LocalSendNodeState.Running when discoveryWarning is not null =>
                t.Message(new("App", "NodeDiscoveryLimited")),
            LocalSendNodeState.Running => t.Message(new("App", "NodeRunning")),
            LocalSendNodeState.Faulted => t.Message(new("App", "NodeFaulted")),
            LocalSendNodeState.Stopping => t.Message(new("App", "NodeStopping")),
            _ => t.Message(new("App", "NodeDisconnected")),
        };
}
