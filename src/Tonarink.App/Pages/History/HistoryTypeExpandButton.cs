using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
        var pressedToken = UseRef<long?>();
        var label = t.Message(new("App", Props.Collapse
            ? "HistoryFilterCollapseTypes"
            : "HistoryFilterMoreTypes"));

        return Button(Icon(Props.Collapse ? AppIcons.Collapse : AppIcons.More).AccessibilityHidden(), Props.Click)
            .Size(40, 40)
            .Padding(0)
            .IsHitTestVisible(!Props.IsAnimating)
            .IsTabStop(!Props.IsAnimating)
            .AutomationName(label)
            .ToolTip(label)
            .AutomationId("History.Filter.MoreTypes")
            .OnMountAdd(element =>
            {
                Props.Reveal.AttachMore(element);
                if (element is not Button button) return;
                button.CornerRadius = new CornerRadius(20);
                // Button handles pointer presses internally. Observe its actual
                // pressed state to cover mouse, touch, keyboard and cancellation.
                pressedToken.Current = button.RegisterPropertyChangedCallback(ButtonBase.IsPressedProperty,
                    static (sender, _) =>
                    {
                        if (sender is Button current)
                            current.CornerRadius = current.IsPressed
                                ? ThemeResource.CornerRadius("ControlCornerRadius")
                                : new CornerRadius(20);
                    });
            })
            .OnUnmountAdd(element =>
            {
                if (element is Button button && pressedToken.Current is { } token)
                {
                    button.UnregisterPropertyChangedCallback(ButtonBase.IsPressedProperty, token);
                    button.ClearValue(Control.CornerRadiusProperty);
                    pressedToken.Current = null;
                }
                Props.Reveal.DetachMore(element);
            });
    }
}
