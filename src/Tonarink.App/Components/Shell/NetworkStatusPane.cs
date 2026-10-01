using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
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
        var reduceMotion = UseReducedMotion();
        var networkIconPlayerRef = UseRef<AnimatedVisualPlayer?>();
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
        UseEffect(
            () => SetNetworkIconState(networkIconPlayerRef.Current, Props.NodeState, reduceMotion),
            Props.NodeState,
            reduceMotion);

        return Props.IsPaneOpen
            ? VStack(0,
                Divider(),
                Grid(
                        columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                        rows: [GridSize.Auto],
                        NetworkStatusIcon(networkIconPlayerRef, Props.NodeState, reduceMotion)
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

    private static Element NetworkStatusIcon(
        Ref<AnimatedVisualPlayer?> playerRef,
        LocalSendNodeState state,
        bool reduceMotion) =>
        (AnimatedVisualPlayer() with { AutoPlay = false })
        .Size(24, 24)
        .OnMountAdd(element =>
        {
            if (element is not AnimatedVisualPlayer player)
                return;

            var source = new Tonarink.NetworkStatusIcon();
            UpdateNetworkIconForeground(player, source);
            player.Loaded += (_, _) => UpdateNetworkIconForeground(player, source);
            player.ActualThemeChanged += (_, _) => UpdateNetworkIconForeground(player, source);
            player.Source = source;
            playerRef.Current = player;
            SetNetworkIconState(player, state, reduceMotion);
        })
        .OnUnmountAdd(element =>
        {
            if (element is not AnimatedVisualPlayer player)
                return;

            player.Stop();
            if (ReferenceEquals(playerRef.Current, player))
                playerRef.Current = null;
        });

    private static void SetNetworkIconState(
        AnimatedVisualPlayer? player,
        LocalSendNodeState state,
        bool reduceMotion)
    {
        if (player is null)
            return;

        player.Stop();
        switch (state)
        {
            case LocalSendNodeState.Starting when !reduceMotion:
                player.SetProgress(0);
                _ = player.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
                break;
            case LocalSendNodeState.Running:
                player.SetProgress(1);
                break;
            default:
                player.SetProgress(0);
                break;
        }
    }

    private static void UpdateNetworkIconForeground(
        AnimatedVisualPlayer player,
        Tonarink.NetworkStatusIcon source)
    {
        var theme = player.ActualTheme;
        if (theme == ElementTheme.Default
            && player.XamlRoot?.Content is FrameworkElement root)
        {
            theme = root.ActualTheme;
        }

        source.Foreground = theme == ElementTheme.Dark
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.Colors.Black;
    }

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
