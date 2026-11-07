using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Pages.History;

sealed record HistoryTypeExpandButtonProps(
    bool Collapse,
    bool IsAnimating,
    Action Click,
    HistoryTypeReveal Reveal);

sealed class HistoryTypeExpandButton : Component<HistoryTypeExpandButtonProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var playerRef = UseRef<AnimatedVisualPlayer?>();
        var reduceMotion = UseReducedMotion();
        var collapseSource = UseMemo(static () => new CollapseTypesIcon());
        var fallback = UseMemo(static () => new FontIconSource { Glyph = AppIcons.Collapse });
        var label = t.Message(new("App", Props.Collapse
            ? "HistoryFilterCollapseTypes"
            : "HistoryFilterMoreTypes"));

        Element icon = Props.Collapse
            ? AnimatedIcon(collapseSource, fallback).Size(16, 16)
            : (AnimatedVisualPlayer() with { AutoPlay = false })
                .Size(16, 16)
                .OnMountAdd(element =>
                {
                    if (element is not AnimatedVisualPlayer player) return;
                    var source = new MoreTriangleIcon();
                    AnimatedPlayerLifecycle.Mount(player, source,
                        () => AnimatedIconForeground.Apply(player, source),
                        () => player.SetProgress(0));
                    playerRef.Current = player;
                })
                .OnUnmountAdd(element =>
                {
                    if (element is AnimatedVisualPlayer player)
                        AnimatedPlayerLifecycle.Unmount(player);
                    playerRef.Current = null;
                });

        return Button(icon.IsHitTestVisible(false).AccessibilityHidden(), () =>
            {
                if (Props.IsAnimating) return;
                if (!Props.Collapse && !reduceMotion && playerRef.Current is { } player)
                {
                    player.Stop();
                    player.SetProgress(0);
                    _ = player.PlayAsync(0, 1, looped: false);
                }
                Props.Click();
            })
            .MinWidth(32)
            .MinHeight(32)
            .Padding(10, 6)
            .VAlign(VerticalAlignment.Stretch)
            .IsHitTestVisible(!Props.IsAnimating)
            .IsTabStop(!Props.IsAnimating)
            .AutomationName(label)
            .ToolTip(label)
            .AutomationId("History.Filter.MoreTypes")
            .OnMountAdd(Props.Reveal.AttachMore)
            .OnUnmountAdd(Props.Reveal.DetachMore);
    }
}
