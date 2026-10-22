using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedVerifyButtonProps(
    int SuccessVersion,
    string AutomationName,
    Action OnClick,
    string? SuccessAnnouncement = null,
    bool IsEnabled = true,
    bool Succeeded = true,
    string? AutomationId = null);

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
        var verifyIcon = AnimatedFeedback.Outgoing(FeedbackIcon(AppIcons.Verify),
            "verify-feedback-out", Props.SuccessVersion, playing);
        var successIcon = AnimatedFeedback.Incoming(FeedbackIcon(Props.Succeeded ? AppIcons.Success : AppIcons.Error),
            "verify-feedback-in", Props.SuccessVersion, playing);

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
            .AutomationId(Props.AutomationId ?? "")
            .ToolTip(Props.AutomationName)
            .IsEnabled(Props.IsEnabled);
    }

    private static Element FeedbackIcon(string glyph) =>
        Border(Icon(new FontIconData(glyph, "Segoe Fluent Icons", 20))
                .AccessibilityHidden())
            .Size(24, 24)
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
            .OnMount(AnimatedFeedback.BindCompositionCenterPoint);

}
