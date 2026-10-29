using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Shell;

sealed record TransferOverlayHostProps(
    LocalSendNode? Node,
    IncomingTransferRequest? Incoming,
    OutgoingTransferViewState? Outgoing,
    AppSettings Settings,
    ElementTheme Theme,
    Action<Guid> DismissIncoming,
    Action CloseOutgoing,
    bool AcceptIncomingText = false);

sealed class TransferOverlayHost : Component<TransferOverlayHostProps>
{
    public override Element Render()
    {
        var reduceMotion = UseReducedMotion();
        Element? overlay = Props switch
        {
            { Incoming: { } incoming, Node: { } node } =>
                Component<IncomingTransferOverlay, IncomingTransferOverlayProps>(new(
                        node,
                        incoming,
                        Props.Settings.DownloadDirectory,
                        Props.Settings.SaveReceiveHistory,
                        Props.Settings.VerifyChecksumsOnReceive,
                        Props.Settings.AutoReturnAfterReceive,
                        Props.Theme,
                        Props.DismissIncoming,
                        Props.AcceptIncomingText))
                    .WithKey(incoming.RequestId.ToString("N")),

            { Outgoing: { } outgoing } =>
                Component<OutgoingTransferOverlay, OutgoingTransferOverlayProps>(new(
                        outgoing,
                        Props.Theme,
                        Props.CloseOutgoing))
                    .WithKey(outgoing.Receiver.Fingerprint),

            _ => null,
        };

        // Exit belongs on the direct child of the panel: a decorator's content
        // replacement would otherwise remove the component immediately.
        if (Props.Incoming is not null && overlay is not null && !reduceMotion)
            overlay = overlay.Transition(Transition.Exit(new FadeTransition()));

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                overlay)
            .IsHitTestVisible(overlay is not null);
    }
}
