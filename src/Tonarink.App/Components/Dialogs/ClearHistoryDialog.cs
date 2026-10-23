using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record ClearHistoryDialogProps(bool IsOpen, ElementTheme Theme, Action Confirm, Action Close);

sealed class ClearHistoryDialog : Component<ClearHistoryDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        return (ContentDialog(
                t.Message(new("App", "HistoryDeleteAllConfirm")),
                TextBlock(t.Message(new("App", "HistoryDeleteAllConfirmMessage")))
                    .TextWrapping(TextWrapping.WrapWholeWords),
                primaryButtonText: t.Message(new("App", "HistoryDeleteAll"))) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            DefaultButton = ContentDialogButton.Primary,
            OnClosed = result =>
            {
                if (result == ContentDialogResult.Primary)
                    Props.Confirm();
                Props.Close();
            },
        }).Themed(Props.Theme);
    }
}
