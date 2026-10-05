using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedVerifyButtonProps(
    int SuccessVersion,
    string AutomationName,
    Action OnClick,
    string? SuccessAnnouncement = null,
    bool IsEnabled = true,
    bool Succeeded = true);

sealed class AnimatedVerifyButton : Component<AnimatedVerifyButtonProps>
{
    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        var playing = Props.SuccessVersion > 0 && !reduceMotion;
        var announce = this.UseAnnounce();
        var previousVersion = UseRef(Props.SuccessVersion);
        UseEffect(() =>
        {
            if (Props.SuccessVersion > previousVersion.Current
                && !string.IsNullOrWhiteSpace(Props.SuccessAnnouncement))
            {
                announce.Announce(Props.SuccessAnnouncement);
            }

            previousVersion.Current = Props.SuccessVersion;
        }, Props.SuccessVersion, Props.SuccessAnnouncement);

        // Match AnimatedCopyButton's timings and easing: shrink the source icon,
        // pop in a check, hold it, then return to the original icon.
        var verifyIcon = FeedbackIcon(AppIcons.Verify)
            .Keyframes("verify-feedback-out", Props.SuccessVersion, keyframes => playing
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
        var successIcon = FeedbackIcon(Props.Succeeded ? AppIcons.Success : AppIcons.Error)
            .Opacity(0)
            .Keyframes("verify-feedback-in", Props.SuccessVersion, keyframes => playing
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
                Grid(columns: [GridSize.Auto], rows: [GridSize.Auto],
                        verifyIcon, successIcon, announce.Region)
                    .Size(24, 24)
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center),
                Props.OnClick)
            .SubtleButton()
            .MinWidth(0)
            .MinHeight(0)
            .Size(40, 40)
            .Padding(0, 0)
            .HorizontalContentAlignment(HorizontalAlignment.Center)
            .VerticalContentAlignment(VerticalAlignment.Center)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.AutomationName)
            .IsEnabled(Props.IsEnabled);
    }

    private static Element FeedbackIcon(string glyph) =>
        Border(Icon(new FontIconData(glyph, "Segoe Fluent Icons", 20))
                .AccessibilityHidden())
            .Size(24, 24)
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
            .OnMount(BindCompositionCenterPoint);

    private static void BindCompositionCenterPoint(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var centerPoint = visual.Compositor.CreateExpressionAnimation(
            "Vector3(target.Size.X / 2, target.Size.Y / 2, 0)");
        centerPoint.SetReferenceParameter("target", visual);
        visual.StartAnimation("CenterPoint", centerPoint);
    }
}
