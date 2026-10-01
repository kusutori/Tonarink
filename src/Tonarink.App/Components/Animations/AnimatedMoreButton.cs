using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedMoreButtonProps(
    string AutomationName,
    Element Flyout,
    string? ToolTip = null,
    bool IsEnabled = true);

sealed class AnimatedMoreButton : Component<AnimatedMoreButtonProps>
{
    public override Element Render()
    {
        var playerRef = UseRef<AnimatedVisualPlayer?>();
        var reduceMotion = UseReducedMotion();

        return Button(
                (AnimatedVisualPlayer() with { AutoPlay = false })
                .Size(24, 24)
                .Opacity(Props.IsEnabled ? 1 : 0.36)
                .IsHitTestVisible(false)
                .AccessibilityHidden()
                .OnMountAdd(element =>
                {
                    if (element is not AnimatedVisualPlayer player)
                        return;

                    var source = new Tonarink.MoreTriangleIcon();
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

                    playerRef.Current = null;
                }),
                () =>
                {
                    if (reduceMotion || playerRef.Current is not { } player)
                        return;

                    player.Stop();
                    player.SetProgress(0);
                    _ = player.PlayAsync(
                        fromProgress: 0,
                        toProgress: 1,
                        looped: false);
                })
            .SubtleButton()
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .MinWidth(40)
            .MinHeight(40)
            .Padding(0, 0)
            .IsEnabled(Props.IsEnabled)
            .WithFlyout(Props.Flyout);
    }

    private static void UpdateForeground(
        AnimatedVisualPlayer player,
        Tonarink.MoreTriangleIcon source)
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
