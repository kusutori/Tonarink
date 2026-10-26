using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.AnimatedReflowPanelElement;
using static Tonarink.Components.Transfers.TransferOverlayVisuals;
using static Tonarink.Pages.Send.SelectedSendItemOperations;
using static Tonarink.Pages.Send.SendPageVisuals;
using static Tonarink.Services.Devices.SendDeviceResolver;

namespace Tonarink.Pages.Send;

sealed class SendPage : Component<SendPageProps>
{
    public override Element Render() => RenderEachTime(context =>
    {
        var t = context.UseIntl();
        var window = context.UseWindow();
        var storagePicker = new StoragePicker(
            window?.NativeWindow,
            t.Message(new("App", "WindowUnavailable")));
        var reduceMotion = context.UseReducedMotion();
        var (observedWindowWidth, _) = context.UseWindowSize();
        var currentWindowWidth = window?.NativeWindow.Bounds.Width ?? observedWindowWidth;
        var isWideLayout = currentWindowWidth >= AppLayout.WideBreakpoint;
        var (_, refreshCachedPage) = context.UseReducer(0);
        var navigation = context.UseNavigation<AppRoute>();
        var selectedItems = Props.SelectedItems;
        var updateSelectedItems = Props.UpdateSelectedItems;
        var (pickerMessage, setPickerMessage) = context.UseState(t.Message(new("App", "NothingSelected")));
        var (text, setText) = context.UseState(string.Empty);
        var (showTextDialog, setShowTextDialog) = context.UseState(false);
        var (showAddressDialog, setShowAddressDialog) = context.UseState(false);
        var (renameItemId, setRenameItemId) = context.UseState<Guid?>(null);
        var (renameFileName, setRenameFileName) = context.UseState(string.Empty);
        var (showFavoritesDialog, setShowFavoritesDialog) = context.UseState(false);
        var (favoriteEdit, setFavoriteEdit) = context.UseState<FavoriteDeviceEdit?>(null);
        var (favoriteToDelete, setFavoriteToDelete) = context.UseState<FavoriteDevice?>(null);
        var (deviceFavoriteDraft, setDeviceFavoriteDraft) = context.UseState<FavoriteDevice?>(null);
        var (deviceFavoriteToDelete, setDeviceFavoriteToDelete) = context.UseState<FavoriteDevice?>(null);
        var (verificationDevice, setVerificationDevice) = context.UseState<LocalSendDevice?>(null);
        var (manualAddress, setManualAddress) = context.UseState(string.Empty);
        var (manualAddressError, setManualAddressError) = context.UseState<string?>(null);
        var (isResolvingAddress, setResolvingAddress) = context.UseState(false);
        var (recentManualAddress, setRecentManualAddress) = context.UseState(RecentManualAddressStore.Load());
        var favorites = context.UseExternalStore(
            listener =>
            {
                FavoriteDeviceStore.Changed += listener;
                return () => FavoriteDeviceStore.Changed -= listener;
            },
            static () => FavoriteDeviceStore.Entries);
        var searchingPlayerRef = context.UseRef<AnimatedVisualPlayer?>();
        var shareSource = context.UseRef<WindowsShareSource?>();
        var transfer = context.UseSendTransfer(
            Props.Node,
            Props.Runtime,
            selectedItems,
            updateSelectedItems,
            Props.KeepItemsForMultipleReceivers,
            Props.VerifyChecksums,
            Props.SetTransferOverlay,
            setPickerMessage,
            setRecentManualAddress,
            t);
        var enqueueFavoriteShare = context.UseFavoriteShareSend(Props.Node, Props.Runtime, transfer,
            t, setPickerMessage, setResolvingAddress);
        var itemSelection = context.UseSendItemSelection(
            storagePicker,
            pickerMessage,
            setPickerMessage,
            Props.ShareTargetPayload,
            Props.ConsumeShareTargetPayload,
            updateSelectedItems,
            enqueueFavoriteShare,
            t);

        context.UseEffect(() => () =>
        {
            shareSource.Current?.Dispose();
            shareSource.Current = null;
        });

        // A cached page keeps its previous hook state while it is outside the active tree.
        // Reconcile it before the transition and read the live window bounds so a resize
        // performed on another page is reflected immediately when Send becomes visible.
        context.UseNavigationLifecycle(
            onNavigatingTo: _ => refreshCachedPage(value => value + 1),
            onNavigatedTo: _ =>
                PlaySearchingAnimation(searchingPlayerRef.Current, play: !reduceMotion));

        var previousNavigationFavorite = context.UseRef<string?>();
        context.UseEffect(() =>
        {
            if (Props.NavigationFavoriteFingerprint is not null)
                setShowFavoritesDialog(true);
            else if (previousNavigationFavorite.Current is not null)
                setShowFavoritesDialog(false);
            previousNavigationFavorite.Current = Props.NavigationFavoriteFingerprint;
        }, Props.NavigationFavoriteFingerprint);

        // Clipboard APIs require the UI/STA thread. Use a synchronous command dispatcher
        // to start the existing async flow there; an ExecuteAsync command is intentionally
        // avoided because UseCommand runs async command bodies on the thread pool.
        var pasteCommand = context.UseCommand(StandardCommand.Paste(() =>
            {
                _ = itemSelection.AddClipboardAsync();
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
                AnimatedButtons.FileSelection(
                        t.Message(new("App", "File")),
                        t.Message(
                            new("App", "ChooseItem"),
                            ("item", t.Message(new("App", "File")))),
                        () => _ = itemSelection.PickFileAsync())
                    .Grid(column: 0),
                AnimatedButtons.FolderSelection(
                        t.Message(new("App", "Folder")),
                        t.Message(
                            new("App", "ChooseItem"),
                            ("item", t.Message(new("App", "Folder")))),
                        () => _ = itemSelection.PickFolderAsync())
                    .Grid(column: 1),
                AnimatedButtons.TextSelection(
                        t.Message(new("App", "Text")),
                        t.Message(
                            new("App", "ChooseItem"),
                            ("item", t.Message(new("App", "Text")))),
                        () => setShowTextDialog(true))
                    .Grid(column: 2),
                AnimatedButtons.ClipboardSelection(
                        t.Message(new("App", "Clipboard")),
                        t.Message(
                            new("App", "ChooseItem"),
                            ("item", t.Message(new("App", "Clipboard")))),
                        () => pasteCommand.Execute?.Invoke(),
                        pasteCommand.IsEnabled)
                    .Grid(column: 3)) with
        {
            ColumnSpacing = 12,
        };

        Element selectedItemsCard = Component<SendItemsCard, SendItemsCardProps>(new(
                selectedItems,
                updateSelectedItems,
                pickerMessage,
                setPickerMessage,
                isWideLayout,
                Props.ExpandDragDropToEntireApp,
                Props.FilePreviewEnabled,
                Props.PreviewProvider,
                Props.PreviewExecutablePath,
                window?.NativeWindow is not null,
                ShareItemAsync,
                item =>
                {
                    setRenameFileName(ProtocolLeafName(item.Item.FileName));
                    setRenameItemId(item.Id);
                },
                itemSelection.AddDroppedItemsAsync))
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch);

        Element nearbyDevicesCard = Component<SendDevicesCard, SendDevicesCardProps>(new(
                Props.Runtime,
                favorites,
                isWideLayout,
                transfer.IsSending,
                isResolvingAddress,
                showAddressDialog,
                showFavoritesDialog,
                Props.KeepItemsForMultipleReceivers,
                Props.SetKeepItemsForMultipleReceivers,
                SearchingDevicesAnimation(),
                Props.RefreshAsync,
                OpenAddressDialog,
                OpenFavoritesDialog,
                () =>
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
                },
                (device, source) =>
                {
                    if (selectedItems.Count == 0)
                    {
                        setPickerMessage(t.Message(new("App", "SelectContentFirst")));
                        return;
                    }

                    if (source is null || reduceMotion)
                    {
                        _ = transfer.StartAsync(device, null, null);
                        return;
                    }

                    DeviceConnectedAnimation.NavigateToDestination(
                        DeviceConnectedKey(device.Fingerprint),
                        source,
                        () => _ = transfer.StartAsync(device, null, null));
                },
                Props.OpenDeviceDetails,
                (device, favorite) =>
                {
                    if (favorite is null)
                        setDeviceFavoriteDraft(CreateFavorite(device));
                    else
                        setDeviceFavoriteToDelete(favorite);
                },
                device => setVerificationDevice(device)))
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch);

        // Stable native slots let Composition animate the same cards across breakpoints.
        Element contentCards = (AnimatedReflowPanel(children:
            [
                Border(selectedItemsCard).WithKey("selected-items"),
                Border(nearbyDevicesCard).WithKey("nearby-devices"),
            ]) with
        {
            Orientation = isWideLayout ? Orientation.Horizontal : Orientation.Vertical,
            Spacing = 16,
            AnimationsEnabled = !reduceMotion,
        }).HAlign(HorizontalAlignment.Stretch);
        if (isWideLayout)
            contentCards = contentCards.VAlign(VerticalAlignment.Stretch).Flex(grow: 1, basis: 0);

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
                        itemSelection.Add((SelectedSendItem[])
                        [
                            new(
                                Guid.NewGuid(),
                                item,
                                t.Message(new("App", "TextMessage")),
                                TextLength(value),
                                SelectedSendItemKind.Text)
                        ]);
                    },
                    () => setShowTextDialog(false)))
                .Grid(row: 0, column: 0),
            Component<SendAddressDialog, SendAddressDialogProps>(new(
                    Props.Theme,
                    showAddressDialog,
                    manualAddress,
                    setManualAddress,
                    value => TryParseAddress(value, out _, out _),
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
                    Props.NavigationFavoriteFingerprint,
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
                        if (Props.NavigationFavoriteFingerprint is { } fingerprint)
                            Props.ConsumeNavigationFavorite(fingerprint);
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

        // Keep the scroll host mounted in both modes; replacing it would remount the cards.
        return (ScrollView(page) with
            {
                VerticalScrollBarVisibility = isWideLayout ? ScrollingScrollBarVisibility.Hidden : ScrollingScrollBarVisibility.Auto,
            })
            .ContentOrientation(isWideLayout ? ScrollingContentOrientation.None : ScrollingContentOrientation.Vertical)
            .VerticalScrollMode(isWideLayout ? ScrollingScrollMode.Disabled : ScrollingScrollMode.Auto)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .VerticalContentAlignment(isWideLayout ? VerticalAlignment.Stretch : VerticalAlignment.Top);

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

            enqueueFavoriteShare(new(Guid.NewGuid(), favorite.Fingerprint, selectedItems.ToArray()));
        }

        void UseRecentAddress()
        {
            if (recentManualAddress is not { } address)
                return;

            setManualAddress(address);
            setShowAddressDialog(false);
            _ = SendToAddressAsync(address);
        }

        async Task SendToAddressAsync(string value)
        {
            if (!TryParseAddress(value, out var address, out var port))
            {
                setManualAddressError(t.Message(new("App", "InvalidDeviceAddress")));
                setShowAddressDialog(true);
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
                            expectedFingerprint: null)
                        .ConfigureAwait(true)
                    : new DeviceResolutionFailure(new InvalidOperationException(
                        "The LocalSend node is not running."));
                var resolvedDevice = resolution switch
                {
                    ResolvedDevice(var device) => device,
                    DeviceResolutionFailure => null,
                };
                if (resolvedDevice is not null)
                {
                    await transfer.StartAsync(resolvedDevice, null, normalizedAddress).ConfigureAwait(true);
                    return;
                }

                setManualAddressError(t.Message(
                    new("App", "DeviceAddressNotFound"),
                    ("address", normalizedAddress)));
                setShowAddressDialog(true);
            }
            finally
            {
                setResolvingAddress(false);
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
    });
}
