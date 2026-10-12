using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

enum UndoRedoIconKind
{
    Undo,
    Redo,
}

sealed record AnimatedUndoRedoButtonProps(
    UndoRedoIconKind Kind,
    string AutomationName,
    Action OnClick,
    string? ToolTip = null,
    bool IsEnabled = true,
    double IconSize = 24);

sealed class AnimatedUndoRedoButton : Component<AnimatedUndoRedoButtonProps>
{
    public override Element Render()
    {
        var playbackRef = UseRef<UndoRedoPlayback?>();
        var playback = playbackRef.Current ??= new UndoRedoPlayback();
        var reduceMotion = UseReducedMotion();

        return Button(
                (AnimatedVisualPlayer() with { AutoPlay = false })
                .Size(Props.IconSize, Props.IconSize)
                .Opacity(Props.IsEnabled ? 1 : 0.36)
                .IsHitTestVisible(false)
                .AccessibilityHidden()
                .OnMountAdd(element =>
                {
                    if (element is not AnimatedVisualPlayer player)
                        return;

                    var source = CreateSource(Props.Kind);
                    UpdateForeground(player, source);
                    player.Loaded += (_, _) =>
                    {
                        UpdateForeground(player, source);
                        player.SetProgress(0);
                    };
                    player.ActualThemeChanged += (_, _) => UpdateForeground(player, source);
                    player.Source = source.Visual;
                    player.SetProgress(0);
                    playback.Player = player;
                })
                .OnUnmountAdd(element =>
                {
                    if (element is AnimatedVisualPlayer player)
                        player.Stop();

                    playback.AnimationVersion++;
                    playback.Player = null;
                }),
                () =>
                {
                    if (!reduceMotion && playback.Player is { } player)
                    {
                        var version = ++playback.AnimationVersion;
                        player.Stop();
                        player.SetProgress(0);
                        _ = PlayAndSettleAsync(playback, player, version);
                    }

                    Props.OnClick();
                })
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .Size(40, 40)
            .Padding(0, 0)
            .IsEnabled(Props.IsEnabled);
    }

    private static async Task PlayAndSettleAsync(
        UndoRedoPlayback playback,
        AnimatedVisualPlayer player,
        int version)
    {
        await player.PlayAsync(0, 1, looped: false);
        if (version == playback.AnimationVersion && playback.Player == player)
            player.SetProgress(0);
    }

    private static UndoRedoIconSource CreateSource(UndoRedoIconKind kind) => kind switch
    {
        UndoRedoIconKind.Undo => new Tonarink.UndoFlowIcon(),
        UndoRedoIconKind.Redo => new Tonarink.RedoFlowIcon(),
    };

    private static void UpdateForeground(AnimatedVisualPlayer player, UndoRedoIconSource source)
    {
        var theme = player.ActualTheme;
        if (theme == ElementTheme.Default
            && player.XamlRoot?.Content is FrameworkElement root)
        {
            theme = root.ActualTheme;
        }

        var color = theme == ElementTheme.Dark
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.Colors.Black;
        source.SetForeground(color);
    }

    private union UndoRedoIconSource(Tonarink.UndoFlowIcon, Tonarink.RedoFlowIcon)
    {
        public IAnimatedVisualSource Visual => this switch
        {
            Tonarink.UndoFlowIcon undo => undo,
            Tonarink.RedoFlowIcon redo => redo,
        };

        public void SetForeground(Windows.UI.Color color) => _ = this switch
        {
            Tonarink.UndoFlowIcon undo => undo.Foreground = color,
            Tonarink.RedoFlowIcon redo => redo.Foreground = color,
        };
    }

    private sealed class UndoRedoPlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public int AnimationVersion { get; set; }
    }
}
