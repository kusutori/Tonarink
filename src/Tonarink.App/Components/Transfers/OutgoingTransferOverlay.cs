using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using BasicConnectedAnimationConfiguration = Microsoft.UI.Xaml.Media.Animation.BasicConnectedAnimationConfiguration;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Utilities.ByteSize;
using static Tonarink.Components.Devices.DeviceVisuals;
using static Tonarink.Components.Transfers.TransferOverlayVisuals;

namespace Tonarink.Components.Transfers;

sealed record OutgoingTransferOverlayProps(
    OutgoingTransferViewState Transfer,
    ElementTheme Theme,
    Action Close);

sealed class OutgoingTransferOverlay : Component<OutgoingTransferOverlayProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var reduceMotion = UseReducedMotion();
        var transfer = Props.Transfer;
        var (showVerification, setShowVerification) = UseState(false);
        var (connectedAnimationReady, setConnectedAnimationReady) = UseState(false);
        var (pin, setPin) = UseState(string.Empty);
        var focusTrap = this.UseFocusTrap(
            !showVerification && !(connectedAnimationReady && transfer.PinPrompt is not null));
        var receiverCardRef = UseRef<FrameworkElement?>();
        var connectedAnimationKey = DeviceConnectedKey(transfer.Receiver.Fingerprint);
        var taskbarProgress = new TaskbarTransferProgress(
            transfer.State,
            transfer.BytesTransferred,
            transfer.TotalBytes,
            transfer.Status);

        UseEffect(() => UpdateTaskbarProgress(window, taskbarProgress), taskbarProgress);
        UseEffect(() => () => ClearTaskbarProgress(window));

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
                Icon(FontIcon("\uE74B", fontSize: 28)).AccessibilityHidden()
                    .HAlign(HorizontalAlignment.Center),
                Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                    transfer.Receiver.Alias,
                    transfer.Receiver.DeviceModel,
                    transfer.Receiver.DeviceType,
                    RemoteDeviceNumber(transfer.Receiver),
                    connectedAnimationKey,
                    AnimationRole: DeviceIdentityCardAnimationRole.Destination,
                    AnimationCompleted: _ => setConnectedAnimationReady(true),
                    ElementChanged: element => receiverCardRef.Current = element)),
                VerificationButton(t, () => setShowVerification(true))
                    .HAlign(HorizontalAlignment.Center))
            .MaxWidth(AppLayout.OverlayDevicesMaxWidth)
            .HAlign(HorizontalAlignment.Stretch);

        var status = VStack(12,
                BodyLarge(OutgoingStatus(t, transfer.State))
                    .Foreground(transfer.IsError ? Theme.SystemCritical : Theme.PrimaryText)
                    .LiveRegion(AutomationLiveSetting.Polite)
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
                                Icon("\uE711").AccessibilityHidden(),
                                TextBlock(t.Message(new("App", "Cancel")))),
                            transfer.Cancel)
                        .AutomationName(t.Message(new("App", "CancelCurrentSend")))
                        .OnMountAdd(element => element.Focus(FocusState.Programmatic))
                        .MinWidth(120)
                    : Button(
                            HStack(8,
                                Icon("\uE711").AccessibilityHidden(),
                                TextBlock(t.Message(new("App", "Close")))),
                            CloseOverlay)
                        .AutomationName(t.Message(new("App", "Close")))
                        .OnMountAdd(element => element.Focus(FocusState.Programmatic))
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
                    Props.Theme,
                    showVerification,
                    () => setShowVerification(false))),
                PinDialog())
            .Transition(new FadeTransition())
            .FocusTrap(focusTrap)
            .AutomationName(t.Message(new("App", "SendingTitle")))
            .Landmark(AutomationLandmarkType.Main);

        Element PinDialog()
        {
            var prompt = transfer.PinPrompt;
            return (ContentDialog(
                    t.Message(new("App", "PinRequiredTitle")),
                    VStack(8,
                        TextBlock(t.Message(
                                new("App", "PinRequiredMessage"),
                                ("device", transfer.Receiver.Alias)))
                            .TextWrapping(TextWrapping.WrapWholeWords),
                        PasswordBox(pin, setPin, placeholderText: t.Message(new("App", "PinPlaceholder")))
                            .Header(t.Message(new("App", "Pin")))
                            .AutomationName(t.Message(new("App", "Pin")))
                            .Required()
                            .MaxLength(32),
                        prompt?.Error is null
                            ? null
                            : TextBlock(prompt.Error)
                                .Foreground(Theme.SystemCritical)
                                .LiveRegion(AutomationLiveSetting.Assertive)),
                    primaryButtonText: t.Message(new("App", "PinConfirm"))) with
            {
                IsOpen = connectedAnimationReady && prompt is not null,
                SecondaryButtonText = t.Message(new("App", "Cancel")),
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(pin),
                OnClosed = result =>
                {
                    var submittedPin = pin.Trim();
                    setPin(string.Empty);
                    if (prompt is null)
                        return;

                    if (result == ContentDialogResult.Primary && submittedPin.Length > 0)
                        prompt.Submit(submittedPin);
                    else
                    {
                        prompt.Cancel();
                        CloseOverlay();
                    }
                },
            }).Themed(Props.Theme);
        }

        void CloseOverlay()
        {
            if (!reduceMotion && receiverCardRef.Current is { } receiverCard)
            {
                DeviceConnectedAnimation.ReturnToSource(
                    connectedAnimationKey,
                    receiverCard,
                    Props.Close);
                return;
            }

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
        _ => t.Message(new("App", "SendFailed")),
    };
}
