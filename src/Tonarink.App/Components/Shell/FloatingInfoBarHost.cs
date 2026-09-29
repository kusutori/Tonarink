using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Shell;

sealed class FloatingInfoBarSlot
{
    public AppRoute? Owner { get; set; }
    public Func<Element>? Build { get; set; }
    public required Action Invalidate { get; init; }
}

sealed record FloatingInfoBarHostProps(Element Content);

sealed class FloatingInfoBarHost : Component<FloatingInfoBarHostProps>
{
    internal static readonly Context<FloatingInfoBarSlot> Slot =
        new(new FloatingInfoBarSlot { Invalidate = static () => { } });

    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();

        return Border(Props.Content)
            .Margin(left: 24, top: 72, right: AppLayout.PageHeaderPaddingHorizontal)
            .MaxWidth(560)
            .HAlign(HorizontalAlignment.Right)
            .VAlign(VerticalAlignment.Top)
            .IsHitTestVisible(true)
            .Transition(Transition.Exit(new FadeTransition()));
    }
}

sealed record FloatingInfoBarItemProps(Element Content);

sealed class FloatingInfoBarItem : Component<FloatingInfoBarItemProps>
{
    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        var transition = Transition.Enter(new FadeTransition())
                         | Transition.Exit(new FadeTransition());

        if (!reduceMotion)
            transition = Transition.Enter(Transition.Slide(Edge.Right))
                         | Transition.Exit(new FadeTransition());

        return Border(Props.Content).Transition(transition);
    }
}
