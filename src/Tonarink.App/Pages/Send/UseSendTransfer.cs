using LocalSendDotNet;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using static Tonarink.Components.Transfers.TransferOverlayVisuals;
using static Tonarink.Pages.Send.SendTransferPresentation;

namespace Tonarink.Pages.Send;

sealed record SendTransferController(
    bool IsSending,
    Action Reset,
    Func<LocalSendDevice, string?, string?, Task> StartAsync);

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
        var (_, dispatchTransfer) = context.UseReducer<TransferUiState, SendTransferAction>(
            SendTransferReducer.Reduce,
            new TransferUiState.Idle(t.Message(new("App", "SendHint"))));
        var (isSending, setIsSending) = context.UseState(false);
        var sendCancellationRef = context.UseRef<CancellationTokenSource?>();

        return new(isSending, Reset, StartAsync);

        void Reset() => dispatchTransfer(new SendTransferAction.Reset(
            t.Message(new("App", "SendHint"))));

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
                    var next = new TransferUiState.Active(
                        value.State,
                        request.Device.Alias,
                        value.BytesTransferred,
                        value.TotalBytes,
                        ProgressMessage(t, value.State, request.Device.Alias),
                        IsError: false);
                    dispatchTransfer(new SendTransferAction.Progressed(next));
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

        async Task StartAsync(
            LocalSendDevice device,
            string? pin,
            string? resolvedManualAddress = null)
        {
            if (node?.State != LocalSendNodeState.Running || selectedItems.Count == 0)
                return;

            var items = (SendItem[])[.. selectedItems.Select(static item => item.Item)];
            var totalBytes = selectedItems.Sum(static item => item.Length);
            var cancellation = new CancellationTokenSource();
            sendCancellationRef.Current?.Dispose();
            sendCancellationRef.Current = cancellation;
            var startingState = new TransferUiState.Active(
                TransferState.Preparing,
                device.Alias,
                0,
                totalBytes,
                t.Message(new("App", "PreparingForDevice"), ("device", device.Alias)),
                IsError: false);
            dispatchTransfer(new SendTransferAction.Started(startingState));
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
                    var waitingState = new TransferUiState.Active(
                        TransferState.WaitingForAcceptance,
                        device.Alias,
                        0,
                        totalBytes,
                        t.Message(new("App", "TargetRequiresPin")),
                        pinRequired.InvalidPin);
                    dispatchTransfer(new SendTransferAction.PinRequested(waitingState));
                    PublishTransferOverlay(
                        device,
                        items,
                        waitingState,
                        new OutgoingOverlayUpdate.AwaitingPin(new OutgoingPinPrompt(
                            pinRequired.InvalidPin ? t.Message(new("App", "PinIncorrect")) : null,
                            enteredPin => _ = StartAsync(device, enteredPin, resolvedManualAddress),
                            Reset)));
                    return;
                }

                if (result is OutgoingTransferResult.PinRateLimited)
                {
                    var errorState = new TransferUiState.Active(
                        TransferState.Failed,
                        device.Alias,
                        0,
                        totalBytes,
                        t.Message(new("App", "PinRateLimited")),
                        IsError: true);
                    dispatchTransfer(new SendTransferAction.Failed(errorState));
                    PublishTransferOverlay(
                        device,
                        items,
                        errorState,
                        new OutgoingOverlayUpdate.Finished());
                    return;
                }

                var resultState = ResultState(t, result, device.Alias, totalBytes);
                dispatchTransfer(new SendTransferAction.Finished(resultState));
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
                    selectedItems.Count == 1
                        ? t.Message(
                            new("App", "NotificationSendCompleteOne"),
                            ("device", device.Alias))
                        : t.Message(
                            new("App", "NotificationSendCompleteMany"),
                            ("count", selectedItems.Count),
                            ("device", device.Alias)),
                    "send-complete",
                    items
                        .OfType<SendFileItem>()
                        .Select(static item => item.Path),
                    AppSettingsStore.Load().NotificationDefaultAction,
                    t.Message(new("App", "NotificationOpenFile")),
                    t.Message(new("App", "NotificationShowInFolder")));
                if (!keepItemsForMultipleReceivers)
                {
                    updateSelectedItems(_ => []);
                    setPickerMessage(t.Message(new("App", "NothingSelected")));
                }
            }
            catch (Exception exception)
            {
                var errorState = new TransferUiState.Active(
                    TransferState.Failed,
                    device.Alias,
                    0,
                    totalBytes,
                    exception.Message,
                    IsError: true);
                dispatchTransfer(new SendTransferAction.Failed(errorState));
                PublishTransferOverlay(
                    device,
                    items,
                    errorState,
                    new OutgoingOverlayUpdate.Finished());
            }
            finally
            {
                if (ReferenceEquals(sendCancellationRef.Current, cancellation))
                    sendCancellationRef.Current = null;
                cancellation.Dispose();
            }
        }

        void PublishTransferOverlay(
            LocalSendDevice device,
            IReadOnlyList<SendItem> items,
            TransferUiState state,
            OutgoingOverlayUpdate update)
        {
            var snapshot = new OutgoingTransferSnapshot(
                runtime.Identity,
                device,
                ContentSummary(t, items),
                state.State ?? TransferState.Preparing,
                state.BytesTransferred,
                state.TotalBytes,
                state.Message,
                () =>
                {
                    sendCancellationRef.Current?.Cancel();
                    dispatchTransfer(new SendTransferAction.MessageChanged(
                        t.Message(new("App", "CancellingTransfer"))));
                });
            setTransferOverlay(update switch
            {
                OutgoingOverlayUpdate.Pending => new OutgoingTransferViewState.Pending(snapshot),
                OutgoingOverlayUpdate.AwaitingPin(var prompt) =>
                    new OutgoingTransferViewState.AwaitingPin(snapshot, prompt),
                OutgoingOverlayUpdate.Finished =>
                    new OutgoingTransferViewState.Finished(snapshot, state.IsError),
            });
        }
    }
}
