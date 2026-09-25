using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Shell;

sealed record StartupSplashOverlayProps(
    Action BeginDismissal,
    Action CompleteDismissal);

sealed class StartupSplashOverlay : Component<StartupSplashOverlayProps>
{
    internal static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(280);

    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        var (opacity, setOpacity) = UseState(1.0);
        var alive = UseRef(true);
        var completionStarted = UseRef(false);

        UseEffect(() => () => { alive.Current = false; });

        return Border(
                (AnimatedVisualPlayer() with { AutoPlay = false })
                .Size(256, 256)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .AutomationName("Tonarink")
                .OnMountAdd(element =>
                {
                    if (element is AnimatedVisualPlayer player)
                        _ = PlayThenFadeAsync(player);
                }))
            .Opacity(opacity)
            .OpacityTransition(reduceMotion ? TimeSpan.Zero : FadeDuration)
            .IsHitTestVisible(opacity > 0);

        async Task PlayThenFadeAsync(AnimatedVisualPlayer player)
        {
            try
            {
                if (!reduceMotion)
                {
                    player.Source = new Tonarink.SplashLogo();
                    await player.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
                }
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report("Could not play the startup animation", exception);
            }

            if (!alive.Current || completionStarted.Current)
                return;

            completionStarted.Current = true;
            Props.BeginDismissal();
            setOpacity(0);
            if (reduceMotion)
            {
                Props.CompleteDismissal();
                return;
            }

            await Task.Delay(FadeDuration);

            if (alive.Current)
                Props.CompleteDismissal();
        }
    }
}
