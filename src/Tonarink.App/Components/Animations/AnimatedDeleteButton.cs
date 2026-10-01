using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using static Microsoft.UI.Reactor.Factories;
using XamlButton = Microsoft.UI.Xaml.Controls.Button;

namespace Tonarink.Components.Animations;

sealed record AnimatedDeleteButtonProps(
    string AutomationName,
    Action OnClick,
    string? Label = null,
    string? ToolTip = null,
    bool IsEnabled = true,
    bool Subtle = false,
    bool Critical = false);

sealed class AnimatedDeleteButton : Component<AnimatedDeleteButtonProps>
{
    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        var playbackRef = UseRef<DeleteButtonPlayback?>();
        var playback = playbackRef.Current ??= new DeleteButtonPlayback();
        var player = DeleteIcon(playback, Props.Critical);
        Element content = Props.Label is null
            ? player
            : HStack(player, Props.Label);

        var button = Button(content, Props.OnClick)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .MinWidth(40)
            .MinHeight(40)
            .IsEnabled(Props.IsEnabled)
            .OnMountAdd(element => AttachPointerHandlers(element, playback, reduceMotion))
            .OnUnmountAdd(element => DetachPointerHandlers(element, playback));

        if (Props.Subtle)
            button = button.SubtleButton();

        if (Props.Critical)
        {
            button = button.Resources(static resources => resources
                .Set("ButtonForeground", Theme.SystemCritical)
                .Set("ButtonForegroundPointerOver", Theme.SystemCritical)
                .Set("ButtonForegroundPressed", Theme.SystemCritical)
                .Set("ButtonForegroundDisabled", Theme.DisabledText));
        }

        return button;
    }

    private static Element DeleteIcon(DeleteButtonPlayback playback, bool critical) =>
        (AnimatedVisualPlayer() with { AutoPlay = false })
        .Size(24, 24)
        .IsHitTestVisible(false)
        .AccessibilityHidden()
        .OnMountAdd(element =>
        {
            if (element is not AnimatedVisualPlayer player)
                return;

            var source = new Tonarink.DeleteIcon();
            UpdateForeground(player, source, critical);
            player.Loaded += (_, _) => UpdateForeground(player, source, critical);
            player.ActualThemeChanged += (_, _) => UpdateForeground(player, source, critical);
            player.Source = source;
            player.SetProgress(0);
            playback.Player = player;
        })
        .OnUnmountAdd(element =>
        {
            if (element is AnimatedVisualPlayer player)
                player.Stop();

            playback.Player = null;
        });

    private static void AttachPointerHandlers(
        UIElement element,
        DeleteButtonPlayback playback,
        bool reduceMotion)
    {
        if (element is not XamlButton button)
            return;

        PointerEventHandler entered = (_, _) =>
        {
            if (!reduceMotion)
                PlayHover(playback);
        };
        PointerEventHandler exited = (_, _) =>
        {
            CloseImmediately(playback);
        };

        button.AddHandler(UIElement.PointerEnteredEvent, entered, handledEventsToo: true);
        button.AddHandler(UIElement.PointerExitedEvent, exited, handledEventsToo: true);
        playback.DetachPointerHandlers = () =>
        {
            button.RemoveHandler(UIElement.PointerEnteredEvent, entered);
            button.RemoveHandler(UIElement.PointerExitedEvent, exited);
        };
    }

    private static void DetachPointerHandlers(
        UIElement element,
        DeleteButtonPlayback playback)
    {
        playback.DetachPointerHandlers?.Invoke();
        playback.DetachPointerHandlers = null;
        playback.Player?.Stop();
        playback.Player = null;
    }

    private static void PlayHover(DeleteButtonPlayback playback)
    {
        if (playback.Player is not { } player)
            return;

        player.Stop();
        _ = player.PlayAsync(
            fromProgress: 0,
            toProgress: 1,
            looped: false);
    }

    private static void CloseImmediately(DeleteButtonPlayback playback)
    {
        if (playback.Player is not { } player)
            return;

        player.Stop();
        player.SetProgress(0);
    }

    private static void UpdateForeground(
        AnimatedVisualPlayer player,
        Tonarink.DeleteIcon source,
        bool critical)
    {
        if (critical
            && Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(
                "SystemFillColorCritical",
                out var resource)
            && resource is Windows.UI.Color criticalColor)
        {
            source.Foreground = criticalColor;
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

    private sealed class DeleteButtonPlayback
    {
        public AnimatedVisualPlayer? Player { get; set; }

        public Action? DetachPointerHandlers { get; set; }
    }
}
