using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedFavoritesButtonProps(
    string AutomationName,
    Action OnClick,
    string? ToolTip = null,
    bool IsEnabled = true,
    bool IsDialogOpen = false);

sealed class AnimatedFavoritesButton : Component<AnimatedFavoritesButtonProps>
{
    private const double SunkProgress = 18d / 48d;
    private const double RiseStartProgress = 24d / 48d;
    private const double RestoredProgress = 43d / 48d;

    public override Element Render()
    {
        var playerRef = UseRef<AnimatedVisualPlayer?>();
        var isSunk = !Props.IsEnabled || Props.IsDialogOpen;
        var sunkRef = UseRef(isSunk);
        var previousSunkRef = UseRef(isSunk);
        var transitionVersionRef = UseRef(0);
        var reduceMotion = UseReducedMotion();
        sunkRef.Current = isSunk;

        UseEffect(() =>
        {
            var wasSunk = previousSunkRef.Current;
            previousSunkRef.Current = isSunk;
            if (wasSunk == isSunk
                || playerRef.Current is not { } player)
            {
                return;
            }

            player.Stop();
            var transitionVersion = ++transitionVersionRef.Current;
            if (reduceMotion)
            {
                player.SetProgress(isSunk ? SunkProgress : RestoredProgress);
                return;
            }

            var targetSunk = isSunk;
            _ = PlayAndSettleAsync();

            async Task PlayAndSettleAsync()
            {
                await player.PlayAsync(
                    fromProgress: targetSunk ? 0 : RiseStartProgress,
                    toProgress: targetSunk ? SunkProgress : RestoredProgress,
                    looped: false);

                if (transitionVersion == transitionVersionRef.Current)
                {
                    player.SetProgress(
                        sunkRef.Current ? SunkProgress : RestoredProgress);
                }
            }
        }, isSunk, reduceMotion);

        return Button(
                (AnimatedVisualPlayer() with { AutoPlay = false })
                .Size(24, 24)
                .Opacity(Props.IsEnabled ? 1 : 0.36)
                .AccessibilityHidden()
                .OnMountAdd(element =>
                {
                    if (element is not AnimatedVisualPlayer player)
                        return;

                    var source = new Tonarink.FavoriteListIcon();
                    UpdateForeground(player, source);
                    player.Loaded += (_, _) =>
                    {
                        UpdateForeground(player, source);
                        player.SetProgress(
                            sunkRef.Current ? SunkProgress : RestoredProgress);
                    };
                    player.ActualThemeChanged += (_, _) => UpdateForeground(player, source);
                    player.Source = source;
                    player.SetProgress(
                        sunkRef.Current ? SunkProgress : RestoredProgress);
                    playerRef.Current = player;
                })
                .OnUnmountAdd(element =>
                {
                    if (element is AnimatedVisualPlayer player)
                        player.Stop();

                    playerRef.Current = null;
                }),
                Props.OnClick)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .Size(44, 40)
            .Padding(0, 0)
            .IsEnabled(Props.IsEnabled);
    }

    private static void UpdateForeground(
        AnimatedVisualPlayer player,
        Tonarink.FavoriteListIcon source)
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
