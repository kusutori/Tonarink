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

sealed record IncomingTransferOverlayProps(
    LocalSendNode Node,
    IncomingTransferRequest Request,
    string DownloadDirectory,
    bool SaveReceiveHistory,
    bool VerifyChecksums,
    ElementTheme Theme,
    Action<Guid> Dismiss);

sealed class IncomingTransferOverlay : Component<IncomingTransferOverlayProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var announce = this.UseAnnounce();
        var window = UseWindow();
        var storagePicker = new StoragePicker(
            window?.NativeWindow,
            t.Message(new("App", "WindowUnavailable")));
        var highContrast = UseHighContrast();
        var (windowWidth, windowHeight) = UseWindowSize();
        var reduceMotion = UseReducedMotion();
        var request = Props.Request;
        var (view, dispatchView) = UseReducer<IncomingTransferViewState, IncomingTransferAction>(
            IncomingTransferReducer.Reduce,
            IncomingTransferViewState.Initial(request, IncomingSummary(t, request.Items)));
        var (copied, setCopied) = UseState(false);
        var (showVerification, setShowVerification) = UseState(false);
        var (showFileOptions, setShowFileOptions) = UseState(false);
        var (destinationDirectory, setDestinationDirectory) = UseState(Props.DownloadDirectory);
        var (selectedItemIds, updateSelectedItemIds) = UseReducer<IReadOnlySet<string>>(
            IncomingFileCard.AllItemIds(request.Items));
        var (targetFileNames, updateTargetFileNames) = UseReducer<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal));
        var (renameItemId, setRenameItemId) = UseState<string?>(null);
        var (renameFileName, setRenameFileName) = UseState(string.Empty);
        var (showQuickActions, setShowQuickActions) = UseState(false);
        var (folderError, setFolderError) = UseState<string?>(null);
        var focusTrap = this.UseFocusTrap(
            !showVerification && !showQuickActions && renameItemId is null);
        var cancellationRef = UseRef<CancellationTokenSource?>();
        var fileCardRef = UseRef<FrameworkElement?>();
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
            TrayFlyoutStore.NotifyIncoming();
            return () =>
            {
                WidgetAppHost.SetIncoming(null);
                TrayFlyoutStore.NotifyIncoming();
            };
        }, view.State, view.BytesTransferred, view.TotalBytes, view.Status, request.RequestId);

        var acceptMutation =
            UseMutation<IncomingAcceptConfiguration, IncomingTransferResult>(async (configuration, mutationToken) =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationRef.Current?.Token ?? CancellationToken.None,
                    mutationToken);
                var acceptedItemIds = configuration.AcceptedItemIds.ToHashSet(StringComparer.Ordinal);
                var totalBytes = request.Items
                    .Where(item => acceptedItemIds.Contains(item.Id))
                    .Sum(static item => item.Size);
                var progressTitle = IncomingSummary(
                    t,
                    request.Items.Where(item => acceptedItemIds.Contains(item.Id)).ToArray());
                var progressNotification = AppNotificationService.StartTransferProgress(
                    request.RequestId,
                    t.Message(new("App", "NotificationReceiveProgressTitle"), ("device", request.Sender.Alias)),
                    progressTitle,
                    t.Message(new("App", "ReceivingContent")),
                    0,
                    totalBytes,
                    ProgressText(0, totalBytes),
                    "receive-progress");
                var progress = new Progress<TransferProgress>(value =>
                {
                    dispatchView(new IncomingTransferAction.Progressed(
                        value,
                        t.Message(new("App", "ReceivingContent"))));
                    progressNotification?.Report(
                        progressTitle,
                        t.Message(new("App", "ReceivingContent")),
                        value.BytesTransferred,
                        value.TotalBytes,
                        ProgressText(value.BytesTransferred, value.TotalBytes));
                });
                try
                {
                    var outcome = await Props.Node.AcceptAsync(
                            request.RequestId,
                            new AcceptTransferOptions
                            {
                                DestinationDirectory = configuration.DestinationDirectory,
                                AcceptedItemIds = configuration.AcceptedItemIds,
                                TargetFileNames = configuration.TargetFileNames,
                                VerifySha256 = Props.VerifyChecksums,
                            },
                            progress,
                            linked.Token)
                        .ConfigureAwait(false);
                    return CoreTransferOutcomeMapper.Map(outcome);
                }
                finally
                {
                    if (progressNotification is not null)
                        await progressNotification.RemoveAsync().ConfigureAwait(false);
                }
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
        var canEdit = !view.IsDecided && !isPending;
        var renameFileCommand = UseCommand(UseMemo(() => new Command<IncomingFileCommandTarget>
            {
                Label = t.Message(new("App", "Rename")),
                Icon = new FontIconData("\uE70F"),
                Accelerator = Accelerator(VirtualKey.F2),
                CanExecute = canEdit,
                Execute = target => OpenRenameDialog(target.ItemId, target.DisplayName),
            },
            t.Locale,
            canEdit));
        var undoRenameCommand = UseCommand(UseMemo(() => new Command<IncomingFileCommandTarget>
            {
                Label = t.Message(new("App", "UndoIncomingFileRename")),
                Icon = new FontIconData("\uE7A7"),
                Accelerator = Accelerator(VirtualKey.Z, VirtualKeyModifiers.Control),
                CanExecute = canEdit,
                Execute = target => updateTargetFileNames(current =>
                    IncomingFileCard.UndoRename(current, target.ItemId)),
            },
            t.Locale,
            canEdit));
        var showText = request.Items.Count == 1 && IsText(request.Items[0]);
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
        var fileCard = IncomingFileCard.Build(new(
            request,
            t,
            showFileOptions,
            canEdit,
            reduceMotion,
            fileOptionsAnimatingRef.Current,
            fileCardWidth,
            showFileOptions ? expandedFileCardHeight : null,
            fileCardAnimationKey,
            destinationDirectory,
            selectedItemIds,
            targetFileNames,
            folderError,
            Props.Theme,
            ToggleFileOptions,
            CompleteFileOptionsTransition,
            element => fileCardRef.Current = element,
            element =>
            {
                if (ReferenceEquals(fileCardRef.Current, element))
                    fileCardRef.Current = null;
            },
            itemId => updateSelectedItemIds(current => IncomingFileCard.ToggleItem(current, itemId)),
            () => updateSelectedItemIds(current => IncomingFileCard.ToggleSelectAll(current, request.Items)),
            ResetFileOptions,
            () => setShowQuickActions(true),
            renameFileCommand,
            undoRenameCommand,
            PickDestinationDirectoryAsync));

        var sender = VStack(16,
                DeviceAvatar(request.Sender.DeviceType, OverlayAvatarSize)
                    .HAlign(HorizontalAlignment.Center),
                Title(request.Sender.Alias)
                    .HeadingLevel(AutomationHeadingLevel.Level1)
                    .TextAlignment(TextAlignment.Center)
                    .HAlign(HorizontalAlignment.Center),
                HStack(8,
                        DeviceTag(RemoteDeviceNumber(request.Sender)),
                        DeviceTag(DeviceModel(t, request.Sender.DeviceModel, request.Sender.DeviceType)))
                    .HAlign(HorizontalAlignment.Center))
            .HAlign(HorizontalAlignment.Center)
            .Transition(Transition.Enter(new FadeTransition()));

        Element content = showText ? TextContent() : FileContent();
        var actions = (view.IsDecided, acceptMutation.IsPending) switch
        {
            (false, _) => UndecidedActions(),
            (true, true) => ReceivingActions(),
            (true, false) => FinishedActions(),
        };

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
                    names => updateTargetFileNames(current =>
                        IncomingFileCard.ApplyQuickActionNames(current, request.Items, names)))),
                IncomingFileCard.RenameDialog(
                    t,
                    request,
                    Props.Theme,
                    renameItemId,
                    renameFileName,
                    setRenameFileName,
                    setRenameItemId,
                    updateTargetFileNames),
                announce.Region)
            .Transition(Transition.Enter(new FadeTransition()))
            .FocusTrap(focusTrap)
            .AutomationName(t.Message(new("App", "ReceiveTitle")))
            .Landmark(AutomationLandmarkType.Main);

        Element TextContent() =>
            VStack(12,
                TextBlock(view.IsDecided
                        ? t.Message(new("App", "ReceivedMessage"))
                        : t.Message(new("App", "WantsToSendMessage")))
                    .TextAlignment(TextAlignment.Center)
                    .HAlign(HorizontalAlignment.Center),
                TextBox(view.Text ?? t.Message(new("App", "TextAvailableAfterAccept")), _ => { })
                    .IsReadOnly()
                    .AcceptsReturn()
                    .TextWrapping()
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
                    .HAlign(HorizontalAlignment.Center));

        Element FileContent() =>
            VStack(12,
                BodyLarge(view.Status)
                    .TextAlignment(TextAlignment.Center)
                    .LiveRegion(AutomationLiveSetting.Polite)
                    .HAlign(HorizontalAlignment.Center)
                    .Transition(Transition.Enter(new FadeTransition())),
                showFileOptions ? null : fileCard,
                verificationButton
                    .HAlign(HorizontalAlignment.Center)
                    .Transition(Transition.Enter(new FadeTransition())));

        Element UndecidedActions() =>
            HStack(12,
                    Button(
                            HStack(8,
                                Icon("\uE711").AccessibilityHidden(),
                                TextBlock(t.Message(new("App", "Decline")))),
                            () => _ = DeclineAsync())
                        .AutomationName(t.Message(new("App", "Decline")))
                        .IsEnabled(!isPending)
                        .MinWidth(120)
                        .CriticalButton(highContrast),
                    Button(
                            HStack(8,
                                Icon("\uE8FB").AccessibilityHidden(),
                                TextBlock(t.Message(new("App", "Accept")))),
                            () => _ = AcceptAsync())
                        .AutomationName(t.Message(new("App", "Accept")))
                        .IsEnabled(!isPending && (showText || selectedItemIds.Count > 0))
                        .OnMountAdd(element => element.Focus(FocusState.Programmatic))
                        .MinWidth(120)
                        .AccentButton())
                .HAlign(HorizontalAlignment.Center);

        Element ReceivingActions() =>
            VStack(12,
                    view.State is TransferState.Preparing or TransferState.WaitingForAcceptance
                        ? ProgressIndeterminate().MaxWidth(AppLayout.OverlayProgressMaxWidth)
                            .AutomationName(view.Status)
                            .HAlign(HorizontalAlignment.Stretch)
                        : Progress(progressValue).MaxWidth(AppLayout.OverlayProgressMaxWidth)
                            .AutomationName(view.Status)
                            .HAlign(HorizontalAlignment.Stretch),
                    Caption(progressText)
                        .Foreground(Theme.SecondaryText)
                        .HAlign(HorizontalAlignment.Center),
                    Button(
                            HStack(8,
                                Icon("\uE711").AccessibilityHidden(),
                                TextBlock(t.Message(new("App", "Cancel")))),
                            CancelReceive)
                        .AutomationName(t.Message(new("App", "Cancel")))
                        .OnMountAdd(element => element.Focus(FocusState.Programmatic))
                        .MinWidth(120)
                        .HAlign(HorizontalAlignment.Center))
                .MaxWidth(AppLayout.OverlayIncomingActionsMaxWidth)
                .HAlign(HorizontalAlignment.Stretch);

        Element FinishedActions() =>
            VStack(8,
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
                        .OnMountAdd(element => element.Focus(FocusState.Programmatic))
                        .MinWidth(120)
                        .HAlign(HorizontalAlignment.Center))
                .HAlign(HorizontalAlignment.Center);

        static string ProgressText(long bytesTransferred, long totalBytes) => totalBytes > 0
            ? $"{FormatBytes(bytesTransferred)} / {FormatBytes(totalBytes)}"
            : FormatBytes(bytesTransferred);

        async Task AcceptAsync()
        {
            cancellationRef.Current?.Dispose();
            cancellationRef.Current = new CancellationTokenSource();
            dispatchView(new IncomingTransferAction.Started(
                t.Message(new("App", "ReceivingPreparing"))));
            try
            {
                var result = await acceptMutation.RunAsync(new(
                    destinationDirectory,
                    selectedItemIds.ToArray(),
                    new Dictionary<string, string>(targetFileNames, StringComparer.Ordinal)));
                if (result is IncomingTransferResult.Completed completed)
                {
                    if (Props.SaveReceiveHistory)
                        ReceiveHistoryStore.Record(request.Sender.Alias, completed);
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
                        completed.Items.Select(static item => item.SavedPath ?? string.Empty),
                        AppSettingsStore.Load().NotificationDefaultAction,
                        t.Message(new("App", "NotificationOpenFile")),
                        t.Message(new("App", "NotificationShowInFolder")));
                }

                var (state, bytesTransferred, status, receivedText, isError) = result switch
                {
                    IncomingTransferResult.Completed value => (
                        TransferState.Completed,
                        value.Items.Sum(static item => item.BytesTransferred),
                        showText
                            ? t.Message(new("App", "TextReceived"))
                            : t.Message(new("App", "ContentSaved")),
                        showText ? await ReadReceivedTextAsync(value) : view.Text,
                        false),
                    IncomingTransferResult.Cancelled value => (
                        TransferState.Cancelled,
                        value.Items.Sum(static item => item.BytesTransferred),
                        t.Message(new("App", "ReceiveCancelled")),
                        view.Text,
                        false),
                    IncomingTransferResult.Failed value => (
                        TransferState.Failed,
                        value.Items.Sum(static item => item.BytesTransferred),
                        value.Failure.Message,
                        view.Text,
                        true),
                };
                dispatchView(new IncomingTransferAction.Completed(
                    state,
                    bytesTransferred,
                    status,
                    receivedText,
                    isError));
            }
            catch (Exception exception)
            {
                dispatchView(new IncomingTransferAction.Failed(exception.Message));
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
                dispatchView(new IncomingTransferAction.Failed(exception.Message));
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
            announce.Announce(t.Message(new("App", "Copied")));
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
                new BasicConnectedAnimationConfiguration());
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

        void ResetFileOptions()
        {
            updateSelectedItemIds(_ => IncomingFileCard.AllItemIds(request.Items));
            updateTargetFileNames(_ => new Dictionary<string, string>(StringComparer.Ordinal));
        }

        async Task PickDestinationDirectoryAsync()
        {
            try
            {
                var folder = await storagePicker.PickFolderAsync(t.Message(new("App", "Change")));
                if (folder is null)
                    return;

                setDestinationDirectory(folder.Path);
                setFolderError(null);
            }
            catch (Exception exception)
            {
                setFolderError(t.Message(
                    new("App", "PickFolderFailed"),
                    ("error", exception.Message)));
            }
        }
    }

    private static async Task<string?> ReadReceivedTextAsync(IncomingTransferResult.Completed result)
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
