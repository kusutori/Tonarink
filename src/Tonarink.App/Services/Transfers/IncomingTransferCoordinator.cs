using LocalSendDotNet;
using Microsoft.UI.Reactor.Localization;

namespace Tonarink.Services.Transfers;

sealed class IncomingTransferCoordinator(
    Func<IntlAccessor> getIntl,
    Func<LocalSendNode?> getNode,
    Func<AppRuntimeState> getRuntime,
    Action<AppRuntimeAction> dispatchRuntime)
{
    public void Dismiss(Guid requestId) =>
        dispatchRuntime(new AppRuntimeAction.IncomingDismissed(requestId));

    public async Task WatchAsync(LocalSendNode node, CancellationToken cancellationToken)
    {
        await foreach (var request in node.WatchIncomingTransfersAsync(cancellationToken).ConfigureAwait(false))
        {
            var t = getIntl();
            var settings = AppSettingsStore.Load();
            var autoAccept = settings.AutoSave switch
            {
                AutoSaveMode.On => true,
                AutoSaveMode.Favorites => FavoriteDeviceStore.Contains(request.Sender.Fingerprint),
                _ => false,
            };
            if (autoAccept)
            {
                _ = AcceptAsync(
                    node,
                    request,
                    settings.DownloadDirectory,
                    settings.VerifyChecksumsOnReceive,
                    cancellationToken);
                continue;
            }

            dispatchRuntime(new AppRuntimeAction.IncomingAdded(request));
            AppNotificationService.ShowIncomingRequest(
                t.Message(new("App", "NotificationIncomingTitle"), ("device", request.Sender.Alias)),
                TransferOverlayVisuals.IncomingSummary(t, request.Items),
                request.RequestId,
                t.Message(new("App", "Accept")),
                t.Message(new("App", "Decline")));
        }
    }

    public async Task<bool> HandleActivationAsync(string action, Guid requestId)
    {
        if (getNode() is not { } node
            || getRuntime().IncomingTransfers.FirstOrDefault(request => request.RequestId == requestId) is not
            { } request)
            return false;

        Dismiss(requestId);
        if (action == "incoming-accept")
        {
            var settings = AppSettingsStore.Load();
            await AcceptAsync(
                node,
                request,
                settings.DownloadDirectory,
                settings.VerifyChecksumsOnReceive,
                CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await node.DeclineAsync(requestId).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                dispatchRuntime(new AppRuntimeAction.ErrorReported(exception.Message));
            }
        }

        return true;
    }

    private async Task AcceptAsync(
        LocalSendNode node,
        IncomingTransferRequest request,
        string downloadDirectory,
        bool verifyChecksums,
        CancellationToken cancellationToken)
    {
        var t = getIntl();
        var totalBytes = request.Items.Sum(static item => item.Size);
        var progressTitle = TransferOverlayVisuals.IncomingSummary(t, request.Items);
        var progressNotification = AppNotificationService.StartTransferProgress(
            request.RequestId,
            t.Message(new("App", "NotificationReceiveProgressTitle"), ("device", request.Sender.Alias)),
            progressTitle,
            t.Message(new("App", "ReceivingContent")),
            0,
            totalBytes,
            ProgressText(0, totalBytes),
            "receive-progress");
        try
        {
            var progress = new Progress<TransferProgress>(value =>
                progressNotification?.Report(
                    progressTitle,
                    t.Message(new("App", "ReceivingContent")),
                    value.BytesTransferred,
                    value.TotalBytes,
                    ProgressText(value.BytesTransferred, value.TotalBytes)));
            var result = CoreTransferOutcomeMapper.Map(await node.AcceptAsync(
                    request.RequestId,
                    new AcceptTransferOptions
                    {
                        DestinationDirectory = downloadDirectory,
                        VerifySha256 = verifyChecksums,
                    },
                    progress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false));
            if (result is not IncomingTransferResult.Completed completed)
            {
                var message = result switch
                {
                    IncomingTransferResult.Cancelled => t.Message(new("App", "ReceiveCancelled")),
                    IncomingTransferResult.Failed failed => failed.Failure.Message,
                    _ => t.Message(new("App", "ReceiveFailed")),
                };
                AppNotificationService.Show(t.Message(new("App", "ReceiveFailed")), message, "receive-failed");
                dispatchRuntime(new AppRuntimeAction.ErrorReported(message));
                return;
            }

            if (AppSettingsStore.Load().SaveReceiveHistory)
                ReceiveHistoryStore.Record(request.Sender.Alias, completed);
            AppNotificationService.ShowTransferComplete(
                t.Message(new("App", "NotificationReceiveCompleteTitle")),
                request.Items.Count == 1
                    ? t.Message(new("App", "NotificationReceiveCompleteOne"), ("device", request.Sender.Alias))
                    : t.Message(
                        new("App", "NotificationReceiveCompleteMany"),
                        ("count", request.Items.Count),
                        ("device", request.Sender.Alias)),
                "receive-complete",
                completed.Items.Select(static item => item.SavedPath ?? string.Empty),
                AppSettingsStore.Load().NotificationDefaultAction,
                t.Message(new("App", "NotificationOpenFile")),
                t.Message(new("App", "NotificationShowInFolder")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when automatic receiving is cancelled with the node session.
        }
        catch (Exception exception)
        {
            AppNotificationService.Show(
                t.Message(new("App", "ReceiveFailed")),
                exception.Message,
                "receive-failed");
            dispatchRuntime(new AppRuntimeAction.ErrorReported(exception.Message));
        }
        finally
        {
            if (progressNotification is not null)
                await progressNotification.RemoveAsync().ConfigureAwait(false);
        }
    }

    private static string ProgressText(long bytesTransferred, long totalBytes) => totalBytes > 0
        ? $"{Utilities.ByteSize.FormatBytes(bytesTransferred)} / {Utilities.ByteSize.FormatBytes(totalBytes)}"
        : Utilities.ByteSize.FormatBytes(bytesTransferred);
}
