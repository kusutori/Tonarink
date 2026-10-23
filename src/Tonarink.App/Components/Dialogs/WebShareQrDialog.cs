using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record WebShareQrDialogProps(string? Url, ElementTheme Theme, Action Close);

sealed class WebShareQrDialog : Component<WebShareQrDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        return (ContentDialog(
                t.Message(new("App", "WebShareQrTitle")),
                Props.Url is not { } url
                    ? Empty()
                    : VStack(12,
                        QrCodeCanvas.Render(url, t.Message(new("App", "WebShareQrTitle"))),
                        TextBlock(url)
                            .TextWrapping(TextWrapping.WrapWholeWords)
                            .IsTextSelectionEnabled()),
                primaryButtonText: t.Message(new("App", "Close"))) with
        {
            IsOpen = Props.Url is not null,
            OnClosed = _ => Props.Close(),
        }).Themed(Props.Theme);
    }
}
