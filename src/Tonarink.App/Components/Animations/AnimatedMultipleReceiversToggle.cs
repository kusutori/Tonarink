using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedMultipleReceiversToggleProps(
    bool IsChecked,
    Action<bool> OnChanged,
    string AutomationName,
    string? ToolTip = null,
    bool IsEnabled = true);

sealed class AnimatedMultipleReceiversToggle : Component<AnimatedMultipleReceiversToggleProps>
{
    private const double OffRestProgress = 0;
    private const double OffHoverProgress = 0.2;
    private const double OnRestProgress = 0.4;
    private const double OnHoverProgress = 0.6;
    private const double OffReturnProgress = 0.8;

    public override Element Render()
    {
        var playbackRef = UseRef<MultipleReceiversPlayback?>();
        var playback = playbackRef.Current ??= new MultipleReceiversPlayback();
        var previousCheckedRef = UseRef(Props.IsChecked);
        var reduceMotion = UseReducedMotion();
        playback.IsChecked = Props.IsChecked;
        playback.ReduceMotion = reduceMotion;

        UseEffect(() =>
        {
            var wasChecked = previousCheckedRef.Current;
            previousCheckedRef.Current = Props.IsChecked;
            if (wasChecked == Props.IsChecked)
                return;

            if (playback.Button is { } button
                && playback.Source is { } source)
            {
                button.DispatcherQueue.TryEnqueue(() =>
                    AnimatedIconForeground.Apply(button, source, button, playback.IsChecked));
            }

            PlayToggleTransition(playback, Props.IsChecked);
        }, Props.IsChecked, reduceMotion);

        var toggle = ToggleButton(string.Empty, Props.IsChecked, Props.OnChanged)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .Size(44, 40)
            .Padding(0, 0)
            .IsEnabled(Props.IsEnabled)
            .OnPointerEntered((_, _) => { if (Props.IsEnabled) PlayHover(playback); })
            .OnPointerExited((_, _) => RestoreRestPosition(playback))
            .OnPointerCanceled((_, _) => RestoreRestPosition(playback))
            .OnPointerCaptureLost((_, _) => RestoreRestPosition(playback))
            .OnMountAdd(element =>
            {
                if (element is not Control button)
                    return;

                playback.Button = button;
                button.DispatcherQueue.TryEnqueue(() =>
                {
                    if (playback.Source is { } source)
                        AnimatedIconForeground.Apply(button, source, button, playback.IsChecked);
                });
            })
            .OnUnmountAdd(_ =>
            {
                playback.Button = null;
                playback.AnimationVersion++;
                playback.Player?.Stop();
                playback.HoverExpanded = false;
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

                var source = new MultipleReceiversIcon();
                AnimatedPlayerLifecycle.Mount(player, source,
                    () => AnimatedIconForeground.Apply(player, source, playback.Button, playback.IsChecked),
                    () => player.SetProgress(playback.IsChecked ? OnRestProgress : OffRestProgress));
                playback.Source = source;
                playback.Player = player;
            })
            .OnUnmountAdd(element =>
            {
                if (element is AnimatedVisualPlayer player)
                    AnimatedPlayerLifecycle.Unmount(player);

                playback.Source = null;
                playback.Player = null;
            });

        return Grid(
            columns: [GridSize.Auto],
            rows: [GridSize.Auto],
            toggle,
            icon);
    }

    private static void PlayHover(MultipleReceiversPlayback playback)
    {
        if (playback.Player is not { } player || playback.ReduceMotion)
            return;

        playback.AnimationVersion++;
        playback.HoverExpanded = true;
        player.Stop();
        _ = player.PlayAsync(
            fromProgress: playback.IsChecked ? OnRestProgress : OffRestProgress,
            toProgress: playback.IsChecked ? OnHoverProgress : OffHoverProgress,
            looped: false);
    }

    private static void RestoreRestPosition(MultipleReceiversPlayback playback)
    {
        if (!playback.HoverExpanded || playback.Player is not { } player)
            return;

        playback.AnimationVersion++;
        playback.HoverExpanded = false;
        player.Stop();
        player.SetProgress(playback.IsChecked ? OnRestProgress : OffRestProgress);
    }

    private static void PlayToggleTransition(
        MultipleReceiversPlayback playback,
        bool isChecked)
    {
        if (playback.Player is not { } player)
            return;

        var wasExpanded = playback.HoverExpanded;
        playback.HoverExpanded = false;
        var version = ++playback.AnimationVersion;
        player.Stop();

        if (playback.ReduceMotion)
        {
            player.SetProgress(isChecked ? OnRestProgress : OffRestProgress);
            return;
        }

        var fromProgress = isChecked
            ? wasExpanded ? OffHoverProgress : OffRestProgress
            : wasExpanded ? OnHoverProgress : OnRestProgress;
        var toProgress = isChecked ? OnRestProgress : OffReturnProgress;
        var settledProgress = isChecked ? OnRestProgress : OffRestProgress;
        _ = PlayAndSettleAsync(
            playback,
            version,
            fromProgress,
            toProgress,
            settledProgress);
    }

    private static async Task PlayAndSettleAsync(
        MultipleReceiversPlayback playback,
        int version,
        double fromProgress,
        double toProgress,
        double settledProgress)
    {
        if (playback.Player is not { } player)
            return;

        await player.PlayAsync(fromProgress, toProgress, looped: false);
        if (version == playback.AnimationVersion && playback.Player == player)
            player.SetProgress(settledProgress);
    }


    private sealed class MultipleReceiversPlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public MultipleReceiversIcon? Source { get; set; }

        public Control? Button { get; set; }

        public bool IsChecked { get; set; }

        public bool ReduceMotion { get; set; }

        public bool HoverExpanded { get; set; }

        public int AnimationVersion { get; set; }

    }
}
