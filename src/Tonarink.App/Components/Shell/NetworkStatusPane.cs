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
    private const double NetworkIconSettledProgress = 34d / 72d;

    public override Element Render()
    {
        var t = UseIntl();
        var reduceMotion = UseReducedMotion();
        var networkIconPlaybackRef = UseRef<NetworkIconPlayback?>();
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
            () => SetNetworkIconState(networkIconPlaybackRef.Current, Props.NodeState, reduceMotion),
            Props.NodeState,
            reduceMotion);

        return Props.IsPaneOpen
            ? VStack(0,
                Divider(),
                Grid(
                        columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                        rows: [GridSize.Auto],
                        NetworkStatusIcon(networkIconPlaybackRef, Props.NodeState, reduceMotion)
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
        Ref<NetworkIconPlayback?> playbackRef,
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
            var playback = playbackRef.Current = new NetworkIconPlayback { Player = player };
            SetNetworkIconState(playback, state, reduceMotion);
        })
        .OnUnmountAdd(element =>
        {
            if (element is not AnimatedVisualPlayer player)
                return;

            if (playbackRef.Current is { } playback)
            {
                playback.Generation++;
                playback.Animation = NetworkIconAnimation.None;
                playback.Player = null;
            }

            player.Stop();
            playbackRef.Current = null;
        });

    private static void SetNetworkIconState(
        NetworkIconPlayback? playback,
        LocalSendNodeState state,
        bool reduceMotion)
    {
        if (playback?.Player is not { } player)
            return;

        playback.State = state;
        playback.ReduceMotion = reduceMotion;
        if (state == LocalSendNodeState.Starting && !reduceMotion)
        {
            if (playback.Animation == NetworkIconAnimation.Starting)
                return;

            CancelNetworkIconAnimation(playback, player);
            var generation = ++playback.Generation;
            playback.Animation = NetworkIconAnimation.Starting;
            player.SetProgress(0);
            _ = PlayStartingRoundsAsync(playback, player, generation);

            return;
        }

        if (state == LocalSendNodeState.Stopping && !reduceMotion)
        {
            if (playback.Animation == NetworkIconAnimation.Stopping)
                return;

            CancelNetworkIconAnimation(playback, player);
            var generation = ++playback.Generation;
            playback.Animation = NetworkIconAnimation.Stopping;
            player.SetProgress(NetworkIconSettledProgress);
            _ = PlayStoppingAnimationAsync(playback, player, generation);

            return;
        }

        if (state == LocalSendNodeState.Running)
        {
            // If startup completes in the middle of a round, let that round settle
            // naturally at the complete icon instead of cutting it short.
            if (playback.Animation == NetworkIconAnimation.Starting)
                return;

            CancelNetworkIconAnimation(playback, player);
            player.SetProgress(NetworkIconSettledProgress);
            return;
        }

        // The service can finish stopping before the visual reaches its final frame.
        // Keep the in-flight reverse animation alive so it still settles on the pole.
        if (playback.Animation == NetworkIconAnimation.Stopping && !reduceMotion)
            return;

        CancelNetworkIconAnimation(playback, player);
        player.SetProgress(0);
    }

    private static async Task PlayStartingRoundsAsync(
        NetworkIconPlayback playback,
        AnimatedVisualPlayer player,
        int generation)
    {
        while (playback.Generation == generation
               && ReferenceEquals(playback.Player, player)
               && playback.State == LocalSendNodeState.Starting
               && !playback.ReduceMotion)
        {
            await player.PlayAsync(
                fromProgress: 0,
                toProgress: NetworkIconSettledProgress,
                looped: false);

            if (playback.Generation == generation
                && playback.State == LocalSendNodeState.Starting)
            {
                await Task.Delay(300);
            }
        }

        if (playback.Generation == generation)
        {
            playback.Animation = NetworkIconAnimation.None;
            if (playback.State == LocalSendNodeState.Running)
                player.SetProgress(NetworkIconSettledProgress);
        }
    }

    private static async Task PlayStoppingAnimationAsync(
        NetworkIconPlayback playback,
        AnimatedVisualPlayer player,
        int generation)
    {
        await player.PlayAsync(
            fromProgress: NetworkIconSettledProgress,
            toProgress: 0,
            looped: false);
        if (playback.Generation != generation)
            return;

        playback.Animation = NetworkIconAnimation.None;
        player.SetProgress(
            playback.State == LocalSendNodeState.Running ? NetworkIconSettledProgress : 0);
    }

    private static void CancelNetworkIconAnimation(
        NetworkIconPlayback playback,
        AnimatedVisualPlayer player)
    {
        playback.Generation++;
        playback.Animation = NetworkIconAnimation.None;
        player.Stop();
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

    private sealed class NetworkIconPlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public LocalSendNodeState State { get; set; }

        public bool ReduceMotion { get; set; }

        public NetworkIconAnimation Animation { get; set; }

        public int Generation { get; set; }
    }

    private enum NetworkIconAnimation
    {
        None,
        Starting,
        Stopping,
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
