using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;

namespace Tonarink.Hooks.Shell;

sealed record ShellActivationState(
    ShareTargetPayload? ShareTargetPayload,
    Action<Guid> ConsumeShareTargetPayload,
    string? JumpListFavoriteFingerprint,
    Action<string> ConsumeJumpListFavorite,
    Guid? JumpListHistoryId,
    Action<Guid> ConsumeJumpListHistory);

static class ShellActivationHooks
{
    public static ShellActivationState UseShellActivations(
        this RenderContext context,
        NavigationHandle<AppRoute> navigation,
        LocalSendNodeSession nodeSession,
        Action restoreWindow,
        bool canPresentDialogs)
    {
        var (shareTargetPayload, setShareTargetPayload) = context.UseState<ShareTargetPayload?>(null);
        var (jumpListFavoriteFingerprint, setJumpListFavoriteFingerprint) = context.UseState<string?>(null);
        var (jumpListHistoryId, setJumpListHistoryId) = context.UseState<Guid?>(null);
        var drainingActivations = context.UseRef(false);
        var sessionRef = context.UseRef(nodeSession);
        sessionRef.Current = nodeSession;

        context.UseEffect(() =>
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

        context.UseEffect(() =>
        {
            if (!canPresentDialogs || jumpListFavoriteFingerprint is null)
                return;

            if (navigation.CurrentRoute != AppRoute.Send)
                navigation.Navigate(AppRoute.Send);
            restoreWindow();
        }, canPresentDialogs, jumpListFavoriteFingerprint);

        context.UseEffect(() =>
        {
            if (!canPresentDialogs || jumpListHistoryId is null)
                return;

            if (navigation.CurrentRoute != AppRoute.History)
                navigation.Navigate(AppRoute.History, AppNavigation.DrillIn);
            restoreWindow();
        }, canPresentDialogs, jumpListHistoryId);

        return new(
            shareTargetPayload,
            ConsumeShareTargetPayload,
            jumpListFavoriteFingerprint,
            ConsumeJumpListFavorite,
            jumpListHistoryId,
            ConsumeJumpListHistory);

        void ConsumeShareTargetPayload(Guid payloadId)
        {
            if (shareTargetPayload?.Id == payloadId)
                setShareTargetPayload(null);
        }

        void ConsumeJumpListFavorite(string fingerprint)
        {
            if (string.Equals(jumpListFavoriteFingerprint, fingerprint, StringComparison.Ordinal))
                setJumpListFavoriteFingerprint(null);
        }

        void ConsumeJumpListHistory(Guid historyId)
        {
            if (jumpListHistoryId == historyId)
                setJumpListHistoryId(null);
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
                    _ = HandleNotificationActivationAsync(activation);

                while (JumpListService.TryDequeue(out var jumpListActivation))
                {
                    switch (jumpListActivation)
                    {
                        case JumpListActivation.Favorite(var fingerprint)
                            when FavoriteDeviceStore.Contains(fingerprint):
                            setJumpListFavoriteFingerprint(fingerprint);
                            break;

                        case JumpListActivation.History(var historyId)
                            when ReceiveHistoryStore.Entries.Any(entry => entry.Id == historyId):
                            setJumpListHistoryId(historyId);
                            break;

                        default:
                            restoreWindow();
                            break;
                    }
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
                    || JumpListService.HasPendingActivations
                    || AppNotificationService.HasPendingActivations)
                    ScheduleActivationDrain();
            }
        }

        async Task HandleNotificationActivationAsync(AppNotificationActivation activation)
        {
            switch (activation)
            {
                case AppNotificationActivation.IncomingAccept(var requestId):
                    if (!await sessionRef.Current.HandleIncomingActivationAsync(
                            "incoming-accept",
                            requestId).ConfigureAwait(false))
                        restoreWindow();
                    return;

                case AppNotificationActivation.IncomingDecline(var requestId):
                    if (!await sessionRef.Current.HandleIncomingActivationAsync(
                            "incoming-decline",
                            requestId).ConfigureAwait(false))
                        restoreWindow();
                    return;

                case AppNotificationActivation.OpenFile(var path):
                    ShellLauncher.Open(path);
                    return;

                case AppNotificationActivation.ShowInFolder(var path):
                    ShellLauncher.Reveal(path);
                    return;

                case AppNotificationActivation.Open:
                    restoreWindow();
                    return;
            }
        }
    }
}
