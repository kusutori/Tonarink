using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

sealed record AnimatedCopyButtonProps(
    int SuccessVersion,
    string AutomationName,
    Action OnClick,
    string? SuccessAnnouncement = null,
    string? ToolTip = null,
    bool IsEnabled = true);

sealed class AnimatedCopyButton : Component<AnimatedCopyButtonProps>
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
        var copyIcon = AnimatedFeedback.Outgoing(Border(Icon(AppIcons.Copy).AccessibilityHidden())
            .OnMount(AnimatedFeedback.BindCompositionCenterPoint),
            "copy-feedback-out", Props.SuccessVersion, playing);
        var successIcon = AnimatedFeedback.Incoming(Border(Icon(AppIcons.Success).AccessibilityHidden())
            .OnMount(AnimatedFeedback.BindCompositionCenterPoint),
            "copy-feedback-in", Props.SuccessVersion, playing);

        return Button(
                Grid(
                    columns: [GridSize.Auto],
                    rows: [GridSize.Auto],
                    copyIcon,
                    successIcon,
                    announce.Region),
                Props.OnClick)
            .SubtleButton()
            .MinWidth(40)
            .MinHeight(40)
            .AutomationName(Props.AutomationName)
            .ToolTip(Props.ToolTip ?? Props.AutomationName)
            .IsEnabled(Props.IsEnabled);
    }

}
