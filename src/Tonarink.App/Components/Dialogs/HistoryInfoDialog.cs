using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Components.Dialogs;

sealed record HistoryInfoDialogProps(ReceiveHistoryEntry? Entry, ElementTheme Theme, Action Close);

sealed class HistoryInfoDialog : Component<HistoryInfoDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        return (ContentDialog(
                t.Message(new("App", "HistoryInfoTitle")),
                Props.Entry is { } entry ? InfoBody(entry, t) : Empty(),
                primaryButtonText: t.Message(new("App", "Close"))) with
        {
            IsOpen = Props.Entry is not null,
            DefaultButton = ContentDialogButton.Primary,
            OnClosed = _ => Props.Close(),
        }).Themed(Props.Theme);
    }

    private static Element InfoBody(ReceiveHistoryEntry entry, IntlAccessor t) =>
        VStack(12,
            InfoRow(t.Message(new("App", "HistoryInfoFileName")), entry.FileName),
            InfoRow(t.Message(new("App", "HistoryInfoPath")), entry.Path),
            InfoRow(t.Message(new("App", "HistoryInfoSize")), FormatBytes(entry.Size)),
            InfoRow(t.Message(new("App", "HistoryInfoSender")), entry.SenderAlias),
            InfoRow(t.Message(new("App", "HistoryInfoTime")), entry.ReceivedAt.ToLocalTime().ToString("F")));

    private static Element InfoRow(string label, string value) =>
        VStack(4,
            Caption(label).Foreground(Theme.SecondaryText),
            TextBlock(value).TextWrapping(TextWrapping.WrapWholeWords));
}
