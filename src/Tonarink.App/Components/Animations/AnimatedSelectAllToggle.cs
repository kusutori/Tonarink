using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using XamlToggleButton = Microsoft.UI.Xaml.Controls.Primitives.ToggleButton;

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

            if (playback.Button is { } button && playback.Source is { } source)
            {
                button.DispatcherQueue.TryEnqueue(() =>
                    UpdateForeground(button, source, Props.CheckedState));
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
                if (element is not XamlToggleButton button)
                    return;

                playback.Button = button;
                button.DispatcherQueue.TryEnqueue(() =>
                {
                    if (playback.Source is { } source)
                        UpdateForeground(button, source, playback.CheckedState);
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

                var source = new Tonarink.SelectAllToggleIcon();
                UpdateForeground(playback.Button, player, source, playback.CheckedState);
                player.Loaded += (_, _) =>
                {
                    UpdateForeground(playback.Button, player, source, playback.CheckedState);
                    player.SetProgress(RestProgress(playback.CheckedState));
                };
                player.ActualThemeChanged += (_, _) =>
                    UpdateForeground(playback.Button, player, source, playback.CheckedState);
                player.Source = source;
                player.SetProgress(RestProgress(playback.CheckedState));
                playback.Source = source;
                playback.Player = player;
            })
            .OnUnmountAdd(element =>
            {
                if (element is AnimatedVisualPlayer player)
                    player.Stop();

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
            _ => (settledProgress, settledProgress),
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

    private static void UpdateForeground(
        XamlToggleButton button,
        Tonarink.SelectAllToggleIcon source,
        bool? checkedState)
    {
        if (checkedState is true)
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
        Tonarink.SelectAllToggleIcon source,
        bool? checkedState)
    {
        if (checkedState is true)
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

    private sealed class SelectAllPlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public Tonarink.SelectAllToggleIcon? Source { get; set; }

        public XamlToggleButton? Button { get; set; }

        public bool? CheckedState { get; set; }

        public bool ReduceMotion { get; set; }

        public int AnimationVersion { get; set; }
    }
}
