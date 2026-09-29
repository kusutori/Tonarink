using LocalSendDotNet;
using System.Net;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Utilities.ByteSize;
using static Tonarink.Components.Devices.DeviceVisuals;
using static Tonarink.Components.Transfers.TransferOverlayVisuals;
using static Tonarink.Pages.Send.SelectedSendItemOperations;
using static Tonarink.Pages.Send.SendDeviceResolver;
using static Tonarink.Pages.Send.SendPageVisuals;
using static Tonarink.Pages.Send.SendTransferPresentation;

namespace Tonarink.Pages.Send;

sealed class SendPage : Component<SendPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var storagePicker = new StoragePicker(
            window?.NativeWindow,
            t.Message(new("App", "WindowUnavailable")));
        var reduceMotion = UseReducedMotion();
        var (observedWindowWidth, _) = UseWindowSize();
        var currentWindowWidth = window?.NativeWindow.Bounds.Width ?? observedWindowWidth;
        var isWideLayout = currentWindowWidth >= AppLayout.WideBreakpoint;
        var (_, refreshCachedPage) = UseReducer(0);
        var navigation = UseNavigation<AppRoute>();
        var selectedItems = Props.SelectedItems;
        var updateSelectedItems = Props.UpdateSelectedItems;
        var (pickerMessage, setPickerMessage) = UseState(t.Message(new("App", "NothingSelected")));
        var (isFileDropActive, setFileDropActive) = UseState(false);
        var (selectedItemId, setSelectedItemId) = UseState<Guid?>(null);
        var (text, setText) = UseState(string.Empty);
        var (showTextDialog, setShowTextDialog) = UseState(false);
        var (showAddressDialog, setShowAddressDialog) = UseState(false);
        var (renameItemId, setRenameItemId) = UseState<Guid?>(null);
        var (renameFileName, setRenameFileName) = UseState(string.Empty);
        var (showFavoritesDialog, setShowFavoritesDialog) = UseState(false);
        var (favoriteEdit, setFavoriteEdit) = UseState<FavoriteDeviceEdit?>(null);
        var (favoriteToDelete, setFavoriteToDelete) = UseState<FavoriteDevice?>(null);
        var (deviceFavoriteDraft, setDeviceFavoriteDraft) = UseState<FavoriteDevice?>(null);
        var (deviceFavoriteToDelete, setDeviceFavoriteToDelete) = UseState<FavoriteDevice?>(null);
        var (verificationDevice, setVerificationDevice) = UseState<LocalSendDevice?>(null);
        var (manualAddress, setManualAddress) = UseState(string.Empty);
        var (manualAddressError, setManualAddressError) = UseState<string?>(null);
        var (isResolvingAddress, setResolvingAddress) = UseState(false);
        var (recentManualAddress, setRecentManualAddress) = UseState(RecentManualAddressStore.Load());
        var (suggestedContactSend, setSuggestedContactSend) = UseState<SuggestedContactSend?>(null);
        var favorites = UseExternalStore(
            listener =>
            {
                FavoriteDeviceStore.Changed += listener;
                return () => FavoriteDeviceStore.Changed -= listener;
            },
            static () => FavoriteDeviceStore.Entries);
        var (transfer, dispatchTransfer) = UseReducer<TransferUiState, SendTransferAction>(
            SendTransferReducer.Reduce,
            new TransferUiState.Idle(t.Message(new("App", "SendHint"))));
        var sendCancellationRef = UseRef<CancellationTokenSource?>();
        var searchingPlayerRef = UseRef<AnimatedVisualPlayer?>();
        var shareSource = UseRef<WindowsShareSource?>();
        var shareTargetPayloadId = Props.ShareTargetPayload?.Id ?? Guid.Empty;

        UseEffect(() => () =>
        {
            shareSource.Current?.Dispose();
            shareSource.Current = null;
        });

        // A cached page keeps its previous hook state while it is outside the active tree.
        // Reconcile it before the transition and read the live window bounds so a resize
        // performed on another page is reflected immediately when Send becomes visible.
        UseNavigationLifecycle(
            onNavigatingTo: _ => refreshCachedPage(value => value + 1),
            onNavigatedTo: _ =>
                PlaySearchingAnimation(searchingPlayerRef.Current, play: !reduceMotion));

        UseEffect(() =>
        {
            if (Props.JumpListFavoriteFingerprint is not null)
                setShowFavoritesDialog(true);
        }, Props.JumpListFavoriteFingerprint);

        UseEffect(() =>
        {
            if (Props.ShareTargetPayload is not { } payload)
                return static () => { };

            var cancellation = new CancellationTokenSource();
            _ = ImportShareTargetPayloadAsync(payload, cancellation.Token);
            return () =>
            {
                cancellation.Cancel();
                cancellation.Dispose();
            };
        }, shareTargetPayloadId);

        var sendMutation = UseMutation<SendRequest, OutgoingTransferResult>(async (request, mutationToken) =>
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                request.CancellationToken,
                mutationToken);
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
                var outcome = await Props.Node!.SendAsync(
                        request.Device,
                        request.Items,
                        new SendOptions
                        {
                            Pin = request.Pin,
                            ComputeSha256 = Props.VerifyChecksums,
                        },
                        progress,
                        linkedCancellation.Token)
                    .ConfigureAwait(false);
                return CoreTransferOutcomeMapper.Map(outcome);
            }
            finally
            {
                if (progressNotification is not null)
                    await progressNotification.RemoveAsync().ConfigureAwait(false);
            }
        });

        UseEffect(() =>
        {
            if (suggestedContactSend is not { } pending
                || Props.Runtime.NodeState != LocalSendNodeState.Running
                || Props.Node is null)
                return;

            setSuggestedContactSend(null);
            _ = SendToSuggestedContactAsync(pending);
        }, suggestedContactSend?.Id ?? Guid.Empty, Props.Runtime.NodeState, selectedItems.Count);

        // Clipboard APIs require the UI/STA thread. Use a synchronous command dispatcher
        // to start the existing async flow there; an ExecuteAsync command is intentionally
        // avoided because UseCommand runs async command bodies on the thread pool.
        var pasteCommand = UseCommand(StandardCommand.Paste(() =>
        {
            _ = AddClipboardAsync();
        }) with
        {
            Label = t.Message(new("App", "Clipboard")),
            Description = t.Message(
                new("App", "ChooseItem"),
                ("item", t.Message(new("App", "Clipboard")))),
            DebounceMs = 250,
        });

        var selectionGrid = Grid(
                columns:
                [
                    GridSize.Star().MinSize(88),
                    GridSize.Star().MinSize(88),
                    GridSize.Star().MinSize(88),
                    GridSize.Star().MinSize(88),
                ],
                rows: [GridSize.Auto],
                SelectionTile(t.Message(new("App", "File")), "Document", () => _ = PickFileAsync(), t)
                    .Grid(column: 0),
                SelectionTile(t.Message(new("App", "Folder")), "Folder", () => _ = PickFolderAsync(), t)
                    .Grid(column: 1),
                SelectionTile(t.Message(new("App", "Text")), "Edit", () => setShowTextDialog(true), t)
                    .Grid(column: 2),
                SelectionTile(
                        t.Message(new("App", "Clipboard")),
                        "Paste",
                        () => pasteCommand.Execute?.Invoke(),
                        t)
                    .IsEnabled(pasteCommand.IsEnabled)
                    .Grid(column: 3)) with
        {
            ColumnSpacing = 12,
        };

        var selectedHeader = selectedItems.Count == 0
            ? t.Message(new("App", "NothingSelected"))
            : t.Message(
                new("App", "SelectedItems"),
                ("count", selectedItems.Count),
                ("size", FormatBytes(selectedItems.Sum(static item => item.Length))));
        var isPreviewProviderAvailable = Props.FilePreviewEnabled
                                         && FilePreviewLauncher.IsAvailable(
                                             Props.PreviewProvider,
                                             Props.PreviewExecutablePath);

        Element selectedItemsContent = selectedItems switch
        {
            [] => ScrollView(EmptySelection(
                    isFileDropActive,
                    pickerMessage,
                    Props.ExpandDragDropToEntireApp,
                    t))
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .VerticalContentAlignment(VerticalAlignment.Stretch),
            _ => (LazyVStack(
                    selectedItems,
                    static item => item.Id.ToString("N"),
                    (item, index) =>
                    Component<SelectedSendItemRow, SelectedSendItemRowProps>(new(
                            item,
                            selectedItemId == item.Id,
                            Props.FilePreviewEnabled,
                            isPreviewProviderAvailable
                            && FilePreviewLauncher.CanPreview(item.LocalPath),
                            Props.PreviewProvider,
                            Props.PreviewExecutablePath,
                            window?.NativeWindow is not null
                            && item.LocalPath is { } path
                            && File.Exists(path),
                            () => setSelectedItemId(
                                selectedItemId == item.Id ? null : item.Id),
                            () => ShareItemAsync(item),
                            () =>
                            {
                                setRenameFileName(ProtocolLeafName(item.Item.FileName));
                                setRenameItemId(item.Id);
                            },
                            () => updateSelectedItems(current => (SelectedSendItem[])
                            [
                                .. current.Select(candidate => candidate.Id == item.Id
                                    ? UndoRename(candidate)
                                    : candidate)
                            ]),
                            () => updateSelectedItems(current => (SelectedSendItem[])
                            [
                                .. current.Select(candidate => candidate.Id == item.Id
                                    ? RedoRename(candidate)
                                    : candidate)
                            ]),
                            () =>
                            {
                                if (selectedItemId == item.Id)
                                    setSelectedItemId(null);
                                updateSelectedItems(current => (SelectedSendItem[])
                                [
                                    .. current.Where(candidate => candidate.Id != item.Id)
                                ]);
                            }))
                        .PositionInSet(index + 1, selectedItems.Count)) with
                {
                    Spacing = 8,
                })
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(VerticalAlignment.Stretch),
        };

        selectedItemsContent = isWideLayout
            ? selectedItemsContent.Flex(grow: 1, basis: 0)
            : selectedItemsContent.Height(AppLayout.NarrowSendItemsViewportHeight);

        var selectedItemsCard = Card(
                FlexColumn(
                        FlexRow(
                                BodyStrong(selectedHeader).Flex(grow: 1, basis: 0),
                                selectedItems.Count == 0
                                    ? null
                                    : Button(t.Message(new("App", "Clear")), () =>
                                    {
                                        updateSelectedItems(_ => []);
                                        setSelectedItemId(null);
                                        setPickerMessage(t.Message(new("App", "NothingSelected")));
                                    }).AutomationName(t.Message(new("App", "Clear")))) with
                        {
                            AlignItems = FlexAlign.Center,
                            ColumnGap = 8,
                        },
                        selectedItemsContent) with
                {
                    RowGap = 12,
                })
            .VAlign(VerticalAlignment.Stretch);
        if (isWideLayout)
            selectedItemsCard = selectedItemsCard.Flex(grow: 1, shrink: 1, basis: 320);

        if (!Props.ExpandDragDropToEntireApp)
        {
            selectedItemsCard = selectedItemsCard
                .OnDragEnter(args =>
                {
                    if (!args.Data.HasFormat(StandardDataFormats.StorageItems))
                        return;

                    args.AcceptedOperation = DragOperations.Copy;
                    setFileDropActive(true);
                })
                .OnDragOver(args =>
                {
                    if (!args.Data.HasFormat(StandardDataFormats.StorageItems))
                        return;

                    args.AcceptedOperation = DragOperations.Copy;
                    args.UIOverride.Caption = t.Message(new("App", "DropFilesCaption"));
                    args.UIOverride.IsCaptionVisible = true;
                    args.UIOverride.IsGlyphVisible = true;
                })
                .OnDragLeave(_ => setFileDropActive(false))
                .OnDrop(args =>
                {
                    setFileDropActive(false);
                    args.AcceptedOperation = DragOperations.Copy;
                    _ = AddDroppedItemsAsync(args.Data);
                }, acceptedOps: DragOperations.Copy);
        }

        if (isFileDropActive)
        {
            selectedItemsCard = selectedItemsCard
                .Background(Theme.SystemAttentionBackground)
                .WithBorder(Theme.SystemAttention);
        }

        var devices = Props.Runtime.Devices;
        Element deviceBody = devices switch
        {
            [] => EmptyDevices(
                    t,
                    Props.Runtime.NodeState,
                    Props.Runtime.DiscoveryWarning,
                    SearchingDevicesAnimation())
                .VAlign(VerticalAlignment.Stretch),
            _ => VStack(8,
            [
                .. devices.Select((device, index) =>
                {
                    var favorite = favorites.GetValueOrDefault(device.Fingerprint);
                    return DeviceCard(
                            device,
                            favorite,
                            isEnabled: Props.Node?.State == LocalSendNodeState.Running
                                       && !sendMutation.IsPending,
                            onClick: source =>
                            {
                                if (selectedItems.Count == 0)
                                {
                                    setPickerMessage(t.Message(new("App", "SelectContentFirst")));
                                    return;
                                }

                                if (source is null || reduceMotion)
                                {
                                    _ = StartSendAsync(device, pin: null);
                                    return;
                                }

                                DeviceConnectedAnimation.NavigateToDestination(
                                    DeviceConnectedKey(device.Fingerprint),
                                    source,
                                    () => _ = StartSendAsync(device, pin: null));
                            },
                            onDetails: () => Props.OpenDeviceDetails(device),
                            onFavorite: () =>
                            {
                                if (favorite is null)
                                    setDeviceFavoriteDraft(CreateFavorite(device));
                                else
                                    setDeviceFavoriteToDelete(favorite);
                            },
                            onVerify: () => setVerificationDevice(device),
                            t)
                        .PositionInSet(index + 1, devices.Count)
                        .WithKey(device.Fingerprint);
                })
            ]),
        };

        Element deviceContent = isWideLayout
            ? ScrollView(deviceBody)
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .VerticalContentAlignment(VerticalAlignment.Stretch)
                .Flex(grow: 1, basis: 0)
            : deviceBody;

        var nearbyDevicesCard = Card(
                FlexColumn(
                        FlexRow(
                                BodyStrong(t.Message(new("App", "NearbyDevices")))
                                    .Flex(grow: 1, basis: 0),
                                AnimatedButtons.Refresh(
                                    t.Message(new("App", "RefreshDevices")),
                                    () => _ = Props.RefreshAsync(),
                                    isEnabled: !sendMutation.IsPending),
                                Button(Icon(AppIcons.IpAddress).AccessibilityHidden(), OpenAddressDialog)
                                    .AutomationName(t.Message(new("App", "SendToAddress")))
                                    .ToolTip(t.Message(new("App", "SendToAddress")))
                                    .MinWidth(40)
                                    .MinHeight(40)
                                    .IsEnabled(!sendMutation.IsPending
                                               && !isResolvingAddress
                                               && Props.Runtime.NodeState == LocalSendNodeState.Running),
                                Button(Icon(AppIcons.Favorite).AccessibilityHidden(), OpenFavoritesDialog)
                                    .AutomationName(t.Message(new("App", "FavoritesTitle")))
                                    .ToolTip(t.Message(new("App", "FavoritesTitle")))
                                    .MinWidth(40)
                                    .MinHeight(40)
                                    .IsEnabled(!sendMutation.IsPending
                                               && !isResolvingAddress
                                               && Props.Runtime.NodeState == LocalSendNodeState.Running),
                                Button(Icon(AppIcons.Link).AccessibilityHidden(), () =>
                                    {
                                        if (selectedItems.Count == 0)
                                        {
                                            setPickerMessage(t.Message(new("App", "SelectContentFirst")));
                                            return;
                                        }

                                        if (Props.Node?.State != LocalSendNodeState.Running)
                                            return;
                                        WebShareLaunch.Items = (SendItem[])
                                        [
                                            .. selectedItems.Select(static item => item.Item)
                                        ];
                                        navigation.Navigate(AppRoute.WebShare, AppNavigation.DrillIn);
                                    })
                                    .AutomationName(t.Message(new("App", "WebShareTitle")))
                                    .ToolTip(t.Message(new("App", "WebShareTitle")))
                                    .MinWidth(40)
                                    .MinHeight(40)
                                    .IsEnabled(!sendMutation.IsPending
                                               && Props.Runtime.NodeState == LocalSendNodeState.Running),
                                ToggleButton(
                                        AppIcons.MultipleReceivers,
                                        Props.KeepItemsForMultipleReceivers,
                                        Props.SetKeepItemsForMultipleReceivers)
                                    .FontFamily("Segoe Fluent Icons")
                                    .FontSize(20)
                                    .MinWidth(40)
                                    .MinHeight(40)
                                    .AutomationName(t.Message(new("App", "MultipleReceivers")))
                                    .ToolTip(t.Message(new("App", "MultipleReceiversDescription")))
                                    .IsEnabled(!sendMutation.IsPending)) with
                        {
                            AlignItems = FlexAlign.Center,
                            ColumnGap = 8,
                        },
                        deviceContent) with
                {
                    RowGap = 12,
                })
            .VAlign(VerticalAlignment.Stretch);
        if (isWideLayout)
            nearbyDevicesCard = nearbyDevicesCard.Flex(grow: 1, shrink: 1, basis: 320);

        Element contentCards = isWideLayout
            ? (FlexRow(selectedItemsCard, nearbyDevicesCard) with
            {
                AlignItems = FlexAlign.Stretch,
                AlignContent = FlexAlign.Stretch,
                ColumnGap = 16,
            })
            .VAlign(VerticalAlignment.Stretch)
            .Flex(grow: 1, basis: 0)
            : (FlexColumn(selectedItemsCard, nearbyDevicesCard) with
            {
                RowGap = 16,
            })
            .HAlign(HorizontalAlignment.Stretch);

        var pageBody = (FlexColumn(
                VStack(12,
                    Subtitle(t.Message(new("App", "ChooseContent")))
                        .HeadingLevel(AutomationHeadingLevel.Level2),
                    selectionGrid),
                contentCards) with
        {
            RowGap = 20,
        })
            .VAlign(isWideLayout ? VerticalAlignment.Stretch : VerticalAlignment.Top);

        var pageContainer = CommandHost(
            [pasteCommand],
            Border(pageBody)
                .Padding(AppLayout.PagePadding)
                .MaxWidth(AppLayout.PageMaxWidth)
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(isWideLayout ? VerticalAlignment.Stretch : VerticalAlignment.Top)
                .AutomationName(t.Message(new("App", "SendTitle")))
                .Landmark(AutomationLandmarkType.Main));

        var page = Grid(
            columns: [GridSize.Star()],
            rows: [GridSize.Star()],
            pageContainer.Grid(row: 0, column: 0),
            Component<SendTextDialog, SendTextDialogProps>(new(
                    Props.Theme,
                    showTextDialog,
                    text,
                    setText,
                    value =>
                    {
                        var item = new SendTextItem(value);
                        AddSelectedItems((SelectedSendItem[])
                        [
                            new(
                                Guid.NewGuid(),
                                item,
                                t.Message(new("App", "TextMessage")),
                                TextLength(value),
                                "text")
                        ]);
                    },
                    () => setShowTextDialog(false)))
                .Grid(row: 0, column: 0),
            Component<SendAddressDialog, SendAddressDialogProps>(new(
                    Props.Theme,
                    showAddressDialog,
                    manualAddress,
                    setManualAddress,
                    manualAddressError,
                    () => setManualAddressError(null),
                    isResolvingAddress,
                    recentManualAddress,
                    UseRecentAddress,
                    value => _ = SendToAddressAsync(value),
                    () => setShowAddressDialog(false)))
                .Grid(row: 0, column: 0),
            RenameDialog().Grid(row: 0, column: 0),
            Component<FavoriteDevicesDialog, FavoriteDevicesDialogProps>(new(
                    favorites,
                    Props.JumpListFavoriteFingerprint,
                    Props.Theme,
                    showFavoritesDialog,
                    SendFavorite,
                    () => setShowFavoritesDialog(false),
                    () => setFavoriteEdit(new FavoriteDeviceEdit.Create(
                        new FavoriteDevice(
                            $"manual:{Guid.NewGuid():N}",
                            string.Empty,
                            string.Empty,
                            LocalSendOptions.DefaultPort))),
                    favorite => setFavoriteEdit(new FavoriteDeviceEdit.Update(favorite)),
                    setFavoriteToDelete,
                    () =>
                    {
                        setShowFavoritesDialog(false);
                        if (Props.JumpListFavoriteFingerprint is { } fingerprint)
                            Props.ConsumeJumpListFavorite(fingerprint);
                    }))
                .Grid(row: 0, column: 0),
            favoriteEdit is null
                ? null
                : Component<FavoriteDeviceDialog, FavoriteDeviceDialogProps>(new(
                        favoriteEdit switch
                        {
                            FavoriteDeviceEdit.Create(var device) => device,
                            FavoriteDeviceEdit.Update(var device) => device,
                        },
                        favoriteEdit is FavoriteDeviceEdit.Create,
                        Props.Theme,
                        FavoriteDeviceStore.Upsert,
                        () =>
                        {
                            setFavoriteEdit(null);
                            setShowFavoritesDialog(true);
                        }))
                    .Grid(row: 0, column: 0),
            Component<DeleteFavoriteDialog, DeleteFavoriteDialogProps>(new(
                    favoriteToDelete?.Name ?? string.Empty,
                    Props.Theme,
                    favoriteToDelete is not null,
                    () =>
                    {
                        if (favoriteToDelete is { } target)
                            FavoriteDeviceStore.Remove(target.Fingerprint);
                    },
                    () =>
                    {
                        setFavoriteToDelete(null);
                        setShowFavoritesDialog(true);
                    }))
                .Grid(row: 0, column: 0),
            deviceFavoriteDraft is null
                ? null
                : Component<FavoriteDeviceDialog, FavoriteDeviceDialogProps>(new(
                        deviceFavoriteDraft,
                        IsNew: true,
                        Props.Theme,
                        FavoriteDeviceStore.Upsert,
                        () => setDeviceFavoriteDraft(null)))
                    .WithKey(deviceFavoriteDraft.Fingerprint)
                    .Grid(row: 0, column: 0),
            Component<DeleteFavoriteDialog, DeleteFavoriteDialogProps>(new(
                    deviceFavoriteToDelete?.Name ?? string.Empty,
                    Props.Theme,
                    deviceFavoriteToDelete is not null,
                    () =>
                    {
                        if (deviceFavoriteToDelete is { } target)
                            FavoriteDeviceStore.Remove(target.Fingerprint);
                    },
                    () => setDeviceFavoriteToDelete(null)))
                .Grid(row: 0, column: 0),
            verificationDevice is null
                ? null
                : Component<DeviceVerificationDialog, DeviceVerificationDialogProps>(new(
                        verificationDevice,
                        Props.Runtime.Identity?.Fingerprint,
                        Props.Theme,
                        IsOpen: true,
                        () => setVerificationDevice(null)))
                    .WithKey(verificationDevice.Fingerprint)
                    .Grid(row: 0, column: 0));

        return isWideLayout
            ? page
            : ScrollView(page)
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .VerticalContentAlignment(VerticalAlignment.Top);

        Element SearchingDevicesAnimation() =>
            (AnimatedVisualPlayer() with { AutoPlay = false })
            .Size(144, 144)
            .AccessibilityHidden()
            .OnMountAdd(element =>
            {
                if (element is not AnimatedVisualPlayer player)
                    return;

                searchingPlayerRef.Current = player;
                PlaySearchingAnimation(player, play: !reduceMotion);
            })
            .OnUnmountAdd(element =>
            {
                if (element is AnimatedVisualPlayer player
                    && ReferenceEquals(searchingPlayerRef.Current, player))
                {
                    searchingPlayerRef.Current = null;
                }
            });

        Element RenameDialog()
            => Component<RenameItemDialog, RenameItemDialogProps>(new(
                Props.Theme,
                renameItemId is not null,
                renameFileName,
                setRenameFileName,
                fileName =>
                {
                    if (renameItemId is not { } itemId)
                        return;

                    updateSelectedItems(current => (SelectedSendItem[])
                    [
                        .. current.Select(item => item.Id == itemId
                            ? Rename(item, fileName)
                            : item)
                    ]);
                },
                () =>
                {
                    setRenameItemId(null);
                    setRenameFileName(string.Empty);
                }));

        async Task PickFileAsync()
        {
            try
            {
                var files = await storagePicker.PickFilesAsync(t.Message(new("App", "Add")));
                if (files.Count == 0)
                    return;

                var selected = new List<SelectedSendItem>(files.Count);
                foreach (var file in files)
                    selected.Add(await SelectedSendItemReader.FromStorageFileAsync(
                        file,
                        file.Name,
                        CancellationToken.None));
                AddSelectedItems(selected);
            }
            catch (Exception exception)
            {
                setPickerMessage(t.Message(
                    new("App", "PickFileFailed"),
                    ("error", exception.Message)));
            }
        }

        async Task PickFolderAsync()
        {
            try
            {
                var folder = await storagePicker.PickFolderAsync(t.Message(new("App", "AddFolder")));
                if (folder is null)
                    return;

                var selected = await SelectedSendItemReader.FromFolderAsync(folder, CancellationToken.None);
                if (selected.Count == 0)
                {
                    setPickerMessage(t.Message(new("App", "FolderEmpty")));
                    return;
                }

                AddSelectedItems(selected);
            }
            catch (Exception exception)
            {
                setPickerMessage(t.Message(
                    new("App", "PickFolderFailed"),
                    ("error", exception.Message)));
            }
        }

        async Task AddClipboardAsync()
        {
            try
            {
                var data = Clipboard.GetContent();
                if (data.Contains(StandardDataFormats.StorageItems))
                {
                    var storageItems = await data.GetStorageItemsAsync();
                    var selected = new List<SelectedSendItem>();
                    foreach (var storageItem in storageItems)
                    {
                        switch (storageItem)
                        {
                            case StorageFile file:
                                selected.Add(await SelectedSendItemReader.FromStorageFileAsync(
                                    file,
                                    file.Name,
                                    CancellationToken.None));
                                break;
                            case StorageFolder folder:
                                selected.AddRange(await SelectedSendItemReader.FromFolderAsync(
                                    folder,
                                    CancellationToken.None));
                                break;
                        }
                    }

                    if (selected.Count > 0)
                    {
                        AddSelectedItems(selected);
                        return;
                    }
                }

                if (data.Contains(StandardDataFormats.Text))
                {
                    var clipboardText = await data.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(clipboardText))
                    {
                        var item = new SendTextItem(clipboardText, "clipboard.txt");
                        AddSelectedItems((SelectedSendItem[])[
                            new(
                                Guid.NewGuid(),
                                item,
                                t.Message(new("App", "ClipboardText")),
                                TextLength(clipboardText),
                                "clipboard")
                        ]);
                        return;
                    }
                }

                if (data.Contains(StandardDataFormats.Bitmap))
                {
                    var bitmap = await SelectedSendItemReader.FromClipboardBitmapAsync(
                        data,
                        CancellationToken.None);
                    AddSelectedItems((SelectedSendItem[])[
                        bitmap with
                        {
                            DisplayName = t.Message(new("App", "ClipboardImage")),
                        }
                    ]);
                    return;
                }

                setPickerMessage(t.Message(new("App", "ClipboardEmpty")));
            }
            catch (Exception exception)
            {
                setPickerMessage(t.Message(
                    new("App", "ClipboardReadFailed"),
                    ("error", exception.Message)));
            }
        }

        void AddSelectedItems(IReadOnlyCollection<SelectedSendItem> newItems)
        {
            updateSelectedItems(current => (SelectedSendItem[])[.. current, .. newItems]);
            setPickerMessage(t.Message(new("App", "ItemsAdded"), ("count", newItems.Count)));
            dispatchTransfer(new SendTransferAction.Reset(t.Message(new("App", "SendHint"))));
        }

        async Task ImportShareTargetPayloadAsync(
            ShareTargetPayload payload,
            CancellationToken cancellationToken)
        {
            try
            {
                var imported = new List<SelectedSendItem>();
                foreach (var sharedItem in payload.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (sharedItem)
                    {
                        case ShareTargetItem.FileSystem { IsDirectory: true } directory:
                            imported.AddRange(await SelectedSendItemReader.FromFolderPathAsync(
                                directory.Path,
                                cancellationToken));
                            break;

                        case ShareTargetItem.FileSystem file:
                            var fileInfo = new FileInfo(file.Path);
                            if (!fileInfo.Exists)
                                throw new FileNotFoundException("The shared file is no longer available.", file.Path);
                            imported.Add(new SelectedSendItem(
                                Guid.NewGuid(),
                                new SendFileItem(fileInfo.FullName, fileInfo.Name),
                                fileInfo.Name,
                                fileInfo.Length,
                                "file",
                                fileInfo.FullName));
                            break;

                        case ShareTargetItem.Text sharedText:
                            imported.Add(new SelectedSendItem(
                                Guid.NewGuid(),
                                new SendTextItem(sharedText.Value, sharedText.FileName),
                                sharedText.FileName,
                                TextLength(sharedText.Value),
                                "text"));
                            break;
                    }
                }

                if (imported.Count == 0)
                    throw new InvalidDataException(t.Message(new("App", "ShareTargetEmpty")));

                AddSelectedItems(imported);
                if (!string.IsNullOrWhiteSpace(payload.SuggestedContactFingerprint))
                {
                    setSuggestedContactSend(new(
                        Guid.NewGuid(),
                        payload.SuggestedContactFingerprint));
                }
                Props.ConsumeShareTargetPayload(payload.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The page or share activation was replaced while files were being imported.
            }
            catch (Exception exception)
            {
                setPickerMessage(t.Message(
                    new("App", "ShareTargetFailed"),
                    ("error", exception.Message)));
                Props.ConsumeShareTargetPayload(payload.Id);
            }
        }

        async Task StartSendAsync(LocalSendDevice device, string? pin, string? resolvedManualAddress = null)
        {
            if (Props.Node?.State != LocalSendNodeState.Running || selectedItems.Count == 0)
                return;

            var cancellation = new CancellationTokenSource();
            sendCancellationRef.Current?.Dispose();
            sendCancellationRef.Current = cancellation;
            var startingState = new TransferUiState.Active(
                TransferState.Preparing,
                device.Alias,
                0,
                selectedItems.Sum(static item => item.Length),
                t.Message(new("App", "PreparingForDevice"), ("device", device.Alias)),
                IsError: false);
            dispatchTransfer(new SendTransferAction.Started(startingState));
            PublishTransferOverlay(
                device,
                (SendItem[])[.. selectedItems.Select(static item => item.Item)],
                startingState,
                new OutgoingOverlayUpdate.Pending());

            try
            {
                var result = await sendMutation.RunAsync(new(
                    Guid.NewGuid(),
                    device,
                    (SendItem[])[.. selectedItems.Select(static item => item.Item)],
                    selectedItems.Sum(static item => item.Length),
                    pin,
                    cancellation.Token));
                if (result is OutgoingTransferResult.PinRequired pinRequired)
                {
                    var waitingState = new TransferUiState.Active(
                        TransferState.WaitingForAcceptance,
                        device.Alias,
                        0,
                        selectedItems.Sum(static item => item.Length),
                        t.Message(new("App", "TargetRequiresPin")),
                        pinRequired.InvalidPin);
                    dispatchTransfer(new SendTransferAction.PinRequested(waitingState));
                    PublishTransferOverlay(
                        device,
                        (SendItem[])[.. selectedItems.Select(static item => item.Item)],
                        waitingState,
                        new OutgoingOverlayUpdate.AwaitingPin(new OutgoingPinPrompt(
                            pinRequired.InvalidPin ? t.Message(new("App", "PinIncorrect")) : null,
                            enteredPin => _ = StartSendAsync(device, enteredPin, resolvedManualAddress),
                            () => dispatchTransfer(new SendTransferAction.Reset(
                                t.Message(new("App", "SendHint")))))));
                    return;
                }

                if (result is OutgoingTransferResult.PinRateLimited)
                {
                    var errorState = new TransferUiState.Active(
                        TransferState.Failed,
                        device.Alias,
                        0,
                        selectedItems.Sum(static item => item.Length),
                        t.Message(new("App", "PinRateLimited")),
                        IsError: true);
                    dispatchTransfer(new SendTransferAction.Failed(errorState));
                    PublishTransferOverlay(
                        device,
                        (SendItem[])[.. selectedItems.Select(static item => item.Item)],
                        errorState,
                        new OutgoingOverlayUpdate.Finished());
                    return;
                }

                var resultState = ResultState(
                    t,
                    result,
                    device.Alias,
                    selectedItems.Sum(static item => item.Length));
                dispatchTransfer(new SendTransferAction.Finished(resultState));
                PublishTransferOverlay(
                    device,
                    (SendItem[])[.. selectedItems.Select(static item => item.Item)],
                    resultState,
                    new OutgoingOverlayUpdate.Finished());
                if (result is OutgoingTransferResult.Completed)
                {
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
                        selectedItems
                            .Select(static selected => selected.Item)
                            .OfType<SendFileItem>()
                            .Select(static item => item.Path),
                        AppSettingsStore.Load().NotificationDefaultAction,
                        t.Message(new("App", "NotificationOpenFile")),
                        t.Message(new("App", "NotificationShowInFolder")));
                    if (!Props.KeepItemsForMultipleReceivers)
                    {
                        updateSelectedItems(_ => []);
                        setPickerMessage(t.Message(new("App", "NothingSelected")));
                    }
                }
            }
            catch (Exception exception)
            {
                var errorState = new TransferUiState.Active(
                    TransferState.Failed,
                    device.Alias,
                    0,
                    selectedItems.Sum(static item => item.Length),
                    exception.Message,
                    IsError: true);
                dispatchTransfer(new SendTransferAction.Failed(errorState));
                PublishTransferOverlay(
                    device,
                    (SendItem[])[.. selectedItems.Select(static item => item.Item)],
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

        void OpenAddressDialog()
        {
            if (selectedItems.Count == 0)
            {
                setPickerMessage(t.Message(new("App", "SelectContentFirst")));
                return;
            }

            if (Props.Node?.State != LocalSendNodeState.Running)
                return;

            setManualAddressError(null);
            setManualAddress(recentManualAddress ?? string.Empty);
            setShowAddressDialog(true);
        }

        void OpenFavoritesDialog()
        {
            if (Props.Node?.State != LocalSendNodeState.Running)
                return;

            setShowFavoritesDialog(true);
        }

        void SendFavorite(FavoriteDevice favorite)
        {
            setShowFavoritesDialog(false);
            if (selectedItems.Count == 0)
            {
                setPickerMessage(t.Message(new("App", "SelectContentFirst")));
                return;
            }

            var address = IPAddress.Parse(favorite.Address);
            _ = SendToAddressAsync(FormatAddress(address, favorite.Port));
        }

        async Task SendToSuggestedContactAsync(SuggestedContactSend pending)
        {
            if (!FavoriteDeviceStore.Entries.TryGetValue(pending.Fingerprint, out var favorite))
            {
                setPickerMessage(t.Message(new("App", "ShareSuggestedDeviceUnavailable")));
                return;
            }

            var discovered = Props.Runtime.Devices.FirstOrDefault(device =>
                string.Equals(device.Fingerprint, pending.Fingerprint, StringComparison.Ordinal));
            if (discovered is not null)
            {
                await StartSendAsync(discovered, pin: null).ConfigureAwait(true);
                return;
            }

            if (!IPAddress.TryParse(favorite.Address, out var address))
            {
                setPickerMessage(t.Message(
                    new("App", "ShareSuggestedDeviceOffline"),
                    ("device", favorite.Name)));
                return;
            }

            await SendToAddressAsync(
                    FormatAddress(address, favorite.Port),
                    showDialogOnFailure: false,
                    expectedFingerprint: favorite.Fingerprint,
                    suggestedDeviceName: favorite.Name)
                .ConfigureAwait(true);
        }

        void UseRecentAddress()
        {
            if (recentManualAddress is not { } address)
                return;

            setManualAddress(address);
            setShowAddressDialog(false);
            _ = SendToAddressAsync(address);
        }

        async Task SendToAddressAsync(
            string value,
            bool showDialogOnFailure = true,
            string? expectedFingerprint = null,
            string? suggestedDeviceName = null)
        {
            if (!TryParseAddress(value, out var address, out var port))
            {
                if (showDialogOnFailure)
                {
                    setManualAddressError(t.Message(new("App", "InvalidDeviceAddress")));
                    setShowAddressDialog(true);
                }
                else
                {
                    setPickerMessage(t.Message(
                        new("App", "ShareSuggestedDeviceOffline"),
                        ("device", suggestedDeviceName ?? value)));
                }
                return;
            }

            var normalizedAddress = FormatAddress(address, port);
            setManualAddress(normalizedAddress);
            setManualAddressError(null);
            setResolvingAddress(true);
            try
            {
                DeviceResolution resolution = Props.Node is { } node
                    ? await ResolveAsync(
                            node,
                            address,
                            port,
                            Props.Runtime.Identity?.Protocol ?? LocalSendProtocol.Https,
                            expectedFingerprint)
                        .ConfigureAwait(true)
                    : new DeviceResolutionFailure(new InvalidOperationException(
                        "The LocalSend node is not running."));
                switch (resolution)
                {
                    case ResolvedDevice(var device):
                        await StartSendAsync(device, pin: null, normalizedAddress)
                            .ConfigureAwait(true);
                        return;

                    case DeviceResolutionFailure:
                        if (showDialogOnFailure)
                        {
                            setManualAddressError(t.Message(
                                new("App", "DeviceAddressNotFound"),
                                ("address", normalizedAddress)));
                            setShowAddressDialog(true);
                        }
                        else
                        {
                            setPickerMessage(t.Message(
                                new("App", "ShareSuggestedDeviceOffline"),
                                ("device", suggestedDeviceName ?? normalizedAddress)));
                        }
                        return;
                }
            }
            finally
            {
                setResolvingAddress(false);
            }
        }

        async Task AddDroppedItemsAsync(DragData dragData)
        {
            try
            {
                var selected = await SelectedSendItemReader.ReadDroppedAsync(dragData);

                if (selected.Count == 0)
                {
                    setPickerMessage(t.Message(new("App", "DroppedItemsEmpty")));
                    return;
                }

                AddSelectedItems(selected);
            }
            catch (Exception exception)
            {
                setPickerMessage(t.Message(
                    new("App", "DropItemsFailed"),
                    ("error", exception.Message)));
            }
        }

        async Task ShareItemAsync(SelectedSendItem item)
        {
            if (window?.NativeWindow is not { } nativeWindow || item.LocalPath is not { } path)
                return;

            var source = shareSource.Current ??= new WindowsShareSource(
                nativeWindow,
                t.Message(new("App", "ShareFileFailed")));
            if (!await source.ShareFileAsync(path, item.DisplayName).ConfigureAwait(true))
                setPickerMessage(t.Message(new("App", "ShareFileFailed")));
        }

        void PublishTransferOverlay(
            LocalSendDevice device,
            IReadOnlyList<SendItem> items,
            TransferUiState state,
            OutgoingOverlayUpdate update)
        {
            var snapshot = new OutgoingTransferSnapshot(
                Props.Runtime.Identity,
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
            Props.SetTransferOverlay(update switch
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
