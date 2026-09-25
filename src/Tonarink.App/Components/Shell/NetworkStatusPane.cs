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
    string? DiscoveryWarning);

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

        return Props.IsPaneOpen
            ? VStack(0,
                Divider(),
                Grid(
                        columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                        rows: [GridSize.Auto],
                        Icon("\uE704")
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
                        StatusDot(statusColor).Grid(column: 2))
                    .Padding(horizontal: 16, vertical: 14)
                    .AutomationName(statusText)
                    .ToolTip(statusText))
            : VStack(0,
                Divider(),
                Border(StatusDot(statusColor))
                    .Size(56, 44)
                    .HAlign(HorizontalAlignment.Center)
                    .AutomationName(statusText)
                    .ToolTip(statusText));
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
