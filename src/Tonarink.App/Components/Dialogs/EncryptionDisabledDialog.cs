using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record EncryptionDisabledDialogProps(bool IsOpen, ElementTheme Theme, Action Close);

sealed class EncryptionDisabledDialog : Component<EncryptionDisabledDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        return (ContentDialog(
                t.Message(new("App", "SettingsEncryptionDisabledTitle")),
                TextBlock(t.Message(new("App", "SettingsEncryptionDisabledNotice")))
                    .TextWrapping(TextWrapping.WrapWholeWords),
                primaryButtonText: t.Message(new("App", "Close"))) with
        {
            IsOpen = Props.IsOpen,
            DefaultButton = ContentDialogButton.Close,
            OnClosed = _ => Props.Close(),
        }).Themed(Props.Theme);
    }
}
