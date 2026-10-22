using LocalSendDotNet;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using static Tonarink.Components.Transfers.TransferOverlayVisuals;
using static Tonarink.Pages.Send.SendTransferPresentation;

namespace Tonarink.Pages.Send;

sealed record SendTransferController(
    bool IsSending,
    Func<LocalSendDevice, string?, string?, Task> StartAsync,
    Func<LocalSendDevice, string?, string?, IReadOnlyList<SelectedSendItem>, Task> StartItemsAsync,
    Func<bool> IsBusy);

static class SendTransferHooks
{
    public static SendTransferController UseSendTransfer(
        this RenderContext context,
        LocalSendNode? node,
        AppRuntimeState runtime,
        IReadOnlyList<SelectedSendItem> selectedItems,
        Action<Func<IReadOnlyList<SelectedSendItem>, IReadOnlyList<SelectedSendItem>>> updateSelectedItems,
        bool keepItemsForMultipleReceivers,
        bool verifyChecksums,
        Action<OutgoingTransferViewState?> setTransferOverlay,
        Action<string> setPickerMessage,
        Action<string?> setRecentManualAddress,
        IntlAccessor t)
    {
        var (isSending, setIsSending) = context.UseState(false);
        var sendCancellationRef = context.UseRef<CancellationTokenSource?>();
        var busy = context.UseRef(false);
        var overlay = context.UseRef<OutgoingTransferViewState?>();
        var liveItems = context.UseRef(selectedItems);
        liveItems.Current = selectedItems;

        return new(isSending, (device, pin, address) => StartItemsAsync(device, pin, address, selectedItems),
            StartItemsAsync, () => busy.Current);

        async Task<OutgoingTransferResult> SendAsync(SendRequest request)
        {
            setIsSending(true);
            try
            {
                var progressNotification = AppNotificationService.StartTransferProgress(
                    request.TransferId,
                    t.Message(new("App", "NotificationSendProgressTitle"), ("device", request.Device.Alias)),
                    ContentSummary(t, request.Items),
                    t.Message(new("App", "SendingTransferring")),
                    0,
                    request.TotalBytes,
                    TransferProgressText(0, request.TotalBytes),
                    "send-progress");
                var progress = new Progress<TransferProgress>(value =>
                {
                    var next = new SendTransferStatus(
                        value.State,
                        request.Device.Alias,
                        value.BytesTransferred,
                        value.TotalBytes,
                        ProgressMessage(t, value.State, request.Device.Alias),
                        IsError: false);
                    PublishTransferOverlay(
                        request.Device,
                        request.Items,
                        next,
                        new OutgoingOverlayUpdate.Pending());
                    progressNotification?.Report(
                        ContentSummary(t, request.Items),
                        next.Message,
                        value.BytesTransferred,
                        value.TotalBytes,
                        TransferProgressText(value.BytesTransferred, value.TotalBytes));
                });

                try
                {
                    var outcome = await node!.SendAsync(
                            request.Device,
                            request.Items,
                            new SendOptions
                            {
                                Pin = request.Pin,
                                ComputeSha256 = verifyChecksums,
                            },
                            progress,
                            request.CancellationToken)
                        .ConfigureAwait(true);
                    return CoreTransferOutcomeMapper.Map(outcome);
                }
                finally
                {
                    if (progressNotification is not null)
                        await progressNotification.RemoveAsync().ConfigureAwait(true);
                }
            }
            finally
            {
                setIsSending(false);
            }
        }

        async Task StartItemsAsync(
            LocalSendDevice device,
            string? pin,
            string? resolvedManualAddress,
            IReadOnlyList<SelectedSendItem> batch)
        {
            if (node?.State != LocalSendNodeState.Running || batch.Count == 0 || busy.Current)
                return;

            busy.Current = true;
            var awaitingPin = false;
            var items = (SendItem[])[.. batch.Select(static item => item.Item)];
            var totalBytes = batch.Sum(static item => item.Length);
            var cancellation = new CancellationTokenSource();
            sendCancellationRef.Current?.Dispose();
            sendCancellationRef.Current = cancellation;
            var startingState = new SendTransferStatus(
                TransferState.Preparing,
                device.Alias,
                0,
                totalBytes,
                t.Message(new("App", "PreparingForDevice"), ("device", device.Alias)),
                IsError: false);
            PublishTransferOverlay(
                device,
                items,
                startingState,
                new OutgoingOverlayUpdate.Pending());

            try
            {
                var result = await SendAsync(new(
                    Guid.NewGuid(),
                    device,
                    items,
                    totalBytes,
                    pin,
                    cancellation.Token));
                if (result is OutgoingTransferResult.PinRequired pinRequired)
                {
                    var waitingState = new SendTransferStatus(
                        TransferState.WaitingForAcceptance,
                        device.Alias,
                        0,
                        totalBytes,
                        t.Message(new("App", "TargetRequiresPin")),
                        pinRequired.InvalidPin);
                    PublishTransferOverlay(
                        device,
                        items,
                        waitingState,
                        new OutgoingOverlayUpdate.AwaitingPin(new OutgoingPinPrompt(
                            pinRequired.InvalidPin ? t.Message(new("App", "PinIncorrect")) : null,
                            enteredPin =>
                            {
                                busy.Current = false;
                                _ = StartItemsAsync(device, enteredPin, resolvedManualAddress, batch);
                            },
                            () => { busy.Current = false; })));
                    awaitingPin = true;
                    return;
                }

                if (result is OutgoingTransferResult.RateLimited)
                {
                    var errorState = new SendTransferStatus(
                        TransferState.Failed,
                        device.Alias,
                        0,
                        totalBytes,
                        t.Message(new("App", "RateLimited")),
                        IsError: true);
                    PublishTransferOverlay(
                        device,
                        items,
                        errorState,
                        new OutgoingOverlayUpdate.Finished());
                    return;
                }

                var resultState = ResultState(t, result, device.Alias, totalBytes);
                PublishTransferOverlay(
                    device,
                    items,
                    resultState,
                    new OutgoingOverlayUpdate.Finished());
                if (result is not OutgoingTransferResult.Completed)
                    return;

                if (resolvedManualAddress is not null)
                {
                    RecentManualAddressStore.Save(resolvedManualAddress);
                    setRecentManualAddress(resolvedManualAddress);
                }

                AppNotificationService.ShowTransferComplete(
                    t.Message(new("App", "NotificationSendCompleteTitle")),
                    t.Message(
                        new("App", "NotificationSendComplete"),
                        ("count", batch.Count),
                        ("device", device.Alias)),
                    "send-complete",
                    batch.Where(static item => !string.IsNullOrWhiteSpace(item.LocalPath))
                        .Select(static item => item.LocalPath!),
                    AppSettingsStore.Load().NotificationDefaultAction,
                    t.Message(new("App", "NotificationOpenFile")),
                    t.Message(new("App", "NotificationShowInFolder")));
                if (!keepItemsForMultipleReceivers)
                {
                    var sentIds = batch.Select(item => item.Id).ToHashSet();
                    var remaining = liveItems.Current.Count(item => !sentIds.Contains(item.Id));
                    updateSelectedItems(current => current.Where(item => !sentIds.Contains(item.Id)).ToArray());
                    setPickerMessage(t.Message(new("App", remaining == 0 ? "NothingSelected" : "SendHint")));
                }
            }
            catch (Exception exception)
            {
                var errorState = new SendTransferStatus(
                    TransferState.Failed,
                    device.Alias,
                    0,
                    totalBytes,
                    exception.Message,
                    IsError: true);
                PublishTransferOverlay(
                    device,
                    items,
                    errorState,
                    new OutgoingOverlayUpdate.Finished());
            }
            finally
            {
                if (!awaitingPin) busy.Current = false;
                if (ReferenceEquals(sendCancellationRef.Current, cancellation))
                    sendCancellationRef.Current = null;
                cancellation.Dispose();
            }
        }

        void PublishTransferOverlay(
            LocalSendDevice device,
            IReadOnlyList<SendItem> items,
            SendTransferStatus state,
            OutgoingOverlayUpdate update)
        {
            var snapshot = new OutgoingTransferSnapshot(
                runtime.Identity,
                device,
                ContentSummary(t, items),
                state.State,
                state.BytesTransferred,
                state.TotalBytes,
                state.Message,
                () =>
                {
                    sendCancellationRef.Current?.Cancel();
                    if (overlay.Current is OutgoingTransferViewState.Pending pending)
                    {
                        overlay.Current = pending with
                        {
                            Transfer = pending.Transfer with { Status = t.Message(new("App", "CancellingTransfer")) },
                        };
                        setTransferOverlay(overlay.Current);
                    }
                });
            overlay.Current = update switch
            {
                OutgoingOverlayUpdate.Pending => new OutgoingTransferViewState.Pending(snapshot),
                OutgoingOverlayUpdate.AwaitingPin(var prompt) =>
                    new OutgoingTransferViewState.AwaitingPin(snapshot, prompt),
                OutgoingOverlayUpdate.Finished =>
                    new OutgoingTransferViewState.Finished(snapshot, state.IsError),
            };
            setTransferOverlay(overlay.Current);
        }
    }
}
