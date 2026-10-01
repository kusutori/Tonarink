using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedFileSelectionButtonProps(
    string Label,
    string AutomationName,
    Action OnClick,
    bool IsEnabled = true);

sealed class AnimatedFileSelectionButton : Component<AnimatedFileSelectionButtonProps>
{
    public override Element Render()
    {
        var playerRef = UseRef<AnimatedVisualPlayer?>();
        var reduceMotion = UseReducedMotion();

        return Button(
                VStack(8,
                    (AnimatedVisualPlayer() with { AutoPlay = false })
                    .Size(32, 32)
                    .IsHitTestVisible(false)
                    .AccessibilityHidden()
                    .OnMountAdd(element =>
                    {
                        if (element is not AnimatedVisualPlayer player)
                            return;

                        var source = new Tonarink.DocumentUnfoldIcon();
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
                    BodyStrong(Props.Label)),
                () =>
                {
                    if (!reduceMotion && playerRef.Current is { } player)
                    {
                        player.Stop();
                        player.SetProgress(0);
                        _ = player.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
                    }

                    Props.OnClick();
                })
            .MinHeight(104)
            .HAlign(HorizontalAlignment.Stretch)
            .AutomationName(Props.AutomationName)
            .IsEnabled(Props.IsEnabled);
    }

    private static void UpdateForeground(
        AnimatedVisualPlayer player,
        Tonarink.DocumentUnfoldIcon source)
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
