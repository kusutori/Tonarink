using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components;

sealed record StartupSplashOverlayProps(Action<bool> SetVisible);

sealed class StartupSplashOverlay : Component<StartupSplashOverlayProps>
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(280);

    public override Element Render()
    {
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
            .Background(Theme.SolidBackground)
            .Opacity(opacity)
            .OpacityTransition(FadeDuration)
            .IsHitTestVisible(opacity > 0);

        async Task PlayThenFadeAsync(AnimatedVisualPlayer player)
        {
            try
            {
                player.Source = new Tonarink.SplashLogo();
                await player.PlayAsync(fromProgress: 0, toProgress: 1, looped: false);
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report("Could not play the startup animation", exception);
            }

            if (!alive.Current || completionStarted.Current)
                return;

            completionStarted.Current = true;
            setOpacity(0);
            await Task.Delay(FadeDuration);

            if (alive.Current)
                Props.SetVisible(false);
        }
    }
}
