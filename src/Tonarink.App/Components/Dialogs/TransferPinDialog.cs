using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record TransferPinDialogProps(
    ElementTheme Theme,
    bool IsOpen,
    string DeviceName,
    string Pin,
    Action<string> UpdatePin,
    string? Error,
    Action<ContentDialogResult> Closed);

/// <summary>Shared PIN prompt; the transfer owner handles retry and cancellation.</summary>
sealed class TransferPinDialog : Component<TransferPinDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        return (ContentDialog(
                t.Message(new("App", "PinRequiredTitle")),
                VStack(8,
                    TextBlock(t.Message(new("App", "PinRequiredMessage"), ("device", Props.DeviceName)))
                        .TextWrapping(TextWrapping.WrapWholeWords),
                    PasswordBox(Props.Pin, Props.UpdatePin, placeholderText: t.Message(new("App", "PinPlaceholder")))
                        .Header(t.Message(new("App", "Pin")))
                        .AutomationName(t.Message(new("App", "Pin")))
                        .Required()
                        .MaxLength(32),
                    Props.Error is null
                        ? null
                        : TextBlock(Props.Error)
                            .Foreground(Theme.SystemCritical)
                            .LiveRegion(AutomationLiveSetting.Assertive)),
                primaryButtonText: t.Message(new("App", "PinConfirm"))) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(Props.Pin),
            OnClosed = Props.Closed,
        }).Themed(Props.Theme);
    }
}
