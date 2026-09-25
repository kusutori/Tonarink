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
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Utilities.ByteSize;
using static Tonarink.Components.DeviceVisuals;
using static Tonarink.Components.TransferOverlayVisuals;

namespace Tonarink.Pages.Send;

sealed record SendPageProps(
    AppRuntimeState Runtime,
    LocalSendNode? Node,
    ElementTheme Theme,
    Func<Task> RefreshAsync,
    Action<OutgoingTransferViewState?> SetTransferOverlay,
    ShareTargetPayload? ShareTargetPayload,
    Action<Guid> ConsumeShareTargetPayload,
    IReadOnlyList<SelectedSendItem> SelectedItems,
    Action<Func<IReadOnlyList<SelectedSendItem>, IReadOnlyList<SelectedSendItem>>> UpdateSelectedItems,
    bool KeepItemsForMultipleReceivers,
    Action<bool> SetKeepItemsForMultipleReceivers,
    bool VerifyChecksums,
    Action<LocalSendDevice> OpenDeviceDetails,
    string? JumpListFavoriteFingerprint,
    Action<string> ConsumeJumpListFavorite);

sealed record SelectedSendItem(
    Guid Id,
    SendItem Item,
    string DisplayName,
    long Length,
    string Kind);

sealed record SendRequest(
    Guid TransferId,
    LocalSendDevice Device,
    IReadOnlyList<SendItem> Items,
    long TotalBytes,
    string? Pin,
    CancellationToken CancellationToken);

sealed record SuggestedContactSend(
    Guid Id,
    string Fingerprint);

sealed record ResolvedDevice(LocalSendDevice Device);

sealed record DeviceResolutionFailure(Exception Cause);

union DeviceResolution(ResolvedDevice, DeviceResolutionFailure);

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
        var (text, setText) = UseState(string.Empty);
        var (showTextDialog, setShowTextDialog) = UseState(false);
        var (showAddressDialog, setShowAddressDialog) = UseState(false);
        var (showFavoritesDialog, setShowFavoritesDialog) = UseState(false);
        var (favoriteEdit, setFavoriteEdit) = UseState<FavoriteDeviceEdit?>(null);
        var (favoriteToDelete, setFavoriteToDelete) = UseState<FavoriteDevice?>(null);
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
        var shareTargetPayloadId = Props.ShareTargetPayload?.Id ?? Guid.Empty;

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

        var sendMutation = UseMutation<SendRequest, TransferResult>(async (request, mutationToken) =>
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
                return await Props.Node!.SendAsync(
                    request.Device,
                    request.Items,
                    new SendOptions
                    {
                        Pin = request.Pin,
                        ComputeSha256 = Props.VerifyChecksums,
                    },
                    progress,
                    linkedCancellation.Token).ConfigureAwait(false);
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

        Element selectedItemsBody = selectedItems switch
        {
            [] => EmptySelection(
                isFileDropActive,
                pickerMessage,
                t),
            _ => VStack(8,
            [
                .. selectedItems.Select((item, index) => SelectedItemRow(
                        item,
                        () => updateSelectedItems(current => (SelectedSendItem[])
                        [
                            .. current.Where(candidate => candidate.Id != item.Id)
                        ]),
                        t)
                    .PositionInSet(index + 1, selectedItems.Count)
                    .WithKey(item.Id.ToString("N")))
            ]),
        };

        Element selectedItemsContent = ScrollView(selectedItemsBody)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .VerticalContentAlignment(VerticalAlignment.Stretch);
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
                                Button(Icon("\uF272").AccessibilityHidden(), OpenAddressDialog)
                                    .AutomationName(t.Message(new("App", "SendToAddress")))
                                    .ToolTip(t.Message(new("App", "SendToAddress")))
                                    .MinWidth(40)
                                    .MinHeight(40)
                                    .IsEnabled(!sendMutation.IsPending
                                               && !isResolvingAddress
                                               && Props.Runtime.NodeState == LocalSendNodeState.Running),
                                Button(Icon("\uEB52").AccessibilityHidden(), OpenFavoritesDialog)
                                    .AutomationName(t.Message(new("App", "FavoritesTitle")))
                                    .ToolTip(t.Message(new("App", "FavoritesTitle")))
                                    .MinWidth(40)
                                    .MinHeight(40)
                                    .IsEnabled(!sendMutation.IsPending
                                               && !isResolvingAddress
                                               && Props.Runtime.NodeState == LocalSendNodeState.Running),
                                Button(Icon("\uE71B").AccessibilityHidden(), () =>
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
                                        "\uF22C",
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
            TextDialog().Grid(row: 0, column: 0),
            AddressDialog().Grid(row: 0, column: 0),
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

        Element TextDialog() => (ContentDialog(
                t.Message(new("App", "SendTextTitle")),
                TextBox(text, setText, placeholderText: t.Message(new("App", "SendTextPlaceholder")))
                    .Header(t.Message(new("App", "TextContent")))
                    .AutomationName(t.Message(new("App", "TextContent")))
                    .Required()
                    .AcceptsReturn()
                    .TextWrapping()
                    .MinHeight(160),
                primaryButtonText: t.Message(new("App", "Add"))) with
        {
            IsOpen = showTextDialog,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            OnClosed = result =>
            {
                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(text))
                {
                    var item = new SendTextItem(text);
                    AddSelectedItems((SelectedSendItem[])[
                        new(
                                Guid.NewGuid(),
                                item,
                                t.Message(new("App", "TextMessage")),
                                TextLength(text),
                                "text")
                    ]);
                    setText(string.Empty);
                }

                setShowTextDialog(false);
            },
        }).Themed(Props.Theme);

        Element AddressDialog()
        {
            var hasAddress = !string.IsNullOrWhiteSpace(manualAddress);
            var hasValidFormat = hasAddress && TryParseAddress(manualAddress, out _, out _);
            var validationMessage = manualAddressError
                                    ?? (hasAddress && !hasValidFormat
                                        ? t.Message(new("App", "InvalidDeviceAddress"))
                                        : null);

            return (ContentDialog(
                    t.Message(new("App", "EnterAddressTitle")),
                    VStack(6,
                            TextBox(manualAddress, value =>
                                    {
                                        setManualAddress(value);
                                        if (manualAddressError is not null)
                                            setManualAddressError(null);
                                    },
                                    placeholderText: t.Message(new("App", "AddressPlaceholder")))
                                .AutomationName(t.Message(new("App", "DeviceAddress")))
                                .HelpText(validationMessage ?? t.Message(
                                    new("App", "AddressExample"),
                                    ("address", "192.168.1.100")))
                                .Required()
                                .IsEnabled(!isResolvingAddress),
                            validationMessage is not null
                                ? TextBlock(validationMessage)
                                    .FontSize(14)
                                    .Foreground(Theme.SystemAttention)
                                    .LiveRegion(AutomationLiveSetting.Assertive)
                                    .TextWrapping(TextWrapping.WrapWholeWords)
                                : recentManualAddress is not null
                                    ? HStack(2,
                                        TextBlock(t.Message(new("App", "RecentlyUsedAddress")))
                                            .FontSize(14)
                                            .Foreground(Theme.SecondaryText)
                                            .VAlign(VerticalAlignment.Center),
                                        HyperlinkButton(recentManualAddress, onClick: UseRecentAddress)
                                            .Padding(0, 0)
                                            .FontSize(14)
                                            .VAlign(VerticalAlignment.Center)
                                            .AutomationName(t.Message(
                                                new("App", "UseRecentAddress"),
                                                ("address", recentManualAddress))))
                                    : TextBlock(t.Message(
                                            new("App", "AddressExample"),
                                            ("address", "192.168.1.100")))
                                        .FontSize(14)
                                        .Foreground(Theme.SecondaryText))
                        .MinWidth(340),
                    primaryButtonText: t.Message(new("App", "Confirm"))) with
            {
                IsOpen = showAddressDialog,
                IsPrimaryButtonEnabled = !isResolvingAddress && hasValidFormat,
                SecondaryButtonText = t.Message(new("App", "Cancel")),
                DefaultButton = ContentDialogButton.Primary,
                OnClosed = result =>
                {
                    setShowAddressDialog(false);
                    if (result == ContentDialogResult.Primary)
                        _ = SendToAddressAsync(manualAddress);
                },
            }).Themed(Props.Theme);
        }

        async Task PickFileAsync()
        {
            try
            {
                var files = await storagePicker.PickFilesAsync(t.Message(new("App", "Add")));
                if (files.Count == 0)
                    return;

                var selected = new List<SelectedSendItem>(files.Count);
                foreach (var file in files)
                    selected.Add(await FromStorageFileAsync(file, file.Name, CancellationToken.None));
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

                var selected = await FromFolderAsync(folder, CancellationToken.None);
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
                                selected.Add(await FromStorageFileAsync(file, file.Name, CancellationToken.None));
                                break;
                            case StorageFolder folder:
                                selected.AddRange(await FromFolderAsync(folder, CancellationToken.None));
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
                    var bitmap = await FromClipboardBitmapAsync(data, CancellationToken.None);
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
                            imported.AddRange(await FromFolderPathAsync(
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
                                "file"));
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
                if (result.IsSuccess)
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
            catch (PinRequiredException exception)
            {
                var waitingState = new TransferUiState.Active(
                    TransferState.WaitingForAcceptance,
                    device.Alias,
                    0,
                    selectedItems.Sum(static item => item.Length),
                    t.Message(new("App", "TargetRequiresPin")),
                    exception.InvalidPin);
                dispatchTransfer(new SendTransferAction.PinRequested(waitingState));
                PublishTransferOverlay(
                    device,
                    (SendItem[])[.. selectedItems.Select(static item => item.Item)],
                    waitingState,
                    new OutgoingOverlayUpdate.AwaitingPin(new OutgoingPinPrompt(
                        exception.InvalidPin ? t.Message(new("App", "PinIncorrect")) : null,
                        enteredPin => _ = StartSendAsync(device, enteredPin, resolvedManualAddress),
                        () => dispatchTransfer(new SendTransferAction.Reset(
                            t.Message(new("App", "SendHint")))))));
            }
            catch (PinRateLimitedException)
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
            setManualAddress(recentManualAddress);
            setShowAddressDialog(false);
            _ = SendToAddressAsync(recentManualAddress);
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
                    ? await ResolveDeviceAsync(
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

        static async Task<DeviceResolution> ResolveDeviceAsync(
            LocalSendNode node,
            IPAddress address,
            int port,
            LocalSendProtocol preferredProtocol,
            string? expectedFingerprint)
        {
            Exception? lastError = null;
            LocalSendProtocol[] protocols =
            [
                preferredProtocol,
                preferredProtocol == LocalSendProtocol.Https
                    ? LocalSendProtocol.Http
                    : LocalSendProtocol.Https,
            ];
            foreach (var protocol in protocols)
            {
                try
                {
                    var endpoint = new DeviceEndpoint(address, port, protocol);
                    var probe = await node.ProbeDeviceAsync(endpoint).ConfigureAwait(true);
                    if (expectedFingerprint is not null
                        && !string.Equals(
                            probe.Device.Fingerprint,
                            expectedFingerprint,
                            StringComparison.Ordinal))
                    {
                        throw new LocalSendException(
                            "The saved address now belongs to a different device.");
                    }

                    var device = await node.AddKnownDeviceAsync(
                        endpoint,
                        probe.Device.Fingerprint).ConfigureAwait(true);
                    return new ResolvedDevice(device);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                }
            }

            return new DeviceResolutionFailure(lastError
                ?? new LocalSendException("No compatible device responded."));
        }

        static bool TryParseAddress(string value, out IPAddress address, out int port)
        {
            var input = value.Trim();
            port = LocalSendOptions.DefaultPort;
            if (IPAddress.TryParse(input, out address!))
                return true;

            if (Uri.TryCreate($"tcp://{input}", UriKind.Absolute, out var uri)
                && IPAddress.TryParse(uri.Host, out address!)
                && uri.Port is >= 1 and <= ushort.MaxValue)
            {
                port = uri.Port;
                return true;
            }

            address = IPAddress.None;
            return false;
        }

        static string FormatAddress(IPAddress address, int port)
        {
            var host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? $"[{address}]"
                : address.ToString();
            return port == LocalSendOptions.DefaultPort ? host : $"{host}:{port}";
        }

        async Task AddDroppedItemsAsync(DragData dragData)
        {
            try
            {
                var storageItems = await dragData.GetFilesAsync();
                var selected = new List<SelectedSendItem>();
                foreach (var storageItem in storageItems)
                {
                    if (!IsSafeLocalStorageItem(storageItem))
                        continue;

                    switch (storageItem)
                    {
                        case StorageFile file:
                            selected.Add(await FromStorageFileAsync(file, file.Name, CancellationToken.None));
                            break;
                        case StorageFolder folder:
                            selected.AddRange(await FromFolderAsync(folder, CancellationToken.None));
                            break;
                    }
                }

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

    private static string ContentSummary(IntlAccessor t, IReadOnlyList<SendItem> items)
    {
        return items.Count switch
        {
            1 when items[0] is SendTextItem => t.Message(new("App", "ContentOneTextMessage")),
            1 => t.Message(new("App", "ContentOneFile"), ("file", items[0].FileName)),
            _ => t.Message(new("App", "ContentManyItems"), ("count", items.Count))
        };
    }

    private static string TransferProgressText(long bytesTransferred, long totalBytes) => totalBytes > 0
        ? $"{FormatBytes(bytesTransferred)} / {FormatBytes(totalBytes)}"
        : FormatBytes(bytesTransferred);

    private static Element SelectionTile(string label, string icon, Action onClick, IntlAccessor t) =>
        Button(
                VStack(8,
                    Icon(icon).AccessibilityHidden(),
                    BodyStrong(label)),
                onClick)
            .MinHeight(104)
            .HAlign(HorizontalAlignment.Stretch)
            .AutomationName(t.Message(new("App", "ChooseItem"), ("item", label)));

    private static Element EmptySelection(
        bool isDropActive,
        string pickerMessage,
        IntlAccessor t)
    {
        var nothingSelected = t.Message(new("App", "NothingSelected"));
        var dropText = isDropActive
            ? t.Message(new("App", "ReleaseFilesToAdd"))
            : t.Message(new("App", "DropFilesOrFolders"));

        return (FlexColumn(
                    Image("ms-appx:///Assets/FileDrop.svg")
                        .Size(192, 112)
                        .AccessibilityHidden(),
                    Subtitle(dropText),
                    pickerMessage == nothingSelected
                        ? null
                        : Caption(pickerMessage)
                            .Foreground(Theme.SecondaryText)
                            .TextWrapping(TextWrapping.WrapWholeWords)) with
        {
            RowGap = 12,
            AlignItems = FlexAlign.Center,
            JustifyContent = FlexJustify.Center,
        })
            .MinHeight(280)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch);
    }

    private static bool IsSafeLocalStorageItem(IStorageItem storageItem)
    {
        try
        {
            var path = storageItem.Path;
            if (string.IsNullOrWhiteSpace(path)
                || path.StartsWith(@"\\", StringComparison.Ordinal)
                || !Path.IsPathFullyQualified(path))
            {
                return false;
            }

            var attributes = File.GetAttributes(path);
            return (attributes & System.IO.FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or IOException
                                              or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            AppDiagnostics.Report("Could not inspect a dropped storage item", exception);
            return false;
        }
    }

    private static Element SelectedItemRow(SelectedSendItem item, Action remove, IntlAccessor t) =>
        Grid(
                columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                Icon(ItemIcon(item)).AccessibilityHidden()
                    .VAlign(VerticalAlignment.Center)
                    .Grid(column: 0),
                VStack(2,
                        TextBlock(item.DisplayName)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .ToolTip(item.DisplayName),
                        Caption(t.Message(
                                new("App", "ItemKindAndSize"),
                                ("kind", ItemKindLabel(t, item.Kind)),
                                ("size", FormatBytes(item.Length))))
                            .Foreground(Theme.SecondaryText))
                    .Margin(horizontal: 12, vertical: 0)
                    .Grid(column: 1),
                Button(Icon("Delete").AccessibilityHidden(), remove)
                    .AutomationName(t.Message(new("App", "RemoveItem"), ("item", item.DisplayName)))
                    .ToolTip(t.Message(new("App", "Remove")))
                    .Grid(column: 2))
            .Padding(12)
            .CornerRadius(8)
            .Background(Theme.SubtleFill)
            .WithBorder(Theme.CardStroke);

    private static Element DeviceCard(
        LocalSendDevice device,
        FavoriteDevice? favorite,
        bool isEnabled,
        Action<FrameworkElement?> onClick,
        Action onDetails,
        IntlAccessor t)
    {
        var displayName = favorite?.Name ?? device.Alias;

        return Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
            displayName,
            device.DeviceModel,
            device.DeviceType,
            RemoteDeviceNumber(device),
            DeviceConnectedKey(device.Fingerprint),
            onClick,
            t.Message(new("App", "SendToDevice"), ("device", displayName)),
            isEnabled,
            TrailingReserve: 64,
            AnimationRole: DeviceIdentityCardAnimationRole.Source,
            SecondaryGlyph: "\uE946",
            SecondaryAutomationName: t.Message(
                new("App", "OpenDeviceDetails"),
                ("device", displayName)),
            OnSecondaryClick: _ => onDetails(),
            IsFavorite: favorite is not null));
    }

    private static Element EmptyDevices(
        IntlAccessor t,
        LocalSendNodeState state,
        string? discoveryWarning,
        Element searchingAnimation) =>
        FlexColumn(
                state switch
                {
                    LocalSendNodeState.Faulted => Icon("\uE783").AccessibilityHidden(),
                    _ => searchingAnimation,
                },
                Subtitle(state switch
                {
                    LocalSendNodeState.Faulted => t.Message(new("App", "NetworkStartFailed")),
                    _ => t.Message(new("App", "SearchingDevices")),
                }),
                TextBlock(state switch
                {
                    LocalSendNodeState.Faulted => t.Message(new("App", "PortInUseHint")),
                    _ when discoveryWarning is not null => t.Message(new("App", "DiscoveryScanHint")),
                    _ => t.Message(new("App", "SameNetworkHint")),
                })
                    .Foreground(Theme.SecondaryText)
                    .TextWrapping(TextWrapping.WrapWholeWords)) with
        {
            RowGap = 12,
            AlignItems = FlexAlign.Center,
            JustifyContent = FlexJustify.Center,
        };

    private static void PlaySearchingAnimation(AnimatedVisualPlayer? player, bool play)
    {
        if (player is null)
            return;

        player.Source = new SearchingDevices();
        if (play)
            _ = player.PlayAsync(fromProgress: 0, toProgress: 1, looped: true);
    }

    private static async Task<SelectedSendItem> FromStorageFileAsync(
        StorageFile file,
        string protocolName,
        CancellationToken cancellationToken)
    {
        var properties = await file.GetBasicPropertiesAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var item = new SendStreamItem(
            protocolName.Replace('\\', '/'),
            checked((long)properties.Size),
            async token =>
            {
                token.ThrowIfCancellationRequested();
                return await file.OpenStreamForReadAsync().ConfigureAwait(false);
            });
        return new(Guid.NewGuid(), item, protocolName, checked((long)properties.Size), "file");
    }

    private static Task<IReadOnlyList<SelectedSendItem>> FromFolderAsync(
        StorageFolder folder,
        CancellationToken cancellationToken) => FromFolderPathAsync(folder.Path, cancellationToken);

    private static Task<IReadOnlyList<SelectedSendItem>> FromFolderPathAsync(
        string folderPath,
        CancellationToken cancellationToken) => Task.Run<IReadOnlyList<SelectedSendItem>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new IOException("The selected folder has no accessible local path.");

        var folder = new DirectoryInfo(folderPath);
        if (!folder.Exists)
            throw new DirectoryNotFoundException($"The shared folder is no longer available: {folderPath}");

        return (SelectedSendItem[])
        [
            .. Directory.EnumerateFiles(folder.FullName, "*", SearchOption.AllDirectories)
                .Select(path =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativeName = Path.GetRelativePath(folder.FullName, path).Replace('\\', '/');
                    var protocolName = $"{folder.Name}/{relativeName}";
                    return new SelectedSendItem(
                        Guid.NewGuid(),
                        new SendFileItem(path, protocolName),
                        protocolName,
                        new FileInfo(path).Length,
                        "folder");
                })
        ];
    }, cancellationToken);

    private static async Task<SelectedSendItem> FromClipboardBitmapAsync(
        DataPackageView data,
        CancellationToken cancellationToken)
    {
        var reference = await data.GetBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
        using var probe = await reference.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var length = checked((long)probe.Size);
        var contentType = string.IsNullOrWhiteSpace(probe.ContentType) ? "image/png" : probe.ContentType;
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/bmp" => ".bmp",
            "image/gif" => ".gif",
            _ => ".png",
        };
        var fileName = $"clipboard-image{extension}";
        var item = new SendStreamItem(
            fileName,
            length,
            async token =>
            {
                var stream = await reference.OpenReadAsync().AsTask(token).ConfigureAwait(false);
                return stream.AsStreamForRead();
            },
            contentType);
        return new(Guid.NewGuid(), item, fileName, length, "clipboard");
    }

    private static TransferUiState.Active ResultState(
        IntlAccessor t,
        TransferResult result,
        string deviceAlias,
        long requestedBytes) => result.State switch
        {
            TransferState.Completed => new TransferUiState.Active(
                result.State,
                deviceAlias,
                result.BytesTransferred,
                result.BytesTransferred,
                t.Message(new("App", "SentToDevice"), ("device", deviceAlias)),
                IsError: false),
            TransferState.Cancelled => new TransferUiState.Active(
                result.State,
                deviceAlias,
                result.BytesTransferred,
                requestedBytes,
                t.Message(new("App", "TransferCancelled")),
                IsError: false),
            _ => new TransferUiState.Active(
                result.State,
                deviceAlias,
                result.BytesTransferred,
                requestedBytes,
                result.Failure?.Message ?? t.Message(new("App", "TransferFailed")),
                IsError: true),
        };

    private static string ProgressMessage(IntlAccessor t, TransferState state, string deviceAlias) => state switch
    {
        TransferState.Preparing => t.Message(new("App", "PreparingForDevice"), ("device", deviceAlias)),
        TransferState.WaitingForAcceptance => t.Message(new("App", "WaitingForDevice"), ("device", deviceAlias)),
        TransferState.Transferring => t.Message(new("App", "SendingToDevice"), ("device", deviceAlias)),
        TransferState.Completed => t.Message(new("App", "SentToDevice"), ("device", deviceAlias)),
        TransferState.Cancelled => t.Message(new("App", "TransferCancelled")),
        _ => t.Message(new("App", "TransferFailed")),
    };

    private static string ItemIcon(SelectedSendItem item) => item.Kind switch
    {
        "text" => "Edit",
        "clipboard" => "Paste",
        "folder" => "Folder",
        _ => FileTypeGlyphs.ForFileName(item.DisplayName),
    };

    private static string ItemKindLabel(IntlAccessor t, string kind) => kind switch
    {
        "text" => t.Message(new("App", "Text")),
        "clipboard" => t.Message(new("App", "Clipboard")),
        "folder" => t.Message(new("App", "Folder")),
        _ => t.Message(new("App", "File")),
    };

    private static long TextLength(string value) => System.Text.Encoding.UTF8.GetByteCount(value);
}
