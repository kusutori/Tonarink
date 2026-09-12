using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record DeleteFavoriteDialogProps(
    string DeviceName,
    ElementTheme Theme,
    bool IsOpen,
    Action Confirm,
    Action Close);

sealed class DeleteFavoriteDialog : Component<DeleteFavoriteDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        return (ContentDialog(
                t.Message(new("App", "DeleteFavoriteTitle")),
                TextBlock(t.Message(
                        new("App", "DeleteFavoriteConfirm"),
                        ("device", Props.DeviceName)))
                    .TextWrapping(TextWrapping.WrapWholeWords),
                primaryButtonText: t.Message(new("App", "Delete"))) with
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
