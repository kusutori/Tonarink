using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using static Microsoft.UI.Reactor.Factories;
using static ByteSize;
using static DeviceVisuals;
using static TransferOverlayVisuals;

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
        var transfer = Props.Transfer;
        var (showVerification, setShowVerification) = UseState(false);
        var (connectedAnimationReady, setConnectedAnimationReady) = UseState(false);
        var (pin, setPin) = UseState(string.Empty);
        var receiverCardRef = UseRef<FrameworkElement?>(null);
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
                    ? ProgressIndeterminate().MaxWidth(AppLayout.OverlayProgressMaxWidth).HAlign(HorizontalAlignment.Stretch)
                    : transfer.TotalBytes > 0
                        ? Progress(progress).MaxWidth(AppLayout.OverlayProgressMaxWidth).HAlign(HorizontalAlignment.Stretch)
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
                        .MinWidth(120)
                    : Button(
                            HStack(8,
                                Icon("\uE711").AccessibilityHidden(),
                                TextBlock(t.Message(new("App", "Close")))),
                            CloseOverlay)
                        .AutomationName(t.Message(new("App", "Close")))
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
                            .MaxLength(32),
                        prompt?.Error is null
                            ? null
                            : TextBlock(prompt.Error).Foreground(Theme.SystemCritical)),
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
            if (receiverCardRef.Current is { } receiverCard)
            {
                DeviceConnectedAnimation.ReturnToSource(
                    connectedAnimationKey,
                    receiverCard,
                    Props.Close);
            }
            else
            {
                Props.Close();
            }
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

sealed record IncomingTransferOverlayProps(
    LocalSendNode Node,
    IncomingTransferRequest Request,
    string DownloadDirectory,
    bool SaveReceiveHistory,
    bool VerifyChecksums,
    ElementTheme Theme,
    Action<Guid> Dismiss);

sealed record IncomingTransferViewState(
    TransferState State,
    long BytesTransferred,
    long TotalBytes,
    string Status,
    string? Text,
    bool IsError,
    bool IsDecided)
{
    public static IncomingTransferViewState Pending(IncomingTransferRequest request, string summary) => new(
        TransferState.WaitingForAcceptance,
        0,
        request.Items.Sum(static item => item.Size),
        summary,
        InitialText(request.Items),
        IsError: false,
        IsDecided: false);

    private static string? InitialText(IReadOnlyList<IncomingItem> items) =>
        items.Count == 1 && IsText(items[0]) ? items[0].Preview : null;
}

sealed class IncomingTransferOverlay : Component<IncomingTransferOverlayProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var (windowWidth, windowHeight) = UseWindowSize();
        var reduceMotion = UseReducedMotion();
        var request = Props.Request;
        var (view, updateView) = UseReducer(IncomingTransferViewState.Pending(
            request,
            IncomingSummary(t, request.Items)));
        var (copied, setCopied) = UseState(false);
        var (showVerification, setShowVerification) = UseState(false);
        var (showFileOptions, setShowFileOptions) = UseState(false);
        var (destinationDirectory, setDestinationDirectory) = UseState(Props.DownloadDirectory);
        var (selectedItemIds, updateSelectedItemIds) = UseReducer<IReadOnlySet<string>>(
            request.Items.Select(static item => item.Id).ToHashSet(StringComparer.Ordinal));
        var (targetFileNames, updateTargetFileNames) = UseReducer<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal));
        var (renameItemId, setRenameItemId) = UseState<string?>(null);
        var (renameFileName, setRenameFileName) = UseState(string.Empty);
        var (showQuickActions, setShowQuickActions) = UseState(false);
        var (folderError, setFolderError) = UseState<string?>(null);
        var cancellationRef = UseRef<CancellationTokenSource?>(null);
        var fileCardRef = UseRef<FrameworkElement?>(null);
        var fileOptionsDesiredRef = UseRef(false);
        var fileOptionsActualRef = UseRef(false);
        var fileOptionsAnimatingRef = UseRef(false);
        fileOptionsActualRef.Current = showFileOptions;
        var taskbarProgress = new TaskbarTransferProgress(
            view.State,
            view.BytesTransferred,
            view.TotalBytes,
            view.Status);

        UseEffect(() => UpdateTaskbarProgress(window, taskbarProgress), taskbarProgress);
        UseEffect(() => () => ClearTaskbarProgress(window));
        UseEffect(() =>
        {
            WidgetAppHost.SetIncoming(new WidgetTransferInfo(
                Title: request.Items.Count == 1
                    ? request.Items[0].FileName
                    : view.Status,
                Peer: request.Sender.Alias,
                Status: view.Status,
                BytesTransferred: view.BytesTransferred,
                TotalBytes: view.TotalBytes,
                Indeterminate: view.TotalBytes <= 0
                               || view.State is TransferState.Preparing or TransferState.WaitingForAcceptance));
            return () => WidgetAppHost.SetIncoming(null);
        }, view.State, view.BytesTransferred, view.TotalBytes, view.Status, request.RequestId);

        var acceptMutation = UseMutation<IncomingAcceptConfiguration, TransferResult>(async (configuration, mutationToken) =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationRef.Current?.Token ?? CancellationToken.None,
                mutationToken);
            var progress = new Progress<TransferProgress>(value =>
                updateView(current => current with
                {
                    State = value.State,
                    BytesTransferred = value.BytesTransferred,
                    TotalBytes = value.TotalBytes,
                    Status = t.Message(new("App", "ReceivingContent")),
                    IsDecided = true,
                }));
            return await Props.Node.AcceptAsync(
                request.RequestId,
                new AcceptTransferOptions
                {
                    DestinationDirectory = configuration.DestinationDirectory,
                    AcceptedItemIds = configuration.AcceptedItemIds,
                    TargetFileNames = configuration.TargetFileNames,
                    VerifySha256 = Props.VerifyChecksums,
                },
                progress,
                linked.Token).ConfigureAwait(false);
        });

        var declineMutation = UseMutation<bool, bool>(async (_, token) =>
        {
            await Props.Node.DeclineAsync(request.RequestId, token).ConfigureAwait(false);
            return true;
        });

        var progressValue = view.TotalBytes <= 0
            ? 0
            : Math.Clamp(view.BytesTransferred * 100d / view.TotalBytes, 0, 100);
        var progressText = $"{FormatBytes(view.BytesTransferred)} / {FormatBytes(view.TotalBytes)}";
        var isPending = acceptMutation.IsPending || declineMutation.IsPending;
        var showText = request.Items.Count == 1 && IsText(request.Items[0]);
        var itemRows = request.Items.Take(5).Select(item =>
                Grid(
                        columns: [GridSize.Star(), GridSize.Auto],
                        rows: [GridSize.Auto],
                        TextBlock(item.FileName)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .ToolTip(item.FileName)
                            .Grid(column: 0),
                        Caption(FormatBytes(item.Size))
                            .Foreground(Theme.SecondaryText)
                            .Grid(column: 1))
                    .WithKey(item.Id))
            .Cast<Element?>()
            .Append(request.Items.Count > 5
                ? Caption(t.Message(
                        new("App", "MoreItems"),
                        ("count", request.Items.Count - 5)))
                    .Foreground(Theme.SecondaryText)
                : null)
            .ToArray<Element?>();

        var sender = VStack(16,
                DeviceAvatar(request.Sender.DeviceType, OverlayAvatarSize)
                    .HAlign(HorizontalAlignment.Center),
                Title(request.Sender.Alias)
                    .TextAlignment(TextAlignment.Center)
                    .HAlign(HorizontalAlignment.Center),
                HStack(8,
                        DeviceTag(RemoteDeviceNumber(request.Sender)),
                        DeviceTag(DeviceModel(t, request.Sender.DeviceModel, request.Sender.DeviceType)))
                    .HAlign(HorizontalAlignment.Center))
            .HAlign(HorizontalAlignment.Center)
            .Transition(Transition.Enter(new FadeTransition()));

        var verificationButton = VerificationButton(
            t,
            () => setShowVerification(true),
            isEnabled: !isPending);
        var availableContentWidth = windowWidth > 0
            ? Math.Max(AppLayout.OverlayMinContentWidth, windowWidth - AppLayout.OverlayHorizontalChrome)
            : AppLayout.OverlayContentMaxWidth;
        // Keep the coordinate space stable while the card itself changes size.
        // Changing the parent width in the same render shifts both destination
        // coordinates and makes the connected animation appear off-centre.
        var overlayContentWidth = Math.Min(AppLayout.OverlayContentMaxWidth, availableContentWidth);
        var fileCardWidth = Math.Min(
            showFileOptions ? AppLayout.OverlayFileCardExpandedWidth : AppLayout.OverlayFileCardWidth,
            overlayContentWidth);
        var expandedFileCardHeight = windowHeight > 0
            ? Math.Clamp(
                windowHeight - AppLayout.OverlayExpandedHeightChrome,
                AppLayout.OverlayExpandedHeightMin,
                AppLayout.OverlayExpandedHeightMax)
            : AppLayout.OverlayExpandedHeightFallback;
        var fileCardAnimationKey = $"incoming-file-options:{request.RequestId:N}";
        Element fileCard = Card(
                Grid(
                    columns: [GridSize.Star()],
                    rows: [GridSize.Auto, showFileOptions ? GridSize.Star() : GridSize.Auto],
                    Grid(
                        columns: [GridSize.Star(), GridSize.Auto],
                        rows: [GridSize.Auto],
                        TextBlock(t.Message(
                                new("App", "SelectedIncomingFiles"),
                                ("selected", selectedItemIds.Count),
                                ("count", request.Items.Count)))
                            .SemiBold()
                            .VAlign(VerticalAlignment.Center)
                            .Grid(column: 0),
                        Button(
                                Icon(showFileOptions ? "\uE73F" : "\uE740").AccessibilityHidden(),
                                ToggleFileOptions)
                            .AutomationName(t.Message(new(
                                "App",
                                showFileOptions ? "HideReceiveOptions" : "ShowReceiveOptions")))
                            .ToolTip(t.Message(new(
                                "App",
                                showFileOptions ? "HideReceiveOptions" : "ShowReceiveOptions")))
                            .IsEnabled(!view.IsDecided && !isPending)
                            .SubtleButton()
                            .Grid(column: 1)),
                    showFileOptions
                        ? ReceiveOptions().Grid(row: 1)
                        : VStack(8, itemRows).Grid(row: 1)) with
                {
                    RowSpacing = 12,
                })
            .Width(fileCardWidth)
            .HAlign(HorizontalAlignment.Center);
        if (showFileOptions)
            fileCard = fileCard.Height(expandedFileCardHeight);
        fileCard = fileCard
            .WithKey(showFileOptions ? "incoming-file-options-expanded" : "incoming-file-options-collapsed")
            .OnMountAdd(element =>
            {
                fileCardRef.Current = element;
                if (!reduceMotion && fileOptionsAnimatingRef.Current)
                {
                    DeviceConnectedAnimation.StartDestinationAfterLayout(
                        fileCardAnimationKey,
                        element,
                        _ => CompleteFileOptionsTransition());
                }
            })
            .OnUnmountAdd(element =>
            {
                if (ReferenceEquals(fileCardRef.Current, element))
                    fileCardRef.Current = null;
            });

        Element content = showText
            ? VStack(12,
                TextBlock(view.IsDecided
                        ? t.Message(new("App", "ReceivedMessage"))
                        : t.Message(new("App", "WantsToSendMessage")))
                    .TextAlignment(TextAlignment.Center)
                    .HAlign(HorizontalAlignment.Center),
                TextBox(view.Text ?? t.Message(new("App", "TextAvailableAfterAccept")), _ => { })
                    .IsReadOnly()
                    .AcceptsReturn()
                    .TextWrapping(TextWrapping.Wrap)
                    .MinHeight(120)
                    .MaxHeight(260)
                    .AutomationName(t.Message(new("App", "ReceivedTextContent"))),
                HStack(12,
                        verificationButton,
                        Button(
                                HStack(8,
                                    Icon("\uE8C8").AccessibilityHidden(),
                                    TextBlock(copied
                                        ? t.Message(new("App", "Copied"))
                                        : t.Message(new("App", "Copy")))),
                                CopyText)
                            .AutomationName(t.Message(new("App", "CopyReceivedText")))
                            .IsEnabled(!string.IsNullOrEmpty(view.Text))
                            .MinWidth(120))
                    .HAlign(HorizontalAlignment.Center))
            : VStack(12,
                BodyLarge(view.Status)
                    .TextAlignment(TextAlignment.Center)
                    .HAlign(HorizontalAlignment.Center)
                    .Transition(Transition.Enter(new FadeTransition())),
                showFileOptions ? null : fileCard,
                verificationButton
                    .HAlign(HorizontalAlignment.Center)
                    .Transition(Transition.Enter(new FadeTransition())));

        var actions = RenderActions();

        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star(), GridSize.Auto],
                ScrollView(
                        VStack(28,
                                showFileOptions && !showText ? null : sender,
                                showFileOptions && !showText ? null : content)
                            .Width(overlayContentWidth)
                            .HAlign(HorizontalAlignment.Center))
                    .Padding(horizontal: 0, vertical: 40)
                    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                    .IsHitTestVisible(!showFileOptions)
                    .Grid(row: 0),
                Border(actions)
                    .Padding(horizontal: 40, vertical: 24)
                    .Grid(row: 1),
                showFileOptions && !showText
                    ? Border(fileCard)
                        .Padding(horizontal: 40, vertical: 24)
                        .HAlign(HorizontalAlignment.Stretch)
                        .VAlign(VerticalAlignment.Stretch)
                        .Grid(row: 0)
                    : null,
                Component<DeviceVerificationDialog, DeviceVerificationDialogProps>(new(
                    request.Sender,
                    Props.Node.Identity?.Fingerprint,
                    Props.Theme,
                    showVerification,
                    () => setShowVerification(false))),
                Component<IncomingQuickActionsDialog, IncomingQuickActionsDialogProps>(new(
                    request.Items
                        .Where(item => selectedItemIds.Contains(item.Id))
                        .Select(item => new IncomingQuickActionFile(
                            item.Id,
                            targetFileNames.TryGetValue(item.Id, out var renamed)
                                ? renamed
                                : item.FileName))
                        .ToArray(),
                    Props.Theme,
                    showQuickActions,
                    () => setShowQuickActions(false),
                    ApplyQuickActionNames)),
                RenameDialog())
            .Transition(Transition.Enter(new FadeTransition()))
            .Landmark(AutomationLandmarkType.Main);

        Element RenderActions()
        {
            if (!view.IsDecided)
            {
                return HStack(12,
                        Button(
                                HStack(8,
                                    Icon("\uE711").AccessibilityHidden(),
                                    TextBlock(t.Message(new("App", "Decline")))),
                                () => _ = DeclineAsync())
                            .AutomationName(t.Message(new("App", "Decline")))
                            .IsEnabled(!isPending)
                            .MinWidth(120)
                            .CriticalButton(),
                        Button(
                                HStack(8,
                                    Icon("\uE8FB").AccessibilityHidden(),
                                    TextBlock(t.Message(new("App", "Accept")))),
                                () => _ = AcceptAsync())
                            .AutomationName(t.Message(new("App", "Accept")))
                            .IsEnabled(!isPending && (showText || selectedItemIds.Count > 0))
                            .MinWidth(120)
                            .AccentButton()
                    )
                    .HAlign(HorizontalAlignment.Center);
            }

            if (acceptMutation.IsPending)
            {
                return VStack(12,
                        view.State is TransferState.Preparing or TransferState.WaitingForAcceptance
                            ? ProgressIndeterminate().MaxWidth(AppLayout.OverlayProgressMaxWidth).HAlign(HorizontalAlignment.Stretch)
                            : Progress(progressValue).MaxWidth(AppLayout.OverlayProgressMaxWidth).HAlign(HorizontalAlignment.Stretch),
                        Caption(progressText)
                            .Foreground(Theme.SecondaryText)
                            .HAlign(HorizontalAlignment.Center),
                        Button(
                                HStack(8,
                                    Icon("\uE711").AccessibilityHidden(),
                                    TextBlock(t.Message(new("App", "Cancel")))),
                                CancelReceive)
                            .AutomationName(t.Message(new("App", "Cancel")))
                            .MinWidth(120)
                            .HAlign(HorizontalAlignment.Center))
                    .MaxWidth(AppLayout.OverlayIncomingActionsMaxWidth)
                    .HAlign(HorizontalAlignment.Stretch);
            }

            return VStack(8,
                    TextBlock(view.Status)
                        .Foreground(view.IsError ? Theme.SystemCritical : Theme.SecondaryText)
                        .TextAlignment(TextAlignment.Center)
                        .LiveRegion(AutomationLiveSetting.Polite),
                    Button(
                            HStack(8,
                                Icon("\uE711").AccessibilityHidden(),
                                TextBlock(t.Message(new("App", "Close")))),
                            () => Props.Dismiss(request.RequestId))
                        .AutomationName(t.Message(new("App", "Close")))
                        .MinWidth(120)
                        .HAlign(HorizontalAlignment.Center))
                .HAlign(HorizontalAlignment.Center);
        }

        async Task AcceptAsync()
        {
            cancellationRef.Current?.Dispose();
            cancellationRef.Current = new CancellationTokenSource();
            updateView(current => current with
            {
                State = TransferState.Preparing,
                Status = t.Message(new("App", "ReceivingPreparing")),
                IsDecided = true,
            });
            try
            {
                var result = await acceptMutation.RunAsync(new(
                    destinationDirectory,
                    selectedItemIds.ToArray(),
                    new Dictionary<string, string>(targetFileNames, StringComparer.Ordinal)));
                if (result.IsSuccess)
                {
                    if (Props.SaveReceiveHistory)
                        ReceiveHistoryStore.Record(request.Sender.Alias, result);
                    AppNotificationService.ShowTransferComplete(
                        t.Message(new("App", "NotificationReceiveCompleteTitle")),
                        request.Items.Count == 1
                            ? t.Message(
                                new("App", "NotificationReceiveCompleteOne"),
                                ("device", request.Sender.Alias))
                            : t.Message(
                                new("App", "NotificationReceiveCompleteMany"),
                                ("count", request.Items.Count),
                                ("device", request.Sender.Alias)),
                        "receive-complete",
                        result.Items.Select(static item => item.SavedPath ?? string.Empty),
                        AppSettingsStore.Load().NotificationDefaultAction,
                        t.Message(new("App", "NotificationOpenFile")),
                        t.Message(new("App", "NotificationShowInFolder")));
                }

                var receivedText = showText && result.IsSuccess
                    ? await ReadReceivedTextAsync(result)
                    : view.Text;
                updateView(current => current with
                {
                    State = result.State,
                    BytesTransferred = result.BytesTransferred,
                    Status = result.State switch
                    {
                        TransferState.Completed => showText
                            ? t.Message(new("App", "TextReceived"))
                            : t.Message(new("App", "ContentSaved")),
                        TransferState.Cancelled => t.Message(new("App", "ReceiveCancelled")),
                        _ => result.Failure?.Message ?? t.Message(new("App", "ReceiveFailed")),
                    },
                    Text = receivedText ?? current.Text,
                    IsError = result.State == TransferState.Failed,
                    IsDecided = true,
                });
            }
            catch (Exception exception)
            {
                updateView(current => current with
                {
                    State = TransferState.Failed,
                    Status = exception.Message,
                    IsError = true,
                    IsDecided = true,
                });
            }
            finally
            {
                cancellationRef.Current?.Dispose();
                cancellationRef.Current = null;
            }
        }

        async Task DeclineAsync()
        {
            try
            {
                await declineMutation.RunAsync(true);
                Props.Dismiss(request.RequestId);
            }
            catch (Exception exception)
            {
                updateView(current => current with
                {
                    State = TransferState.Failed,
                    Status = exception.Message,
                    IsError = true,
                    IsDecided = true,
                });
            }
        }

        void CancelReceive() => cancellationRef.Current?.Cancel();

        void CopyText()
        {
            if (string.IsNullOrEmpty(view.Text))
                return;
            var package = new DataPackage();
            package.SetText(view.Text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            setCopied(true);
        }

        Element ReceiveOptions()
        {
            var rows = request.Items.Select(item => ReceiveItemRow(item).WithKey(item.Id)).ToArray<Element?>();
            return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Auto, GridSize.Auto, GridSize.Star()],
                VStack(4,
                        Grid(
                            columns: [GridSize.Star(), GridSize.Auto],
                            rows: [GridSize.Auto],
                            VStack(2,
                                    Caption(t.Message(new("App", "ReceiveSaveDirectory")))
                                        .Foreground(Theme.SecondaryText),
                                    TextBlock(destinationDirectory)
                                        .TextTrimming(TextTrimming.CharacterEllipsis)
                                        .ToolTip(destinationDirectory))
                                .Grid(column: 0),
                            Button(
                                    Icon("\uE8DA").AccessibilityHidden(),
                                    () => _ = PickDestinationDirectoryAsync())
                                .AutomationName(t.Message(new("App", "ChangeSaveLocation")))
                                .ToolTip(t.Message(new("App", "ChangeSaveLocation")))
                                .IsEnabled(!view.IsDecided && !isPending)
                                .Grid(column: 1)),
                        folderError is null
                            ? null
                            : Caption(folderError).Foreground(Theme.SystemCritical))
                    .Grid(row: 0),
                Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    HStack(8,
                            TextBlock(t.Message(new("App", "IncomingFiles")))
                                .SemiBold()
                                .VAlign(VerticalAlignment.Center),
                            Button(
                                    Icon("\uEF60").AccessibilityHidden(),
                                    () => setShowQuickActions(true))
                                .AutomationName(t.Message(new("App", "QuickActionsTitle")))
                                .ToolTip(t.Message(new("App", "QuickActionsTitle")))
                                .IsEnabled(!view.IsDecided && !isPending)
                                .VAlign(VerticalAlignment.Center),
                            Button(
                                    Icon("\uE7A7").AccessibilityHidden(),
                                    ResetFileOptions)
                                .AutomationName(t.Message(new("App", "ResetReceiveOptions")))
                                .ToolTip(t.Message(new("App", "ResetReceiveOptions")))
                                .IsEnabled(!view.IsDecided && !isPending)
                                .VAlign(VerticalAlignment.Center))
                        .HAlign(HorizontalAlignment.Left)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 0),
                    ThreeStateCheckBox(SelectAllState(), _ => ToggleSelectAll())
                        .AutomationName(t.Message(new(
                            "App",
                            selectedItemIds.Count == request.Items.Count
                                ? "DeselectAllIncomingFiles"
                                : "SelectAllIncomingFiles")))
                        .ToolTip(t.Message(new(
                            "App",
                            selectedItemIds.Count == request.Items.Count
                                ? "DeselectAllIncomingFiles"
                                : "SelectAllIncomingFiles")))
                        .IsEnabled(!view.IsDecided && !isPending)
                        .Scale(1.3f)
                        .MinWidth(32)
                        .MinHeight(32)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 1))
                    .Grid(row: 1),
                ScrollView(
                        VStack(8, rows)
                            .Padding(left: 4, top: 4, right: 16, bottom: 4))
                    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                    .Grid(row: 2)) with
            {
                RowSpacing = 12,
            };
        }

        Element ReceiveItemRow(IncomingItem item)
        {
            var isSelected = selectedItemIds.Contains(item.Id);
            var isRenamed = targetFileNames.ContainsKey(item.Id);
            var displayName = isRenamed
                ? targetFileNames[item.Id]
                : item.FileName;
            var renameHint = t.Message(
                new("App", "RenameSuccess"),
                ("size", FormatBytes(item.Size)));
            var canEdit = !view.IsDecided && !isPending;
            var canUndoRename = isRenamed && canEdit;
            var undoRename = canUndoRename
                ? () => UndoItemRename(item.Id)
                : (Action?)null;
            Element FileRowMenu() => MenuItems(
                MenuItem(
                    t.Message(new("App", "UndoIncomingFileRename")),
                    undoRename,
                    icon: "\uE7A7"),
                MenuItem(
                    t.Message(new("App", "Rename")),
                    canEdit ? () => OpenRenameDialog(item.Id, displayName) : null,
                    icon: "\uE70F"));
            return Border(
                    Grid(
                        columns: [GridSize.Star(), GridSize.Auto],
                        rows: [GridSize.Auto],
                        Button(
                                Grid(
                                    columns: [GridSize.Auto, GridSize.Star()],
                                    rows: [GridSize.Auto],
                                    Border(Icon(FileTypeGlyphs.ForFileName(displayName)).AccessibilityHidden())
                                        .Size(40, 40)
                                        .CornerRadius(8)
                                        .Background(Theme.SubtleFill)
                                        .Grid(column: 0),
                                    VStack(2,
                                            TextBlock(displayName)
                                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                                .ToolTip(displayName)
                                                .TextAlignment(TextAlignment.Left)
                                                .HAlign(HorizontalAlignment.Stretch),
                                            Caption(isRenamed ? renameHint : FormatBytes(item.Size))
                                                .Foreground(isRenamed ? Theme.SystemCaution : Theme.SecondaryText)
                                                .TextAlignment(TextAlignment.Left)
                                                .HAlign(HorizontalAlignment.Stretch))
                                        .VAlign(VerticalAlignment.Center)
                                        .HAlign(HorizontalAlignment.Stretch)
                                        .Grid(column: 1))
                                with
                                { ColumnSpacing = 12 },
                                () => ToggleItem(item.Id))
                            .AutomationName(t.Message(
                                new("App", isSelected ? "DeselectIncomingFile" : "SelectIncomingFile"),
                                ("file", displayName)))
                            .HAlign(HorizontalAlignment.Stretch)
                            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                            .GhostButton()
                            .WithContextFlyout(FileRowMenu())
                            .Grid(column: 0),
                        HStack(
                                Button(
                                        Icon("\uE7A7").AccessibilityHidden(),
                                        () => UndoItemRename(item.Id))
                                    .AutomationName(t.Message(new("App", "UndoIncomingFileRename")))
                                    .ToolTip(t.Message(new("App", "UndoIncomingFileRename")))
                                    .IsEnabled(canUndoRename)
                                    .WithContextFlyout(FileRowMenu()),
                                Button(
                                        Icon("\uE70F").AccessibilityHidden(),
                                        () => OpenRenameDialog(item.Id, displayName))
                                    .AutomationName(t.Message(
                                        new("App", "RenameIncomingFile"),
                                        ("file", displayName)))
                                    .ToolTip(t.Message(new("App", "Rename")))
                                    .IsEnabled(canEdit)
                                    .WithContextFlyout(FileRowMenu()))
                            .Margin(8)
                            .VAlign(VerticalAlignment.Center)
                            .Grid(column: 1)))
                .CornerRadius(8)
                .Background(Theme.CardBackground)
                .WithBorder(isSelected ? Theme.Accent : Theme.CardStroke, 2)
                .WithContextFlyout(FileRowMenu());
        }

        Element RenameDialog()
        {
            var validName = IsValidTargetFileName(renameFileName);
            return (ContentDialog(
                    t.Message(new("App", "Rename")),
                    TextBox(renameFileName, setRenameFileName)
                        .Header(t.Message(new("App", "Name")))
                        .AutomationName(t.Message(new("App", "Name"))),
                    primaryButtonText: t.Message(new("App", "Save"))) with
            {
                IsOpen = renameItemId is not null,
                SecondaryButtonText = t.Message(new("App", "Cancel")),
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = validName,
                OnClosed = result =>
                {
                    var itemId = renameItemId;
                    if (result == ContentDialogResult.Primary && itemId is not null && validName)
                    {
                        var originalName = request.Items.First(item => item.Id == itemId).FileName;
                        updateTargetFileNames(current =>
                        {
                            var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
                            if (string.Equals(renameFileName.Trim(), originalName, StringComparison.Ordinal))
                                next.Remove(itemId);
                            else
                                next[itemId] = renameFileName.Trim();
                            return next;
                        });
                    }

                    setRenameItemId(null);
                    setRenameFileName(string.Empty);
                },
            }).Themed(Props.Theme);
        }

        void ToggleItem(string itemId) => updateSelectedItemIds(current =>
        {
            var next = new HashSet<string>(current, StringComparer.Ordinal);
            if (!next.Remove(itemId))
                next.Add(itemId);
            return next;
        });

        bool? SelectAllState()
        {
            if (selectedItemIds.Count == 0)
                return false;
            if (selectedItemIds.Count == request.Items.Count)
                return true;
            return null;
        }

        void ToggleSelectAll()
        {
            if (selectedItemIds.Count == request.Items.Count)
            {
                updateSelectedItemIds(_ => new HashSet<string>(StringComparer.Ordinal));
                return;
            }

            updateSelectedItemIds(_ => request.Items
                .Select(static item => item.Id)
                .ToHashSet(StringComparer.Ordinal));
        }

        void ToggleFileOptions()
        {
            var desired = !fileOptionsDesiredRef.Current;
            fileOptionsDesiredRef.Current = desired;

            if (fileOptionsAnimatingRef.Current)
                return;

            BeginFileOptionsTransition(desired);
        }

        void BeginFileOptionsTransition(bool desired)
        {
            if (fileOptionsActualRef.Current == desired)
                return;

            void ApplyState() => setShowFileOptions(desired);

            if (reduceMotion || fileCardRef.Current is not { } source)
            {
                ApplyState();
                return;
            }

            fileOptionsAnimatingRef.Current = true;

            // Capture the source while it is still attached. Reactor's automatic
            // keyed connected animation can otherwise prepare it during unmount,
            // after the card has already left the visual tree.
            DeviceConnectedAnimation.NavigateToDestination(
                fileCardAnimationKey,
                source,
                ApplyState,
                new Microsoft.UI.Xaml.Media.Animation.BasicConnectedAnimationConfiguration());
        }

        void CompleteFileOptionsTransition()
        {
            fileOptionsAnimatingRef.Current = false;
            var desired = fileOptionsDesiredRef.Current;
            if (desired != fileOptionsActualRef.Current)
                BeginFileOptionsTransition(desired);
        }

        void OpenRenameDialog(string itemId, string currentName)
        {
            setRenameFileName(currentName);
            setRenameItemId(itemId);
        }

        void UndoItemRename(string itemId) => updateTargetFileNames(current =>
        {
            var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
            next.Remove(itemId);
            return next;
        });

        void ApplyQuickActionNames(IReadOnlyDictionary<string, string> names) =>
            updateTargetFileNames(current =>
            {
                var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
                foreach (var (itemId, fileName) in names)
                {
                    var originalName = request.Items.First(item => item.Id == itemId).FileName;
                    if (string.Equals(fileName, originalName, StringComparison.Ordinal))
                        next.Remove(itemId);
                    else
                        next[itemId] = fileName;
                }

                return next;
            });

        void ResetFileOptions()
        {
            updateSelectedItemIds(_ => request.Items
                .Select(static item => item.Id)
                .ToHashSet(StringComparer.Ordinal));
            updateTargetFileNames(_ => new Dictionary<string, string>(StringComparer.Ordinal));
        }

        async Task PickDestinationDirectoryAsync()
        {
            try
            {
                var picker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.Downloads,
                    CommitButtonText = t.Message(new("App", "Change")),
                };
                picker.FileTypeFilter.Add("*");
                var nativeWindow = window?.NativeWindow
                    ?? throw new InvalidOperationException(t.Message(new("App", "WindowUnavailable")));
                WinRT.Interop.InitializeWithWindow.Initialize(
                    picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow));
                var folder = await picker.PickSingleFolderAsync();
                if (folder is not null)
                {
                    setDestinationDirectory(folder.Path);
                    setFolderError(null);
                }
            }
            catch (Exception exception)
            {
                setFolderError(t.Message(
                    new("App", "PickFolderFailed"),
                    ("error", exception.Message)));
            }
        }
    }

    private static bool IsValidTargetFileName(string value)
    {
        var name = value.Trim();
        return name.Length > 0
               && !name.EndsWith('.')
               && !name.EndsWith(' ')
               && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
               && !name.Contains(':', StringComparison.Ordinal);
    }

    private static async Task<string?> ReadReceivedTextAsync(TransferResult result)
    {
        var texts = new List<string>();
        foreach (var item in result.Items)
        {
            if (item.SavedPath is not { } path || !File.Exists(path))
                continue;
            texts.Add(await File.ReadAllTextAsync(path).ConfigureAwait(false));
        }

        return texts.Count == 0 ? null : string.Join(Environment.NewLine, texts);
    }
}

sealed record IncomingAcceptConfiguration(
    string DestinationDirectory,
    IReadOnlyCollection<string> AcceptedItemIds,
    IReadOnlyDictionary<string, string> TargetFileNames);
