// This file supplies partial hook members for LocalizedAppShell in the root namespace.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Navigation;

// ReSharper disable once CheckNamespace
namespace Tonarink;

sealed record ShellActivationState(
    ShareTargetPayload? ShareTargetPayload,
    Action<Guid> ConsumeShareTargetPayload);

sealed partial class LocalizedAppShell
{
    private ShellActivationState UseShellActivations(
        NavigationHandle<AppRoute> navigation,
        LocalSendNodeSession nodeSession,
        Action restoreWindow)
    {
        var (shareTargetPayload, setShareTargetPayload) = UseState<ShareTargetPayload?>(null);
        var drainingActivations = UseRef(false);
        var sessionRef = UseRef(nodeSession);
        sessionRef.Current = nodeSession;

        UseEffect(() =>
        {
            EventHandler activationReceived = (_, _) => ScheduleActivationDrain();
            EventHandler notificationActivated = (_, _) => ScheduleActivationDrain();
            ShareTargetActivationBroker.ActivationReceived += activationReceived;
            AppNotificationService.Activated += notificationActivated;
            ScheduleActivationDrain();
            return () =>
            {
                ShareTargetActivationBroker.ActivationReceived -= activationReceived;
                AppNotificationService.Activated -= notificationActivated;
            };
        });

        return new(shareTargetPayload, ConsumeShareTargetPayload);

        void ConsumeShareTargetPayload(Guid payloadId)
        {
            if (shareTargetPayload?.Id == payloadId)
                setShareTargetPayload(null);
        }

        void ScheduleActivationDrain()
        {
            var dispatcher = ReactorApp.UIDispatcher;
            if (dispatcher is null)
                return;

            if (dispatcher.HasThreadAccess)
                DrainActivations();
            else
                dispatcher.TryEnqueue(DrainActivations);
        }

        void DrainActivations()
        {
            if (drainingActivations.Current)
                return;

            drainingActivations.Current = true;
            try
            {
                while (AppNotificationService.TryDequeueActivation(out var activation))
                {
                    if (activation is not null)
                        _ = HandleNotificationActivationAsync(activation);
                }

                while (ShareTargetActivationBroker.TryDequeue(out var payload))
                {
                    if (payload is null)
                        continue;

                    setShareTargetPayload(payload);
                    if (navigation.CurrentRoute != AppRoute.Send)
                        navigation.Navigate(AppRoute.Send);
                    restoreWindow();
                }
            }
            finally
            {
                drainingActivations.Current = false;
                if (ShareTargetActivationBroker.HasPendingActivations
                    || AppNotificationService.HasPendingActivations)
                    ScheduleActivationDrain();
            }
        }

        async Task HandleNotificationActivationAsync(AppNotificationActivation activation)
        {
            switch (activation.Action)
            {
                case "incoming-accept":
                case "incoming-decline":
                    if (activation.RequestId is not { } requestId
                        || !await sessionRef.Current.HandleIncomingActivationAsync(
                            activation.Action,
                            requestId).ConfigureAwait(false))
                        restoreWindow();
                    return;

                case "open-file":
                    ShellLauncher.Open(activation.Path);
                    return;

                case "show-in-folder":
                    ShellLauncher.Reveal(activation.Path);
                    return;

                default:
                    restoreWindow();
                    return;
            }
        }
    }
}
