using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

public static class AnimatedButtons
{
    public static Element CopyFeedback(
        int successVersion,
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedCopyButton, AnimatedCopyButtonProps>(
            new(successVersion, automationName, onClick, toolTip, isEnabled));

    public static Element Refresh(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true,
        int durationMilliseconds = 500) =>
        Component<AnimatedRefreshButton, AnimatedRefreshButtonProps>(
            new(automationName, onClick, toolTip, isEnabled, durationMilliseconds));
}

sealed record AnimatedCopyButtonProps(
    int SuccessVersion,
    string AutomationName,
    Action OnClick,
    string? ToolTip = null,
    bool IsEnabled = true);

sealed class AnimatedCopyButton : Component<AnimatedCopyButtonProps>
{
    public override Element Render()
    {
        var playing = Props.SuccessVersion > 0;
        var copyIcon = Border(Icon("\uE8C8"))
            .OnMount(BindCompositionCenterPoint)
            .Keyframes("copy-feedback-out", Props.SuccessVersion, keyframes => playing
                ? keyframes
                    .Duration(1433)
                    .At(0.000f, opacity: 1, scale: new(1, 1, 1))
                    .At(0.093f, opacity: 0, scale: new(0.273f, 0.273f, 1),
                        easing: Easing.CubicBezier(0.13f, 0, 0, 1))
                    .At(0.814f, opacity: 0, scale: new(0.273f, 0.273f, 1))
                    .At(0.837f, opacity: 0, scale: new(1, 1, 1))
                    .At(0.907f, opacity: 0, scale: new(1, 1, 1))
                    .At(1.000f, opacity: 1, scale: new(1, 1, 1), easing: Easing.EaseOut)
                : keyframes
                    .Duration(1)
                    .At(0f, opacity: 1, scale: new(1, 1, 1))
                    .At(1f, opacity: 1, scale: new(1, 1, 1)));
        var successIcon = Border(Icon("\uE73E"))
            .OnMount(BindCompositionCenterPoint)
            .Opacity(0)
            .Keyframes("copy-feedback-in", Props.SuccessVersion, keyframes => playing
                ? keyframes
                    .Duration(1433)
                    .At(0.000f, opacity: 0, scale: new(0.385f, 0.385f, 1))
                    .At(0.093f, opacity: 0, scale: new(0.385f, 0.385f, 1))
                    .At(0.186f, opacity: 1, scale: new(1.146f, 1.146f, 1),
                        easing: Easing.CubicBezier(0.39f, 0, 0.63f, 1))
                    .At(0.232f, opacity: 1, scale: new(1, 1, 1),
                        easing: Easing.CubicBezier(0.55f, 0, 0.02f, 1))
                    .At(0.814f, opacity: 1, scale: new(1, 1, 1))
                    .At(0.907f, opacity: 0, scale: new(0.385f, 0.385f, 1),
                        easing: Easing.EaseIn)
                    .At(1.000f, opacity: 0, scale: new(0.385f, 0.385f, 1))
                : keyframes
                    .Duration(1)
                    .At(0f, opacity: 0, scale: new(0.385f, 0.385f, 1))
                    .At(1f, opacity: 0, scale: new(0.385f, 0.385f, 1)));

        return Button(
                Grid(
                    columns: [GridSize.Auto],
                    rows: [GridSize.Auto],
                    copyIcon,
                    successIcon),
                Props.OnClick)
            .SubtleButton()
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .IsEnabled(Props.IsEnabled);
    }

    private static void BindCompositionCenterPoint(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var centerPoint = visual.Compositor.CreateExpressionAnimation(
            "Vector3(target.Size.X / 2, target.Size.Y / 2, 0)");
        centerPoint.SetReferenceParameter("target", visual);
        visual.StartAnimation("CenterPoint", centerPoint);
    }
}

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

        return Button(
                Icon("Refresh")
                    .RotationTransition(TimeSpan.FromMilliseconds(Props.DurationMilliseconds))
                    .Rotation(turns * 360f)
                    .OnSizeChanged(CenterRotation),
                () =>
                {
                    setTurns(turns + 1);
                    Props.OnClick();
                })
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
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
