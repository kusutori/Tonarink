using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Components.Devices.DeviceVisuals;
using static Tonarink.Components.Transfers.TransferOverlayVisuals;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Components.Transfers;

sealed record OutgoingTransferOverlayProps(
    OutgoingTransferViewState Transfer,
    Action Close,
    bool IsPresented = true);

sealed class OutgoingTransferOverlay : Component<OutgoingTransferOverlayProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var transfer = Props.Transfer;
        var (showVerification, setShowVerification) = UseState(false);
        var (connectedAnimationReady, setConnectedAnimationReady) = UseState(false);
        var (isActive, setActive) = UseState(true);
        var isPresented = isActive && Props.IsPresented;
        var (pin, setPin) = UseState(string.Empty);
        var focusTrap = this.UseFocusTrap(
            isPresented && !showVerification && !(connectedAnimationReady && transfer.PinPrompt is not null));
        var (cancelRef, requestCancelFocus) = this.UseElementFocus();
        var (closeRef, requestCloseFocus) = this.UseElementFocus();
        UseNavigationLifecycle(
            onNavigatingTo: _ =>
            {
                setActive(true);
                setConnectedAnimationReady(false);
            },
            onNavigatedTo: _ =>
            {
                setConnectedAnimationReady(true);
                if (!Props.IsPresented) return;
                if (transfer.IsPending) requestCancelFocus();
                else requestCloseFocus();
            },
            onNavigatedFrom: _ =>
            {
                setActive(false);
                setConnectedAnimationReady(false);
                setShowVerification(false);
                if (Props.IsPresented) ClearTaskbarProgress(window);
            });
        var taskbarProgress = new TaskbarTransferProgress(
            transfer.State,
            transfer.BytesTransferred,
            transfer.TotalBytes,
            transfer.Status);

        UseEffect(() =>
        {
            if (isPresented) UpdateTaskbarProgress(window, taskbarProgress);
        }, taskbarProgress, isPresented);
        UseEffect(() => () =>
        {
            if (Props.IsPresented) ClearTaskbarProgress(window);
        });

        var progress = transfer.TotalBytes <= 0
            ? 0
            : Math.Clamp(transfer.BytesTransferred * 100d / transfer.TotalBytes, 0, 100);
        var progressText = $"{FormatBytes(transfer.BytesTransferred)} / {FormatBytes(transfer.TotalBytes)}";

        var devices = VStack(20,
                Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                        transfer.Sender?.Alias ?? t.Message(new("App", "ThisDevice")),
                        transfer.Sender?.DeviceModel,
                        transfer.Sender?.DeviceType ?? LocalSendDeviceType.Desktop,
                        LocalDeviceNumber(transfer.Sender)))
                    .Transition(Transition.Enter(Transition.Slide(Edge.Top))),
                Icon(FontIcon(AppIcons.SendFile, fontSize: 28)).AccessibilityHidden()
                    .HAlign(HorizontalAlignment.Center),
                Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                    transfer.Receiver.Alias,
                    transfer.Receiver.DeviceModel,
                    transfer.Receiver.DeviceType,
                    RemoteDeviceNumber(transfer.Receiver),
                    NavigationAnimationKey: DeviceDetailsAnimationKey(transfer.Receiver.Fingerprint))),
                VerificationButton(t, () => setShowVerification(true))
                    .HAlign(HorizontalAlignment.Center))
            .MaxWidth(AppLayout.OverlayDevicesMaxWidth)
            .HAlign(HorizontalAlignment.Stretch);

        var status = VStack(12,
                BodyLarge(OutgoingStatus(t, transfer.State))
                    .Foreground(transfer.IsError ? Theme.SystemCritical : Theme.PrimaryText)
                    .LiveRegion()
                    .HAlign(HorizontalAlignment.Center),
                TextBlock(transfer.Status)
                    .Foreground(transfer.IsError ? Theme.SystemCritical : Theme.SecondaryText)
                    .TextWrapping(TextWrapping.WrapWholeWords)
                    .TextAlignment(TextAlignment.Center)
                    .HAlign(HorizontalAlignment.Center),
                transfer.State is TransferState.Preparing or TransferState.WaitingForAcceptance
                    ? ProgressIndeterminate().MaxWidth(AppLayout.OverlayProgressMaxWidth)
                        .AutomationName(OutgoingStatus(t, transfer.State))
                        .HAlign(HorizontalAlignment.Stretch)
                    : transfer.TotalBytes > 0
                        ? Progress(progress).MaxWidth(AppLayout.OverlayProgressMaxWidth)
                            .AutomationName(OutgoingStatus(t, transfer.State))
                            .HAlign(HorizontalAlignment.Stretch)
                        : null,
                transfer.TotalBytes > 0
                    ? Caption(progressText)
                        .Foreground(Theme.SecondaryText)
                        .HAlign(HorizontalAlignment.Center)
                    : null,
                (transfer.IsPending
                    ? Button(
                            HStack(8,
                                Icon(AppIcons.Cancel).AccessibilityHidden(),
                                TextBlock(t.Message(new("App", "Cancel")))),
                            transfer.Cancel)
                        .AutomationName(t.Message(new("App", "CancelCurrentSend")))
                        .Ref(cancelRef)
                        .OnMountAdd(_ => requestCancelFocus())
                        .MinWidth(120)
                    : Button(
                            HStack(8,
                                Icon(AppIcons.Cancel).AccessibilityHidden(),
                                TextBlock(t.Message(new("App", "Close")))),
                            CloseOverlay)
                        .AutomationName(t.Message(new("App", "Close")))
                        .Ref(closeRef)
                        .OnMountAdd(_ => requestCloseFocus())
                        .MinWidth(120))
                .HAlign(HorizontalAlignment.Center))
            .MaxWidth(AppLayout.OverlayStatusMaxWidth)
            .HAlign(HorizontalAlignment.Stretch);

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star(), GridSize.Auto],
                ScrollView(
                        VStack(28,
                            Heading(t.Message(new("App", "SendingTitle")))
                                .HeadingLevel(AutomationHeadingLevel.Level1)
                                .HAlign(HorizontalAlignment.Center),
                            Caption(transfer.ContentSummary)
                                .Foreground(Theme.SecondaryText)
                                .HAlign(HorizontalAlignment.Center),
                            devices))
                    .Padding(horizontal: 40, vertical: 32)
                    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                    .Grid(row: 0),
                Border(status)
                    .Padding(horizontal: 40, vertical: 24)
                    .Grid(row: 1),
                Component<DeviceVerificationDialog, DeviceVerificationDialogProps>(new(
                    transfer.Receiver,
                    transfer.Sender?.Fingerprint,
                    isPresented && showVerification,
                    () => setShowVerification(false))),
                Component<TransferPinDialog, TransferPinDialogProps>(new(
                    isPresented && connectedAnimationReady && transfer.PinPrompt is not null,
                    transfer.Receiver.Alias,
                    pin,
                    setPin,
                    transfer.PinPrompt?.Error,
                    ClosePinDialog)))
            .FocusTrap(focusTrap)
            .AutomationName(t.Message(new("App", "SendingTitle")))
            .Landmark(AutomationLandmarkType.Main);

        void ClosePinDialog(ContentDialogResult result)
        {
            var submittedPin = pin.Trim();
            setPin(string.Empty);
            var prompt = transfer.PinPrompt;
            if (prompt is null)
                return;

            if (result == ContentDialogResult.Primary && submittedPin.Length > 0)
                prompt.Submit(submittedPin);
            else
            {
                prompt.Cancel();
                CloseOverlay();
            }
        }

        void CloseOverlay()
        {
            Props.Close();
        }
    }

    private static string OutgoingStatus(IntlAccessor t, TransferState state) => state switch
    {
        TransferState.Preparing => t.Message(new("App", "SendingPreparing")),
        TransferState.WaitingForAcceptance => t.Message(new("App", "SendingWaiting")),
        TransferState.Transferring => t.Message(new("App", "SendingTransferring")),
        TransferState.Completed => t.Message(new("App", "TransferComplete")),
        TransferState.Cancelled => t.Message(new("App", "SendCancelled")),
        TransferState.Failed => t.Message(new("App", "SendFailed")),
    };
}
