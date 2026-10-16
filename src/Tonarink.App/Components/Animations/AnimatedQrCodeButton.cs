using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedQrCodeButtonProps(
    string AutomationName,
    Action OnClick,
    string? ToolTip = null,
    bool IsEnabled = true);

sealed class AnimatedQrCodeButton : Component<AnimatedQrCodeButtonProps>
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

                    var source = new Tonarink.QrRippleIcon();
                    AnimatedIconForeground.Apply(player, source);
                    player.Loaded += (_, _) =>
                    {
                        AnimatedIconForeground.Apply(player, source);
                        player.SetProgress(0);
                    };
                    player.ActualThemeChanged += (_, _) => AnimatedIconForeground.Apply(player, source);
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
                    if (!reduceMotion && playerRef.Current is { } player)
                    {
                        player.Stop();
                        player.SetProgress(0);
                        _ = player.PlayAsync(
                            fromProgress: 0,
                            toProgress: 1,
                            looped: false);
                    }

                    Props.OnClick();
                })
            .SubtleButton()
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .MinWidth(40)
            .MinHeight(40)
            .Padding(0, 0)
            .IsEnabled(Props.IsEnabled);
    }

}
