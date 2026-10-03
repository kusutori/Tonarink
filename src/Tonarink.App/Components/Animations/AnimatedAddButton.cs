using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedAddButtonProps(
    string AutomationName,
    Action OnClick,
    string? Label = null,
    string? ToolTip = null,
    bool IsEnabled = true,
    double IconSize = 20);

sealed class AnimatedAddButton : Component<AnimatedAddButtonProps>
{
    public override Element Render()
    {
        var playerRef = UseRef<AnimatedVisualPlayer?>();
        var animationVersionRef = UseRef(0);
        var reduceMotion = UseReducedMotion();

        var icon = (AnimatedVisualPlayer() with { AutoPlay = false })
            .Size(Props.IconSize, Props.IconSize)
            .Opacity(Props.IsEnabled ? 1 : 0.36)
            .IsHitTestVisible(false)
            .AccessibilityHidden()
            .OnMountAdd(element =>
            {
                if (element is not AnimatedVisualPlayer player)
                    return;

                var source = new Tonarink.AddRippleIcon();
                UpdateForeground(player, source);
                player.Loaded += (_, _) =>
                {
                    UpdateForeground(player, source);
                    player.SetProgress(0);
                };
                player.ActualThemeChanged += (_, _) => UpdateForeground(player, source);
                player.Source = source;
                player.SetProgress(0);
                playerRef.Current = player;
            })
            .OnUnmountAdd(element =>
            {
                if (element is AnimatedVisualPlayer player)
                    player.Stop();

                animationVersionRef.Current++;
                playerRef.Current = null;
            });

        Element content = Props.Label is null
            ? icon
            : HStack(8, icon, TextBlock(Props.Label));

        var button = Button(
                content,
                () =>
                {
                    if (!reduceMotion && playerRef.Current is { } player)
                    {
                        var version = ++animationVersionRef.Current;
                        player.Stop();
                        player.SetProgress(0);
                        _ = PlayAndSettleAsync(player, version);
                    }

                    Props.OnClick();
                })
            .AutomationName(Props.AutomationName)
            .IsEnabled(Props.IsEnabled);

        if (Props.ToolTip is { } toolTip)
            button = button.ToolTip(toolTip);
        else if (Props.Label is null)
            button = button.ToolTip(Props.AutomationName);

        return button;

        async Task PlayAndSettleAsync(AnimatedVisualPlayer player, int version)
        {
            await player.PlayAsync(0, 1, looped: false);
            if (version == animationVersionRef.Current && playerRef.Current == player)
                player.SetProgress(0);
        }
    }

    private static void UpdateForeground(
        AnimatedVisualPlayer player,
        Tonarink.AddRippleIcon source)
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
}
