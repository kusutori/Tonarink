using System.Security.Cryptography;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record WebSharePinDialogProps(
    bool IsOpen,
    ElementTheme Theme,
    Action<string> Apply,
    Action Close);

sealed class WebSharePinDialog : Component<WebSharePinDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var (length, setLength) = UseState(6);
        var (draft, setDraft) = UseState("");
        UseEffect(() =>
        {
            if (Props.IsOpen)
                setDraft(GeneratePin(length));
        }, Props.IsOpen);

        var lengthLabel = t.Message(new("App", "WebSharePinLength"));
        var lengthOptions = ContentFlyout(
            VStack(12,
                    HStack(8,
                        BodyStrong(lengthLabel),
                        BodyStrong(length.ToString(System.Globalization.CultureInfo.CurrentCulture))
                            .AutomationId("WebShare.Pin.LengthValue")),
                    Slider(length, 4, 16, value => setLength((int)Math.Round(value)))
                        .StepFrequency(1)
                        .SnapsTo(SliderSnapsTo.StepValues)
                        .AutomationName(lengthLabel)
                        .AutomationId("WebShare.Pin.Length"))
                .Width(224),
            FlyoutPlacementMode.Bottom);
        var regenerate = UseCallback(() => setDraft(GeneratePin(length)), length);

        return (ContentDialog(
                t.Message(new("App", "WebSharePinTitle")),
                (Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    TextBox(draft, setDraft)
                        .AutomationName(t.Message(new("App", "WebSharePinTitle")))
                        .AutomationId("WebShare.Pin.Input")
                        .Required()
                        .Grid(column: 0),
                    Component<AnimatedShuffleSplitButton, AnimatedShuffleSplitButtonProps>(new(
                            t.Message(new("App", "WebSharePinRegenerate")),
                            regenerate,
                            lengthOptions))
                        .VAlign(VerticalAlignment.Top)
                        .Grid(column: 1))) with
                {
                    ColumnSpacing = 8,
                },
                primaryButtonText: t.Message(new("App", "Confirm"))) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            DefaultButton = ContentDialogButton.Primary,
            OnClosed = result =>
            {
                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(draft))
                    Props.Apply(draft.Trim());
                Props.Close();
            },
        }).Themed(Props.Theme);
    }

    private static string GeneratePin(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return RandomNumberGenerator.GetString(alphabet, length);
    }
}
