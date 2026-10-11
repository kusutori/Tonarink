using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Tonarink.Cli;

namespace Tonarink.Hooks.Shell;

sealed record ShellActivationState(
    ShareTargetPayload? ShareTargetPayload,
    Action<Guid> ConsumeShareTargetPayload,
    string? NavigationFavoriteFingerprint,
    Action<string> ConsumeNavigationFavorite,
    Guid? NavigationHistoryId,
    Action<Guid> ConsumeNavigationHistory);

static class ShellActivationHooks
{
    public static ShellActivationState UseShellActivations(
        this RenderContext context,
        NavigationHandle<AppRoute> navigation,
        LocalSendNodeSession nodeSession,
        Action restoreWindow,
        Func<CancellationToken, Task> prepareNavigation,
        bool canPresentDialogs)
    {
        var (shareTargetPayload, setShareTargetPayload) = context.UseState<ShareTargetPayload?>(null);
        var (navigationFavoriteFingerprint, setNavigationFavoriteFingerprint) = context.UseState<string?>(null);
        var (navigationHistoryId, setNavigationHistoryId) = context.UseState<Guid?>(null);
        var drainingActivations = context.UseRef(false);
        var sessionRef = context.UseRef(nodeSession);
        sessionRef.Current = nodeSession;
        var navigationHandler = context.UseRef<Func<AppNavigationRequest, CancellationToken, Task>>(HandleNavigationAsync);
        navigationHandler.Current = HandleNavigationAsync;

        context.UseEffect(() => canPresentDialogs
            ? AppNavigationService.Connect((request, token) => navigationHandler.Current(request, token))
            : () => { }, canPresentDialogs);

        context.UseEffect(() =>
        {
            EventHandler activationReceived = (_, _) => ScheduleActivationDrain();
            EventHandler notificationActivated = (_, _) => ScheduleActivationDrain();
            ShareTargetActivationBroker.ActivationReceived += activationReceived;
            AppNotificationService.Activated += notificationActivated;
            AppCommandActivation.Queued += activationReceived;
            ScheduleActivationDrain();
            return () =>
            {
                ShareTargetActivationBroker.ActivationReceived -= activationReceived;
                AppNotificationService.Activated -= notificationActivated;
                AppCommandActivation.Queued -= activationReceived;
            };
        });

        return new(
            shareTargetPayload,
            ConsumeShareTargetPayload,
            navigationFavoriteFingerprint,
            ConsumeNavigationFavorite,
            navigationHistoryId,
            ConsumeNavigationHistory);

        async Task HandleNavigationAsync(AppNavigationRequest request, CancellationToken token)
        {
            // This handler starts on the UI dispatcher. Validate before closing
            // an existing dialog, so a stale link cannot disrupt an editor.
            var fingerprint = request.Favorite is null ? null : FavoriteDeviceStore.Entries.Keys
                .FirstOrDefault(key => key.Equals(request.Favorite, StringComparison.OrdinalIgnoreCase))
                ?? throw new CliException(CliText.Get("Favorite fingerprint not found."), 3);
            if (request.History is { } id && !ReceiveHistoryStore.Entries.Any(entry => entry.Id == id))
                throw new CliException(CliText.Get("History entry not found."), 3);
            if (request.Route is not null)
                await prepareNavigation(token);

            // Dialog teardown can resume off-dispatcher. Re-enter the live UI
            // before changing hook state or touching the navigation host.
            await AppNavigationService.OnUIAsync(() =>
            {
                if (request.Route is { } route)
                {
                    setNavigationFavoriteFingerprint(fingerprint);
                    setNavigationHistoryId(request.History);
                    if (navigation.CurrentRoute != route)
                    {
                        if (AppNavigation.IsDetail(route)) navigation.Navigate(route, AppNavigation.DrillIn);
                        else navigation.Navigate(route);
                    }
                }
                restoreWindow();
                return Task.CompletedTask;
            }, token);
        }

        void ConsumeShareTargetPayload(Guid payloadId)
        {
            if (shareTargetPayload?.Id == payloadId)
                setShareTargetPayload(null);
        }

        void ConsumeNavigationFavorite(string fingerprint)
        {
            if (string.Equals(navigationFavoriteFingerprint, fingerprint, StringComparison.Ordinal))
                setNavigationFavoriteFingerprint(null);
        }

        void ConsumeNavigationHistory(Guid historyId)
        {
            if (navigationHistoryId == historyId)
                setNavigationHistoryId(null);
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
                while (AppCommandActivation.TryDequeue(out var invocation))
                    _ = invocation!();

                while (AppNotificationService.TryDequeueActivation(out var activation))
                    _ = HandleNotificationActivationAsync(activation);

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
                    || AppCommandActivation.HasPending
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
