using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedSelectAllToggleProps(
    bool? CheckedState,
    Action<bool?> OnChanged,
    string AutomationName,
    string? ToolTip = null,
    bool IsEnabled = true);

sealed class AnimatedSelectAllToggle : Component<AnimatedSelectAllToggleProps>
{
    private const double UncheckedRestProgress = 0;
    private const double IndeterminateRestProgress = 10d / 41;
    private const double CheckedRestProgress = 20d / 41;
    private const double CheckedToIndeterminateEndProgress = 30d / 41;
    private const double UncheckedReturnEndProgress = 40d / 41;

    public override Element Render()
    {
        var playbackRef = UseRef<SelectAllPlayback?>();
        var playback = playbackRef.Current ??= new SelectAllPlayback();
        var previousStateRef = UseRef(Props.CheckedState);
        var reduceMotion = UseReducedMotion();
        playback.CheckedState = Props.CheckedState;
        playback.ReduceMotion = reduceMotion;

        UseEffect(() =>
        {
            var previousState = previousStateRef.Current;
            previousStateRef.Current = Props.CheckedState;
            if (previousState == Props.CheckedState)
                return;

            if (playback is { Button: { } button, Source: { } source })
            {
                button.DispatcherQueue.TryEnqueue(() =>
                    AnimatedIconForeground.Apply(button, source, button, playback.CheckedState is true));
            }

            PlayStateTransition(playback, previousState, Props.CheckedState);
        }, Props.CheckedState, reduceMotion);

        var toggle = ThreeStateToggleButton(
                string.Empty,
                Props.CheckedState,
                Props.OnChanged)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .Size(40, 40)
            .Padding(0, 0)
            .IsEnabled(Props.IsEnabled)
            .OnMountAdd(element =>
            {
                if (element is not Control button)
                    return;

                playback.Button = button;
                button.DispatcherQueue.TryEnqueue(() =>
                {
                    if (playback.Source is { } source)
                        AnimatedIconForeground.Apply(button, source, button, playback.CheckedState is true);
                });
            })
            .OnUnmountAdd(_ =>
            {
                playback.Button = null;
                playback.AnimationVersion++;
                playback.Player?.Stop();
            });

        var icon = (AnimatedVisualPlayer() with { AutoPlay = false })
            .Size(24, 24)
            .Opacity(Props.IsEnabled ? 1 : 0.36)
            .IsHitTestVisible(false)
            .AccessibilityHidden()
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
            .OnMountAdd(element =>
            {
                if (element is not AnimatedVisualPlayer player)
                    return;

                var source = new SelectAllToggleIcon();
                AnimatedPlayerLifecycle.Mount(player, source,
                    () => AnimatedIconForeground.Apply(player, source, playback.Button, playback.CheckedState is true),
                    () => player.SetProgress(RestProgress(playback.CheckedState)));
                playback.Source = source;
                playback.Player = player;
            })
            .OnUnmountAdd(element =>
            {
                if (element is AnimatedVisualPlayer player)
                    AnimatedPlayerLifecycle.Unmount(player);

                playback.AnimationVersion++;
                playback.Source = null;
                playback.Player = null;
            });

        return Grid(
            columns: [GridSize.Auto],
            rows: [GridSize.Auto],
            toggle,
            icon);
    }

    private static double RestProgress(bool? state) => state switch
    {
        false => UncheckedRestProgress,
        null => IndeterminateRestProgress,
        true => CheckedRestProgress,
    };

    private static void PlayStateTransition(
        SelectAllPlayback playback,
        bool? previousState,
        bool? nextState)
    {
        if (playback.Player is not { } player)
            return;

        var version = ++playback.AnimationVersion;
        player.Stop();
        var settledProgress = RestProgress(nextState);
        if (playback.ReduceMotion)
        {
            player.SetProgress(settledProgress);
            return;
        }

        var (fromProgress, toProgress) = (previousState, nextState) switch
        {
            (false, null) => (UncheckedRestProgress, IndeterminateRestProgress),
            (false, true) => (UncheckedRestProgress, CheckedRestProgress),
            (null, true) => (IndeterminateRestProgress, CheckedRestProgress),
            (true, null) => (CheckedRestProgress, CheckedToIndeterminateEndProgress),
            (true, false) => (CheckedRestProgress, UncheckedReturnEndProgress),
            (null, false) => (CheckedToIndeterminateEndProgress, UncheckedReturnEndProgress),
            (false, false) or (true, true) or (null, null) => (settledProgress, settledProgress),
        };

        _ = PlayAndSettleAsync(
            playback,
            player,
            version,
            fromProgress,
            toProgress,
            settledProgress);
    }

    private static async Task PlayAndSettleAsync(
        SelectAllPlayback playback,
        AnimatedVisualPlayer player,
        int version,
        double fromProgress,
        double toProgress,
        double settledProgress)
    {
        await player.PlayAsync(fromProgress, toProgress, looped: false);
        if (version == playback.AnimationVersion && playback.Player == player)
            player.SetProgress(settledProgress);
    }


    private sealed class SelectAllPlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public SelectAllToggleIcon? Source { get; set; }

        public Control? Button { get; set; }

        public bool? CheckedState { get; set; }

        public bool ReduceMotion { get; set; }

        public int AnimationVersion { get; set; }
    }
}
