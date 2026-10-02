using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using XamlButton = Microsoft.UI.Xaml.Controls.Button;

namespace Tonarink.Components.Animations;

sealed record AnimatedSettingsServiceButtonsProps(
    bool IsOnline,
    bool IsStarting,
    bool IsBusy,
    bool CanStop,
    Action StartOrRestart,
    Action Stop,
    string StartOrRestartName,
    string StopName);

sealed class AnimatedSettingsServiceButtons : Component<AnimatedSettingsServiceButtonsProps>
{
    private const double RefreshRestProgress = 0;
    private const double PlayRestProgress = 18d / 84;
    private const double PlayToRefreshStartProgress = 24d / 84;
    private const double RefreshSpinStartProgress = 42d / 84;
    private const double RefreshSpinEndProgress = 72d / 84;
    private const double StopAnimationEndProgress = 14d / 24;

    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        var actionPlaybackRef = UseRef<ServiceActionPlayback?>();
        var actionPlayback = actionPlaybackRef.Current ??= new ServiceActionPlayback();
        var stopPlaybackRef = UseRef<StopPlayback?>();
        var stopPlayback = stopPlaybackRef.Current ??= new StopPlayback();
        var previousOnlineRef = UseRef(Props.IsOnline);
        actionPlayback.IsOnline = Props.IsOnline;
        actionPlayback.IsStarting = Props.IsStarting;
        actionPlayback.ReduceMotion = reduceMotion;
        stopPlayback.ReduceMotion = reduceMotion;

        UseEffect(() =>
        {
            var wasOnline = previousOnlineRef.Current;
            previousOnlineRef.Current = Props.IsOnline;
            if (reduceMotion)
            {
                if (actionPlayback.Player is { } player)
                {
                    actionPlayback.AnimationVersion++;
                    actionPlayback.Animation = ServiceActionAnimation.None;
                    player.Stop();
                    player.SetProgress(Props.IsOnline ? RefreshRestProgress : PlayRestProgress);
                }

                return;
            }

            if (wasOnline != Props.IsOnline)
            {
                PlayOnlineTransition(actionPlayback, Props.IsOnline);
                return;
            }

            if (Props.IsStarting)
                EnsureStartingSpin(actionPlayback);
            // When startup finishes, the running loop observes IsStarting=false
            // and lets its current revolution settle naturally before stopping.
        }, Props.IsOnline, Props.IsStarting, reduceMotion);

        return HStack(
            4,
            ActionButton(actionPlayback),
            StopButton(stopPlayback));
    }

    private Element ActionButton(ServiceActionPlayback playback)
    {
        var button = Button(string.Empty, () =>
            {
                if (Props.IsOnline)
                    BeginRefreshSpin(playback);

                Props.StartOrRestart();
            })
            .SubtleButton()
            .AutomationName(Props.StartOrRestartName)
            .ToolTip(Props.StartOrRestartName)
            .IsEnabled(!Props.IsBusy)
            .Size(40, 40)
            .Padding(0, 0)
            .OnMountAdd(element =>
            {
                if (element is not XamlButton mountedButton)
                    return;

                playback.Button = mountedButton;
                mountedButton.DispatcherQueue.TryEnqueue(() =>
                {
                    if (playback.Source is { } source)
                        UpdateForeground(mountedButton, source);
                });
            })
            .OnUnmountAdd(_ => playback.Button = null);

        var icon = (AnimatedVisualPlayer() with { AutoPlay = false })
            .Size(24, 24)
            .Opacity(Props.IsBusy ? 0.36 : 1)
            .IsHitTestVisible(false)
            .AccessibilityHidden()
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
            .OnMountAdd(element =>
            {
                if (element is not AnimatedVisualPlayer player)
                    return;

                var source = new Tonarink.SettingsServiceActionIcon();
                UpdateForeground(playback.Button, player, source);
                player.Loaded += (_, _) =>
                {
                    UpdateForeground(playback.Button, player, source);
                    player.SetProgress(playback.IsOnline ? RefreshRestProgress : PlayRestProgress);
                    if (playback.IsStarting && !playback.ReduceMotion)
                        EnsureStartingSpin(playback);
                };
                player.ActualThemeChanged += (_, _) =>
                    UpdateForeground(playback.Button, player, source);
                player.Source = source;
                player.SetProgress(playback.IsOnline ? RefreshRestProgress : PlayRestProgress);
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
            button,
            icon);
    }

    private Element StopButton(StopPlayback playback)
    {
        var isEnabled = Props.CanStop && !Props.IsBusy;
        var button = Button(string.Empty, () =>
            {
                PlayStopFeedback(playback);
                Props.Stop();
            })
            .SubtleButton()
            .AutomationName(Props.StopName)
            .ToolTip(Props.StopName)
            .IsEnabled(isEnabled)
            .Size(40, 40)
            .Padding(0, 0)
            .OnMountAdd(element =>
            {
                if (element is not XamlButton mountedButton)
                    return;

                playback.Button = mountedButton;
                mountedButton.DispatcherQueue.TryEnqueue(() =>
                {
                    if (playback.Source is { } source)
                        UpdateForeground(mountedButton, source);
                });
            })
            .OnUnmountAdd(_ => playback.Button = null);

        var icon = (AnimatedVisualPlayer() with { AutoPlay = false })
            .Size(24, 24)
            .Opacity(isEnabled ? 1 : 0.36)
            .IsHitTestVisible(false)
            .AccessibilityHidden()
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
            .OnMountAdd(element =>
            {
                if (element is not AnimatedVisualPlayer player)
                    return;

                var source = new Tonarink.SettingsStopIcon();
                UpdateForeground(playback.Button, player, source);
                player.Loaded += (_, _) => UpdateForeground(playback.Button, player, source);
                player.ActualThemeChanged += (_, _) =>
                    UpdateForeground(playback.Button, player, source);
                player.Source = source;
                player.SetProgress(0);
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
            button,
            icon);
    }

    private static void PlayOnlineTransition(ServiceActionPlayback playback, bool isOnline)
    {
        if (playback.Player is not { } player)
            return;

        var version = ++playback.AnimationVersion;
        playback.Animation = ServiceActionAnimation.OneShot;
        player.Stop();
        if (playback.ReduceMotion)
        {
            player.SetProgress(isOnline ? RefreshRestProgress : PlayRestProgress);
            return;
        }

        _ = isOnline
            ? PlayStartingSequenceAsync(
                playback,
                player,
                version,
                PlayToRefreshStartProgress)
            : PlayAndSettleAsync(
                playback,
                version,
                RefreshRestProgress,
                PlayRestProgress,
                PlayRestProgress);
    }

    private static void BeginRefreshSpin(ServiceActionPlayback playback)
    {
        if (playback.Player is not { } player || playback.ReduceMotion)
            return;

        var version = ++playback.AnimationVersion;
        playback.Animation = ServiceActionAnimation.Starting;
        player.Stop();
        _ = PlayStartingSequenceAsync(
            playback,
            player,
            version,
            RefreshSpinStartProgress);
    }

    private static void EnsureStartingSpin(ServiceActionPlayback playback)
    {
        if (playback.Player is not { } player
            || playback.ReduceMotion
            || playback.Animation == ServiceActionAnimation.Starting)
        {
            return;
        }

        var version = ++playback.AnimationVersion;
        playback.Animation = ServiceActionAnimation.Starting;
        player.Stop();
        _ = PlayStartingSequenceAsync(
            playback,
            player,
            version,
            RefreshSpinStartProgress);
    }

    private static async Task PlayStartingSequenceAsync(
        ServiceActionPlayback playback,
        AnimatedVisualPlayer player,
        int version,
        double initialProgress)
    {
        playback.Animation = ServiceActionAnimation.Starting;
        await player.PlayAsync(initialProgress, RefreshSpinEndProgress, looped: false);

        while (version == playback.AnimationVersion
               && playback.Player == player
               && playback.IsStarting
               && !playback.ReduceMotion)
        {
            await player.PlayAsync(
                RefreshSpinStartProgress,
                RefreshSpinEndProgress,
                looped: false);
        }

        if (version != playback.AnimationVersion || playback.Player != player)
            return;

        playback.Animation = ServiceActionAnimation.None;
        player.SetProgress(playback.IsOnline ? RefreshRestProgress : PlayRestProgress);
    }

    private static async Task PlayAndSettleAsync(
        ServiceActionPlayback playback,
        int version,
        double fromProgress,
        double toProgress,
        double settledProgress)
    {
        if (playback.Player is not { } player)
            return;

        await player.PlayAsync(fromProgress, toProgress, looped: false);
        if (version == playback.AnimationVersion && playback.Player == player)
        {
            playback.Animation = ServiceActionAnimation.None;
            player.SetProgress(settledProgress);
        }
    }

    private static void PlayStopFeedback(StopPlayback playback)
    {
        if (playback.Player is not { } player || playback.ReduceMotion)
            return;

        var version = ++playback.AnimationVersion;
        player.Stop();
        _ = PlayStopAndSettleAsync(playback, player, version);
    }

    private static async Task PlayStopAndSettleAsync(
        StopPlayback playback,
        AnimatedVisualPlayer player,
        int version)
    {
        await player.PlayAsync(0, StopAnimationEndProgress, looped: false);
        if (version == playback.AnimationVersion && playback.Player == player)
            player.SetProgress(0);
    }

    private static void UpdateForeground(
        XamlButton button,
        object source)
    {
        if (button.Foreground is SolidColorBrush brush)
            SetForeground(source, brush.Color);
    }

    private static void UpdateForeground(
        XamlButton? button,
        AnimatedVisualPlayer player,
        object source)
    {
        if (button?.Foreground is SolidColorBrush brush)
        {
            SetForeground(source, brush.Color);
            return;
        }

        var theme = player.ActualTheme;
        if (theme == ElementTheme.Default
            && player.XamlRoot?.Content is FrameworkElement root)
        {
            theme = root.ActualTheme;
        }

        SetForeground(
            source,
            theme == ElementTheme.Dark
                ? Microsoft.UI.Colors.White
                : Microsoft.UI.Colors.Black);
    }

    private static void SetForeground(object source, Windows.UI.Color color)
    {
        switch (source)
        {
            case Tonarink.SettingsServiceActionIcon action:
                action.Foreground = color;
                break;
            case Tonarink.SettingsStopIcon stop:
                stop.Foreground = color;
                break;
        }
    }

    private sealed class ServiceActionPlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public Tonarink.SettingsServiceActionIcon? Source { get; set; }

        public XamlButton? Button { get; set; }

        public bool IsOnline { get; set; }

        public bool IsStarting { get; set; }

        public bool ReduceMotion { get; set; }

        public int AnimationVersion { get; set; }

        public ServiceActionAnimation Animation { get; set; }
    }

    private enum ServiceActionAnimation
    {
        None,
        OneShot,
        Starting,
    }

    private sealed class StopPlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public Tonarink.SettingsStopIcon? Source { get; set; }

        public XamlButton? Button { get; set; }

        public bool ReduceMotion { get; set; }

        public int AnimationVersion { get; set; }
    }
}
