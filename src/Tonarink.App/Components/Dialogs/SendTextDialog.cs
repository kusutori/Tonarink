using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record SendTextDialogProps(
    ElementTheme Theme,
    bool IsOpen,
    string Text,
    Action<string> SetText,
    Action<string> AddText,
    Action Close);

sealed class SendTextDialog : Component<SendTextDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        return (ContentDialog(
                t.Message(new("App", "SendTextTitle")),
                TextBox(
                        Props.Text,
                        Props.SetText,
                        placeholderText: t.Message(new("App", "SendTextPlaceholder")))
                    .Header(t.Message(new("App", "TextContent")))
                    .AutomationName(t.Message(new("App", "TextContent")))
                    .Required()
                    .AcceptsReturn()
                    .TextWrapping()
                    .MinHeight(160),
                primaryButtonText: t.Message(new("App", "Add"))) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            OnClosed = result =>
            {
                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(Props.Text))
                {
                    Props.AddText(Props.Text);
                    Props.SetText(string.Empty);
                }

                Props.Close();
            },
        }).Themed(Props.Theme);
    }
}
