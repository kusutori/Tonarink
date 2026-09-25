using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;

namespace Tonarink.Hooks;

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
                {
                    if (activation is not null)
                        _ = HandleNotificationActivationAsync(activation);
                }

                while (JumpListService.TryDequeue(out var jumpListActivation))
                {
                    switch (jumpListActivation)
                    {
                        case { Kind: JumpListActivationKind.Favorite, Value: var fingerprint }
                            when FavoriteDeviceStore.Contains(fingerprint):
                            setJumpListFavoriteFingerprint(fingerprint);
                            break;

                        case { Kind: JumpListActivationKind.History, Value: var historyIdText }
                            when Guid.TryParseExact(historyIdText, "N", out var historyId)
                                 && ReceiveHistoryStore.Entries.Any(entry => entry.Id == historyId):
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
