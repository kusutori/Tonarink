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
        var length = UseRef(6);
        var setLength = UseMemo<Action<int>>(() => value => length.Current = value);
        var (draft, setDraft) = UseState("");
        UseEffect(() =>
        {
            if (Props.IsOpen)
                setDraft(GeneratePin(length.Current));
        }, Props.IsOpen);

        // The flyout must remain stable while its slider changes. Keep the
        // displayed length in that child, and read the latest value on shuffle.
        var lengthOptions = UseMemo(() => ContentFlyout(
            Component<WebSharePinLengthPicker, WebSharePinLengthPickerProps>(new(6, setLength)),
            FlyoutPlacementMode.Bottom));
        var regenerate = UseCallback(() => setDraft(GeneratePin(length.Current)));

        return (ContentDialog(
                t.Message(new("App", "WebSharePinTitle")),
                (Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    TextBox(draft, setDraft)
                        .AutomationName(t.Message(new("App", "WebSharePinTitle")))
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

sealed record WebSharePinLengthPickerProps(int InitialLength, Action<int> Changed);

sealed class WebSharePinLengthPicker : Component<WebSharePinLengthPickerProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var (length, setLength) = UseState(Props.InitialLength);
        var label = t.Message(new("App", "WebSharePinLength"));
        return VStack(12,
                BodyStrong(label),
                TextBlock(length.ToString(System.Globalization.CultureInfo.CurrentCulture))
                    .HAlign(HorizontalAlignment.Center),
                Slider(length, 4, 16, value =>
                    {
                        var next = (int)Math.Round(value);
                        setLength(next);
                        Props.Changed(next);
                    })
                    .StepFrequency(1)
                    .SnapsTo(SliderSnapsTo.StepValues)
                    .AutomationName(label))
            .Width(224);
    }
}
