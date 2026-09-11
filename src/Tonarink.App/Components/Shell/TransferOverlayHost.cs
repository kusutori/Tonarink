using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

sealed record TransferOverlayHostProps(
    LocalSendNode? Node,
    IncomingTransferRequest? Incoming,
    OutgoingTransferViewState? Outgoing,
    AppSettings Settings,
    ElementTheme Theme,
    Action<Guid> DismissIncoming,
    Action CloseOutgoing);

sealed class TransferOverlayHost : Component<TransferOverlayHostProps>
{
    public override Element Render()
    {
        Element? overlay = Props switch
        {
            { Incoming: { } incoming, Node: { } node } =>
                Component<IncomingTransferOverlay, IncomingTransferOverlayProps>(new(
                        node,
                        incoming,
                        Props.Settings.DownloadDirectory,
                        Props.Settings.SaveReceiveHistory,
                        Props.Settings.VerifyChecksumsOnReceive,
                        Props.Theme,
                        Props.DismissIncoming))
                    .WithKey(incoming.RequestId.ToString("N")),

            { Outgoing: { } outgoing } =>
                Component<OutgoingTransferOverlay, OutgoingTransferOverlayProps>(new(
                        outgoing,
                        Props.Theme,
                        Props.CloseOutgoing))
                    .WithKey(outgoing.Receiver.Fingerprint),

            _ => null,
        };

        return Border(overlay)
            .IsHitTestVisible(overlay is not null);
    }
}
