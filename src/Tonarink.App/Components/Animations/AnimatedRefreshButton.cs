using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using System.Numerics;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedRefreshButtonProps(
    string AutomationName,
    Action OnClick,
    string? ToolTip = null,
    bool IsEnabled = true,
    int DurationMilliseconds = 500);

sealed class AnimatedRefreshButton : Component<AnimatedRefreshButtonProps>
{
    public override Element Render()
    {
        var (turns, setTurns) = UseState(0);
        var reduceMotion = UseReducedMotion();

        return Button(
                Icon("Sync").AccessibilityHidden()
                    .RotationTransition(TimeSpan.FromMilliseconds(Props.DurationMilliseconds))
                    .Rotation(turns * 360f)
                    .OnSizeChanged(CenterRotation),
                () =>
                {
                    if (!reduceMotion)
                        setTurns(turns + 1);
                    Props.OnClick();
                })
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .Size(44, 40)
            .Padding(0, 0)
            .IsEnabled(Props.IsEnabled);
    }

    private static void CenterRotation(object sender, SizeChangedEventArgs args)
    {
        if (sender is not UIElement element
            || args.NewSize.Width <= 0
            || args.NewSize.Height <= 0)
            return;

        element.CenterPoint = new Vector3(
            (float)(args.NewSize.Width / 2),
            (float)(args.NewSize.Height / 2),
            0);
    }
}
