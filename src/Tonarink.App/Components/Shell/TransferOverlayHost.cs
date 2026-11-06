using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Shell;

sealed record TransferOverlayHostProps(
    LocalSendNode? Node,
    IncomingTransferRequest? Incoming,
    AppSettings Settings,
    Action<Guid> DismissIncoming,
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
                        Props.DismissIncoming,
                        Props.AcceptIncomingText))
                    .WithKey(incoming.RequestId.ToString("N")),

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
