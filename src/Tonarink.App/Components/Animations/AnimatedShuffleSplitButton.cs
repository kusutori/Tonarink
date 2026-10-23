using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using XamlAnimatedVisualPlayer = Microsoft.UI.Xaml.Controls.AnimatedVisualPlayer;

namespace Tonarink.Components.Animations;

sealed record AnimatedShuffleSplitButtonProps(string AutomationName, Action OnClick, Element Flyout);

sealed class AnimatedShuffleSplitButton : Component<AnimatedShuffleSplitButtonProps>
{
    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        var animationVersion = UseRef(0);
        var source = UseMemo(() => new ShufflePinIcon());
        // SplitButton currently exposes only a string Label, not an Element
        // content slot. Keep one player for this unwrapped slot, just as the
        // other click-triggered icons do, without relying on template states.
        var player = UseMemo(() => new XamlAnimatedVisualPlayer
        {
            AutoPlay = false,
            Width = 20,
            Height = 20,
            IsHitTestVisible = false,
        });

        return SplitButton("", () =>
            {
                if (!reduceMotion)
                {
                    var version = ++animationVersion.Current;
                    player.Stop();
                    player.SetProgress(0);
                    _ = PlayAndSettleAsync(version);
                }

                Props.OnClick();
            })
            // Unlike the constructor Flyout slot, this modifier reconciles
            // existing popup content in place while the slider is dragged.
            .WithFlyout(Props.Flyout)
            .AutomationName(Props.AutomationName)
            .AutomationId("WebShare.Pin.Shuffle")
            .ToolTip(Props.AutomationName)
            .Set(button =>
            {
                if (!ReferenceEquals(button.Content, player))
                    button.Content = player;
            })
            .OnMountAdd(element =>
            {
                if (element is SplitButton button)
                    AnimatedPlayerLifecycle.Mount(player, source,
                        () => AnimatedIconForeground.Apply(player, source, button),
                        () => player.SetProgress(0));
            })
            .OnUnmountAdd(_ =>
            {
                animationVersion.Current++;
                AnimatedPlayerLifecycle.Unmount(player);
            });

        async Task PlayAndSettleAsync(int version)
        {
            await player.PlayAsync(0, 1, looped: false);
            if (version == animationVersion.Current)
                player.SetProgress(0);
        }
    }
}
