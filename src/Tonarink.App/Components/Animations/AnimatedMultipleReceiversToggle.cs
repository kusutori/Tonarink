using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using XamlToggleButton = Microsoft.UI.Xaml.Controls.Primitives.ToggleButton;

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
                    UpdateForeground(button, source, Props.IsChecked));
            }

            PlayToggleTransition(playback, Props.IsChecked);
        }, Props.IsChecked, reduceMotion);

        var toggle = ToggleButton(string.Empty, Props.IsChecked, Props.OnChanged)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .MinWidth(40)
            .MinHeight(40)
            .IsEnabled(Props.IsEnabled)
            .OnMountAdd(element =>
            {
                if (element is not XamlToggleButton button)
                    return;

                playback.Button = button;
                AttachPointerHandlers(button, playback);
                button.DispatcherQueue.TryEnqueue(() =>
                {
                    if (playback.Source is { } source)
                        UpdateForeground(button, source, Props.IsChecked);
                });
            })
            .OnUnmountAdd(_ => DetachPointerHandlers(playback));
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

                var source = new Tonarink.MultipleReceiversIcon();
                UpdateForeground(playback.Button, player, source, playback.IsChecked);
                player.Loaded += (_, _) =>
                {
                    UpdateForeground(playback.Button, player, source, playback.IsChecked);
                    player.SetProgress(playback.IsChecked ? OnRestProgress : OffRestProgress);
                };
                player.ActualThemeChanged += (_, _) =>
                    UpdateForeground(playback.Button, player, source, playback.IsChecked);
                player.Source = source;
                player.SetProgress(playback.IsChecked ? OnRestProgress : OffRestProgress);
                playback.Source = source;
                playback.Player = player;
            })
            .OnUnmountAdd(element =>
            {
                if (element is AnimatedVisualPlayer player)
                    player.Stop();

                playback.Source = null;
                playback.Player = null;
            });

        return Grid(
            columns: [GridSize.Auto],
            rows: [GridSize.Auto],
            toggle,
            icon);
    }

    private static void AttachPointerHandlers(
        XamlToggleButton button,
        MultipleReceiversPlayback playback)
    {
        PointerEventHandler entered = (_, _) => PlayHover(playback);
        PointerEventHandler exited = (_, _) => RestoreRestPosition(playback);

        button.AddHandler(UIElement.PointerEnteredEvent, entered, handledEventsToo: true);
        button.AddHandler(UIElement.PointerExitedEvent, exited, handledEventsToo: true);
        playback.DetachPointerHandlers = () =>
        {
            button.RemoveHandler(UIElement.PointerEnteredEvent, entered);
            button.RemoveHandler(UIElement.PointerExitedEvent, exited);
        };
    }

    private static void DetachPointerHandlers(MultipleReceiversPlayback playback)
    {
        playback.DetachPointerHandlers?.Invoke();
        playback.DetachPointerHandlers = null;
        playback.Button = null;
        playback.Player?.Stop();
        playback.HoverExpanded = false;
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

    private static void UpdateForeground(
        XamlToggleButton button,
        Tonarink.MultipleReceiversIcon source,
        bool isChecked)
    {
        if (isChecked)
        {
            source.Foreground = Microsoft.UI.Colors.Black;
            return;
        }

        if (button.Foreground is SolidColorBrush brush)
            source.Foreground = brush.Color;
    }

    private static void UpdateForeground(
        XamlToggleButton? button,
        AnimatedVisualPlayer player,
        Tonarink.MultipleReceiversIcon source,
        bool isChecked)
    {
        if (isChecked)
        {
            source.Foreground = Microsoft.UI.Colors.Black;
            return;
        }

        if (button?.Foreground is SolidColorBrush brush)
        {
            source.Foreground = brush.Color;
            return;
        }

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

    private sealed class MultipleReceiversPlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public Tonarink.MultipleReceiversIcon? Source { get; set; }

        public XamlToggleButton? Button { get; set; }

        public bool IsChecked { get; set; }

        public bool ReduceMotion { get; set; }

        public bool HoverExpanded { get; set; }

        public int AnimationVersion { get; set; }

        public Action? DetachPointerHandlers { get; set; }
    }
}
