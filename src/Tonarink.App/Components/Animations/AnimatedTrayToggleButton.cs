using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using XamlToggleButton = Microsoft.UI.Xaml.Controls.Primitives.ToggleButton;

namespace Tonarink.Components.Animations;

enum TrayToggleIconKind
{
    Service,
    Pin,
}

sealed record AnimatedTrayToggleButtonProps(
    TrayToggleIconKind Kind,
    bool IsChecked,
    Action<bool> OnChanged,
    string AutomationName,
    string? ToolTip = null,
    bool IsEnabled = true);

sealed class AnimatedTrayToggleButton : Component<AnimatedTrayToggleButtonProps>
{
    private const double ServiceOffRestProgress = 0;
    private const double ServiceOnRestProgress = 16d / 40;
    private const double ServiceOffStartProgress = 20d / 40;
    private const double ServiceOffEndProgress = 36d / 40;

    private const double UnpinnedRestProgress = 0;
    private const double PinnedRestProgress = 14d / 36;
    private const double UnpinStartProgress = 18d / 36;
    private const double UnpinEndProgress = 32d / 36;

    public override Element Render()
    {
        var playbackRef = UseRef<TrayTogglePlayback?>();
        var playback = playbackRef.Current ??= new TrayTogglePlayback();
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

            if (playback.Button is { } button && playback.Source is not null)
            {
                button.DispatcherQueue.TryEnqueue(() =>
                    UpdateForeground(button, playback.Source, Props.IsChecked));
            }

            PlayToggleTransition(playback, Props.Kind, Props.IsChecked);
        }, Props.IsChecked, reduceMotion);

        var toggle = ToggleButton(string.Empty, Props.IsChecked, Props.OnChanged)
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
                    if (playback.Source is not null)
                        UpdateForeground(button, playback.Source, Props.IsChecked);
                });
            })
            .OnUnmountAdd(_ =>
            {
                playback.Button = null;
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

                var source = CreateSource(Props.Kind);
                UpdateForeground(playback.Button, player, source, playback.IsChecked);
                player.Loaded += (_, _) =>
                {
                    UpdateForeground(playback.Button, player, source, playback.IsChecked);
                    player.SetProgress(RestProgress(Props.Kind, playback.IsChecked));
                };
                player.ActualThemeChanged += (_, _) =>
                    UpdateForeground(playback.Button, player, source, playback.IsChecked);
                SetSource(player, source);
                player.SetProgress(RestProgress(Props.Kind, playback.IsChecked));
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

    private static object CreateSource(TrayToggleIconKind kind) => kind switch
    {
        TrayToggleIconKind.Service => new Tonarink.TrayServiceToggleIcon(),
        TrayToggleIconKind.Pin => new Tonarink.TrayPinToggleIcon(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void SetSource(AnimatedVisualPlayer player, object source)
    {
        switch (source)
        {
            case Tonarink.TrayServiceToggleIcon service:
                player.Source = service;
                break;
            case Tonarink.TrayPinToggleIcon pin:
                player.Source = pin;
                break;
        }
    }

    private static double RestProgress(TrayToggleIconKind kind, bool isChecked) => kind switch
    {
        TrayToggleIconKind.Service => isChecked ? ServiceOnRestProgress : ServiceOffRestProgress,
        TrayToggleIconKind.Pin => isChecked ? PinnedRestProgress : UnpinnedRestProgress,
        _ => 0,
    };

    private static void PlayToggleTransition(
        TrayTogglePlayback playback,
        TrayToggleIconKind kind,
        bool isChecked)
    {
        if (playback.Player is not { } player)
            return;

        var version = ++playback.AnimationVersion;
        player.Stop();

        var settledProgress = RestProgress(kind, isChecked);
        if (playback.ReduceMotion)
        {
            player.SetProgress(settledProgress);
            return;
        }

        var (fromProgress, toProgress) = (kind, isChecked) switch
        {
            (TrayToggleIconKind.Service, true) =>
                (ServiceOffRestProgress, ServiceOnRestProgress),
            (TrayToggleIconKind.Service, false) =>
                (ServiceOffStartProgress, ServiceOffEndProgress),
            (TrayToggleIconKind.Pin, true) =>
                (UnpinnedRestProgress, PinnedRestProgress),
            (TrayToggleIconKind.Pin, false) =>
                (UnpinStartProgress, UnpinEndProgress),
            _ => (settledProgress, settledProgress),
        };

        _ = PlayAndSettleAsync(
            playback,
            version,
            fromProgress,
            toProgress,
            settledProgress);
    }

    private static async Task PlayAndSettleAsync(
        TrayTogglePlayback playback,
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
        object source,
        bool isChecked)
    {
        if (isChecked)
        {
            SetForeground(source, Microsoft.UI.Colors.Black);
            return;
        }

        if (button.Foreground is SolidColorBrush brush)
            SetForeground(source, brush.Color);
    }

    private static void UpdateForeground(
        XamlToggleButton? button,
        AnimatedVisualPlayer player,
        object source,
        bool isChecked)
    {
        if (isChecked)
        {
            SetForeground(source, Microsoft.UI.Colors.Black);
            return;
        }

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
            case Tonarink.TrayServiceToggleIcon service:
                service.Foreground = color;
                break;
            case Tonarink.TrayPinToggleIcon pin:
                pin.Foreground = color;
                break;
        }
    }

    private sealed class TrayTogglePlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public object? Source { get; set; }

        public XamlToggleButton? Button { get; set; }

        public bool IsChecked { get; set; }

        public bool ReduceMotion { get; set; }

        public int AnimationVersion { get; set; }
    }
}
