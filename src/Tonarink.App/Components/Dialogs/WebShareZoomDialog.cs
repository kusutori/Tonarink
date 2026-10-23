using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record WebShareZoomDialogProps(string? Url, string? Pin, ElementTheme Theme, Action Close);

sealed class WebShareZoomDialog : Component<WebShareZoomDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        return (ContentDialog(
                t.Message(new("App", "WebShareZoomTitle")),
                VStack(16,
                    Title(Props.Url ?? "")
                        .TextWrapping(TextWrapping.WrapWholeWords)
                        .IsTextSelectionEnabled()
                        .AutomationName(t.Message(new("App", "WebShareZoomTitle"))),
                    string.IsNullOrWhiteSpace(Props.Pin)
                        ? null
                        : Grid(
                                columns: [GridSize.Auto, GridSize.Star()],
                                rows: [GridSize.Auto],
                                Icon(AppIcons.WebSharePin)
                                    .AccessibilityHidden()
                                    .VAlign(VerticalAlignment.Center)
                                    .Margin(right: 8)
                                    .Grid(column: 0),
                                Title(Props.Pin)
                                    .TextWrapping(TextWrapping.WrapWholeWords)
                                    .TextAlignment(TextAlignment.Center)
                                    .IsTextSelectionEnabled()
                                    .AutomationName(t.Message(new("App", "WebSharePinHint"), ("pin", Props.Pin)))
                                    .Grid(column: 1))
                            .HAlign(HorizontalAlignment.Center)),
                primaryButtonText: t.Message(new("App", "Close"))) with
        {
            IsOpen = Props.Url is not null,
            OnClosed = _ => Props.Close(),
        }).Themed(Props.Theme);
    }
}
