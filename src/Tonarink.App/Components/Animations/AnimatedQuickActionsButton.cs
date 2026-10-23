using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedQuickActionsButtonProps(
    string AutomationName,
    Action OnClick,
    string? ToolTip = null,
    bool IsEnabled = true,
    double IconSize = 24);

sealed class AnimatedQuickActionsButton : Component<AnimatedQuickActionsButtonProps>
{
    public override Element Render()
    {
        var playerRef = UseRef<AnimatedVisualPlayer?>();
        var animationVersionRef = UseRef(0);
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

                    var source = new QuickActionsSparkleIcon();
                    AnimatedPlayerLifecycle.Mount(player, source,
                        () => AnimatedIconForeground.Apply(player, source),
                        () => player.SetProgress(0));
                    playerRef.Current = player;
                })
                .OnUnmountAdd(element =>
                {
                    if (element is AnimatedVisualPlayer player)
                        AnimatedPlayerLifecycle.Unmount(player);

                    animationVersionRef.Current++;
                    playerRef.Current = null;
                }),
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
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .Size(40, 40)
            .Padding(0, 0)
            .IsEnabled(Props.IsEnabled);

        async Task PlayAndSettleAsync(AnimatedVisualPlayer player, int version)
        {
            await player.PlayAsync(0, 1, looped: false);
            if (version == animationVersionRef.Current && playerRef.Current == player)
                player.SetProgress(0);
        }
    }

}
