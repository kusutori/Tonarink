using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedAddressTargetButtonProps(
    string AutomationName,
    Action OnClick,
    string? ToolTip = null,
    bool IsEnabled = true);

sealed class AnimatedAddressTargetButton : Component<AnimatedAddressTargetButtonProps>
{
    private const double SeparatedProgress = 45d / 48d;
    private const double HitProgress = 21d / 48d;
    private const double RetractStartProgress = 32d / 48d;

    public override Element Render()
    {
        var playerRef = UseRef<AnimatedVisualPlayer?>();
        var enabledRef = UseRef(Props.IsEnabled);
        var previousEnabledRef = UseRef(Props.IsEnabled);
        var transitionVersionRef = UseRef(0);
        var reduceMotion = UseReducedMotion();
        enabledRef.Current = Props.IsEnabled;

        UseEffect(() =>
        {
            var wasEnabled = previousEnabledRef.Current;
            previousEnabledRef.Current = Props.IsEnabled;
            if (wasEnabled == Props.IsEnabled
                || playerRef.Current is not { } player)
            {
                return;
            }

            player.Stop();
            var transitionVersion = ++transitionVersionRef.Current;
            if (reduceMotion)
            {
                player.SetProgress(Props.IsEnabled ? HitProgress : SeparatedProgress);
                return;
            }

            var targetEnabled = Props.IsEnabled;
            _ = PlayAndSettleAsync();

            async Task PlayAndSettleAsync()
            {
                await player.PlayAsync(
                    fromProgress: targetEnabled ? 0 : RetractStartProgress,
                    toProgress: targetEnabled ? HitProgress : SeparatedProgress,
                    looped: false);

                if (transitionVersion == transitionVersionRef.Current)
                {
                    player.SetProgress(
                        enabledRef.Current ? HitProgress : SeparatedProgress);
                }
            }
        }, Props.IsEnabled, reduceMotion);

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

                    var source = new Tonarink.AddressTargetIcon();
                    UpdateForeground(player, source);
                    player.Loaded += (_, _) =>
                    {
                        UpdateForeground(player, source);
                        player.SetProgress(
                            enabledRef.Current ? HitProgress : SeparatedProgress);
                    };
                    player.ActualThemeChanged += (_, _) => UpdateForeground(player, source);
                    player.Source = source;
                    player.SetProgress(
                        enabledRef.Current ? HitProgress : SeparatedProgress);
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
                    if (!reduceMotion && playerRef.Current is { } player)
                    {
                        player.Stop();
                        player.SetProgress(0);
                        _ = player.PlayAsync(
                            fromProgress: 0,
                            toProgress: HitProgress,
                            looped: false);
                    }

                    Props.OnClick();
                })
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .Size(44, 40)
            .Padding(0, 0)
            .IsEnabled(Props.IsEnabled);
    }

    private static void UpdateForeground(
        AnimatedVisualPlayer player,
        Tonarink.AddressTargetIcon source)
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
