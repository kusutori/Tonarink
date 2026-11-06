using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Components.Devices.DeviceVisuals;

namespace Tonarink.Components.Shell;

sealed record OutgoingOverlayNavigatorProps(
    Element MainContent,
    OutgoingTransferViewState? Transfer,
    Action Close,
    bool IsPresented);

sealed class OutgoingOverlayNavigator : Component<OutgoingOverlayNavigatorProps>
{
    private enum Scene { Main, Transfer }

    public override Element Render()
    {
        var navigation = UseNavigation(Scene.Main);
        var lastTransfer = UseRef<OutgoingTransferViewState?>();
        if (Props.Transfer is { } transfer)
            lastTransfer.Current = transfer;

        UseEffect(() =>
        {
            if (Props.Transfer is { } current && navigation.CurrentRoute == Scene.Main)
            {
                navigation.Navigate(Scene.Transfer, new NavigateOptions
                {
                    Transition = NavigationTransition.Fade(),
                    ConnectedAnimationKey = DeviceDetailsAnimationKey(current.Receiver.Fingerprint),
                });
            }
            else if (Props.Transfer is null && navigation.CurrentRoute == Scene.Transfer)
                navigation.GoBack();
        }, Props.Transfer is null, Props.Transfer?.Receiver.Fingerprint);

        return NavigationHost(navigation, scene => scene switch
        {
            Scene.Main => Props.MainContent,
            // Retain the final snapshot until GoBack has captured the outgoing card.
            Scene.Transfer when lastTransfer.Current is { } current =>
                Component<OutgoingTransferOverlay, OutgoingTransferOverlayProps>(new(
                    current, Props.Close, Props.IsPresented)).WithKey(current.Receiver.Fingerprint),
            Scene.Transfer => Empty(),
            _ => throw new ArgumentOutOfRangeException(nameof(scene)),
        }) with
        {
            Transition = NavigationTransition.None,
            CacheMode = NavigationCacheMode.Enabled,
            CacheSize = 1,
        };
    }
}
