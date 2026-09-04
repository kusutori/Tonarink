using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Navigation;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Tonarink.Components.Animations;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static TransferOverlayVisuals;

sealed record SendPageProps(
    AppRuntimeState Runtime,
    LocalSendNode? Node,
    Func<Task> RefreshAsync,
    Action<OutgoingTransferViewState?> SetTransferOverlay,
    ShareTargetPayload? ShareTargetPayload,
    Action<Guid> ConsumeShareTargetPayload);

sealed record SelectedSendItem(
    Guid Id,
    SendItem Item,
    string DisplayName,
    long Length,
    string Kind);

sealed record SendRequest(
    LocalSendDevice Device,
    IReadOnlyList<SendItem> Items,
    string? Pin,
    CancellationToken CancellationToken);

sealed record TransferUiState(
    TransferState? State,
    string? DeviceName,
    long BytesTransferred,
    long TotalBytes,
    string Message,
    bool IsError)
{
    public static TransferUiState Idle(string message) => new(
        State: null,
        DeviceName: null,
        BytesTransferred: 0,
        TotalBytes: 0,
        Message: message,
        IsError: false);
}

sealed class SendPage : Component<SendPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var window = UseWindow();
        var navigation = UseNavigation<AppRoute>();
        var (selectedItems, updateSelectedItems) = UseReducer<IReadOnlyList<SelectedSendItem>>(
            Array.Empty<SelectedSendItem>());
        var (pickerMessage, setPickerMessage) = UseState(t.Message(new("App", "NothingSelected")));
        var (text, setText) = UseState(string.Empty);
        var (showTextDialog, setShowTextDialog) = UseState(false);
        var (pinTarget, setPinTarget) = UseState<LocalSendDevice?>(null);
        var (pin, setPin) = UseState(string.Empty);
        var (pinError, setPinError) = UseState<string?>(null);
        var favorites = UseExternalStore<IReadOnlyDictionary<string, FavoriteDevice>>(
            listener =>
            {
                FavoriteDeviceStore.Changed += listener;
                return () => FavoriteDeviceStore.Changed -= listener;
            },
            static () => FavoriteDeviceStore.Entries);
        var (favoriteTarget, setFavoriteTarget) = UseState<LocalSendDevice?>(null);
        var (favoriteName, setFavoriteName) = UseState(string.Empty);
        var (favoriteAddress, setFavoriteAddress) = UseState(string.Empty);
        var (favoritePort, setFavoritePort) = UseState(string.Empty);
        var (transfer, updateTransfer) = UseReducer(TransferUiState.Idle(
            t.Message(new("App", "SendHint"))));
        var sendCancellationRef = UseRef<CancellationTokenSource?>(null);
        var searchingPlayerRef = UseRef<AnimatedVisualPlayer?>(null);
        var shareTargetPayloadId = Props.ShareTargetPayload?.Id ?? Guid.Empty;

        UseNavigationLifecycle(onNavigatedTo: _ =>
            PlaySearchingAnimation(searchingPlayerRef.Current));

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
            var progress = new Progress<TransferProgress>(value =>
            {
                var next = new TransferUiState(
                    value.State,
                    request.Device.Alias,
                    value.BytesTransferred,
                    value.TotalBytes,
                    ProgressMessage(t, value.State, request.Device.Alias),
                    IsError: false);
                updateTransfer(_ => next);
                PublishTransferOverlay(request.Device, request.Items, next, isPending: true);
            });

            return await Props.Node!.SendAsync(
                request.Device,
                request.Items,
                new SendOptions { Pin = request.Pin },
                progress,
                linkedCancellation.Token).ConfigureAwait(false);
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
            SelectionTile(t.Message(new("App", "Clipboard")), "Paste", () => _ = AddClipboardAsync(), t)
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

        Element selectedItemsBody = selectedItems.Count == 0
            ? Caption(pickerMessage)
                .Foreground(Theme.SecondaryText)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
            : VStack(8,
                selectedItems.Select(item => SelectedItemRow(
                    item,
                    () => updateSelectedItems(current =>
                        current.Where(candidate => candidate.Id != item.Id).ToArray()),
                    t)
                    .WithKey(item.Id.ToString("N")))
                .ToArray<Element?>());

        var selectedItemsContent = ScrollView(selectedItemsBody)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .VerticalContentAlignment(VerticalAlignment.Stretch)
            .Flex(grow: 1, basis: 0);

        var selectedItemsCard = Card(
                (FlexColumn(
                    FlexRow(
                        BodyStrong(selectedHeader).Flex(grow: 1, basis: 0),
                        selectedItems.Count == 0
                            ? null
                            : Button(t.Message(new("App", "Clear")), () =>
                            {
                                updateSelectedItems(_ => Array.Empty<SelectedSendItem>());
                                setPickerMessage(t.Message(new("App", "NothingSelected")));
                            }).AutomationName(t.Message(new("App", "Clear")))) with
                    {
                        AlignItems = FlexAlign.Center,
                        ColumnGap = 8,
                    },
                    selectedItemsContent) with
                {
                    RowGap = 12,
                }))
            .VAlign(VerticalAlignment.Stretch)
            .Flex(grow: 1, shrink: 1, basis: 320);

        var devices = Props.Runtime.Devices;
        Element deviceBody = devices.Count == 0
            ? EmptyDevices(
                    t,
                    Props.Runtime.NodeState,
                    Props.Runtime.DiscoveryWarning,
                    SearchingDevicesAnimation())
                .VAlign(VerticalAlignment.Stretch)
            : VStack(8,
                devices.Select((device, index) =>
                    DeviceCard(
                        device,
                        favorites.GetValueOrDefault(device.Fingerprint),
                        isEnabled: Props.Node?.State == LocalSendNodeState.Running
                            && !sendMutation.IsPending,
                        onClick: source =>
                        {
                            if (selectedItems.Count == 0)
                            {
                                setPickerMessage(t.Message(new("App", "SelectContentFirst")));
                                return;
                            }

                            if (source is null)
                            {
                                _ = StartSendAsync(device, pin: null);
                                return;
                            }

                            DeviceConnectedAnimation.NavigateToDestination(
                                DeviceConnectedKey(device.Fingerprint),
                                source,
                                () => _ = StartSendAsync(device, pin: null));
                        },
                        onFavorite: () => OpenFavoriteDialog(device),
                        t)
                        .PositionInSet(index + 1, devices.Count)
                        .WithKey(device.Fingerprint))
                .ToArray<Element?>());

        var deviceContent = ScrollView(deviceBody)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .VerticalContentAlignment(VerticalAlignment.Stretch)
            .Flex(grow: 1, basis: 0);

        var nearbyDevicesCard = Card(
                (FlexColumn(
                    FlexRow(
                        BodyStrong(t.Message(new("App", "NearbyDevices")))
                            .Flex(grow: 1, basis: 0),
                        AnimatedButtons.Refresh(
                            t.Message(new("App", "RefreshDevices")),
                            () => _ = Props.RefreshAsync(),
                            isEnabled: !sendMutation.IsPending),
                        Button(Icon("\uE71B"), () =>
                        {
                            if (selectedItems.Count == 0)
                            {
                                setPickerMessage(t.Message(new("App", "SelectContentFirst")));
                                return;
                            }
                            if (Props.Node?.State != LocalSendNodeState.Running)
                                return;
                            WebShareLaunch.Items = selectedItems.Select(static item => item.Item).ToArray();
                            navigation.Navigate(AppRoute.WebShare, AppNavigation.DrillIn);
                        })
                            .AutomationName(t.Message(new("App", "WebShareTitle")))
                            .ToolTip(t.Message(new("App", "WebShareTitle")))
                            .IsEnabled(!sendMutation.IsPending
                                && Props.Runtime.NodeState == LocalSendNodeState.Running)) with
                    {
                        AlignItems = FlexAlign.Center,
                        ColumnGap = 8,
                    },
                    deviceContent) with
                {
                    RowGap = 12,
                }))
            .VAlign(VerticalAlignment.Stretch)
            .Flex(grow: 1, shrink: 1, basis: 320);

        var contentCards = (FlexRow(selectedItemsCard, nearbyDevicesCard) with
        {
            AlignItems = FlexAlign.Stretch,
            AlignContent = FlexAlign.Stretch,
            ColumnGap = 16,
            RowGap = 16,
            Wrap = FlexWrap.Wrap,
        })
            .VAlign(VerticalAlignment.Stretch)
            .Flex(grow: 1, basis: 0);

        var page = (FlexColumn(
            Heading(t.Message(new("App", "SendTitle")))
                .HeadingLevel(AutomationHeadingLevel.Level1),
            VStack(12,
                Subtitle(t.Message(new("App", "ChooseContent")))
                    .HeadingLevel(AutomationHeadingLevel.Level2),
                selectionGrid),
            contentCards,
            TextDialog(),
            PinDialog(),
            FavoriteDialog()) with
        {
            RowGap = 20,
        });

        return Border(page)
            .Padding(36)
            .MaxWidth(1120)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);

        Element SearchingDevicesAnimation() =>
            (AnimatedVisualPlayer() with { AutoPlay = false })
                .Size(144, 144)
                .AccessibilityHidden()
                .OnMountAdd(element =>
                {
                    if (element is not AnimatedVisualPlayer player)
                        return;

                    searchingPlayerRef.Current = player;
                    PlaySearchingAnimation(player);
                })
                .OnUnmountAdd(element =>
                {
                    if (element is AnimatedVisualPlayer player
                        && ReferenceEquals(searchingPlayerRef.Current, player))
                    {
                        searchingPlayerRef.Current = null;
                    }
                });

        Element TextDialog() => ContentDialog(
            t.Message(new("App", "SendTextTitle")),
            TextBox(text, setText, placeholderText: t.Message(new("App", "SendTextPlaceholder")))
                .Header(t.Message(new("App", "TextContent")))
                .AutomationName(t.Message(new("App", "TextContent")))
                .AcceptsReturn()
                .TextWrapping(TextWrapping.Wrap)
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
                    AddSelectedItems([new(
                        Guid.NewGuid(),
                        item,
                        t.Message(new("App", "TextMessage")),
                        TextLength(text),
                        "text")]);
                    setText(string.Empty);
                }
                setShowTextDialog(false);
            },
        };

        Element PinDialog() => ContentDialog(
            t.Message(new("App", "PinRequiredTitle")),
            VStack(8,
                TextBlock(t.Message(
                        new("App", "PinRequiredMessage"),
                        ("device", pinTarget?.Alias ?? t.Message(new("App", "TargetDevice")))))
                    .TextWrapping(TextWrapping.WrapWholeWords),
                PasswordBox(pin, setPin, placeholderText: t.Message(new("App", "PinPlaceholder")))
                    .Header(t.Message(new("App", "Pin")))
                    .AutomationName(t.Message(new("App", "Pin")))
                    .MaxLength(32),
                pinError is null
                    ? null
                    : TextBlock(pinError).Foreground(Theme.SystemCritical)),
            primaryButtonText: t.Message(new("App", "Retry"))) with
        {
            IsOpen = pinTarget is not null,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            OnClosed = result =>
            {
                var target = pinTarget;
                setPinTarget(null);
                if (result == ContentDialogResult.Primary
                    && target is not null
                    && !string.IsNullOrWhiteSpace(pin))
                {
                    var retryPin = pin;
                    setPin(string.Empty);
                    setPinError(null);
                    _ = StartSendAsync(target, retryPin);
                }
                else
                {
                    setPin(string.Empty);
                    setPinError(null);
                }
            },
        };

        Element FavoriteDialog()
        {
            const string addressPlaceholder = "192.168.1.72";
            const string portPlaceholder = "53317";
            var validAddress = IPAddress.TryParse(favoriteAddress, out _);
            var validPort = int.TryParse(favoritePort, out var parsedPort)
                && parsedPort is >= 1 and <= ushort.MaxValue;
            var canSave = favoriteTarget is not null
                && !string.IsNullOrWhiteSpace(favoriteName)
                && validAddress
                && validPort;

            return (ContentDialog(
                favorites.ContainsKey(favoriteTarget?.Fingerprint ?? string.Empty)
                    ? t.Message(new("App", "EditFavoriteTitle"))
                    : t.Message(new("App", "AddFavoriteTitle")),
                VStack(12,
                    FormField(
                        TextBox(favoriteName, setFavoriteName, placeholderText: t.Message(new("App", "DeviceName")))
                            .AutomationName(t.Message(new("App", "FavoriteDeviceName"))),
                        label: t.Message(new("App", "Name")),
                        required: true),
                    FormField(
                        TextBox(favoriteAddress, setFavoriteAddress, placeholderText: addressPlaceholder)
                            .AutomationName(t.Message(new("App", "FavoriteIpAddress"))),
                        label: t.Message(new("App", "IpAddress")),
                        required: true,
                        description: validAddress || string.IsNullOrWhiteSpace(favoriteAddress)
                            ? null
                            : t.Message(new("App", "InvalidIpAddress"))),
                    FormField(
                        TextBox(favoritePort, setFavoritePort, placeholderText: portPlaceholder)
                            .NumericInput()
                            .AutomationName(t.Message(new("App", "FavoritePort"))),
                        label: t.Message(new("App", "Port")),
                        required: true,
                        description: validPort || string.IsNullOrWhiteSpace(favoritePort)
                            ? null
                            : t.Message(new("App", "InvalidPort")))),
                primaryButtonText: t.Message(new("App", "Save"))) with
            {
                IsOpen = favoriteTarget is not null,
                SecondaryButtonText = t.Message(new("App", "Cancel")),
                DefaultButton = ContentDialogButton.Primary,
                OnClosed = result =>
                {
                    var target = favoriteTarget;
                    if (result == ContentDialogResult.Primary && target is not null && canSave)
                    {
                        var savedFavorite = new FavoriteDevice(
                            target.Fingerprint,
                            favoriteName.Trim(),
                            IPAddress.Parse(favoriteAddress).ToString(),
                            parsedPort);
                        FavoriteDeviceStore.Upsert(savedFavorite);
                    }
                    setFavoriteTarget(null);
                },
            }).IsPrimaryButtonEnabled(canSave);
        }

        void OpenFavoriteDialog(LocalSendDevice device)
        {
            if (favorites.TryGetValue(device.Fingerprint, out var favorite))
            {
                setFavoriteName(favorite.Name);
                setFavoriteAddress(favorite.Address);
                setFavoritePort(favorite.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                var endpoint = device.PreferredEndpoint;
                setFavoriteName(device.Alias);
                setFavoriteAddress(endpoint?.Address.ToString() ?? string.Empty);
                setFavoritePort(endpoint?.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "53317");
            }
            setFavoriteTarget(device);
        }

        async Task PickFileAsync()
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.Downloads,
                    CommitButtonText = t.Message(new("App", "Add")),
                };
                picker.FileTypeFilter.Add("*");
                InitializePicker(picker);
                var files = await picker.PickMultipleFilesAsync();
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
                var picker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.Downloads,
                    CommitButtonText = t.Message(new("App", "AddFolder")),
                };
                picker.FileTypeFilter.Add("*");
                InitializePicker(picker);
                var folder = await picker.PickSingleFolderAsync();
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

        void InitializePicker(object picker)
        {
            var nativeWindow = window?.NativeWindow
                ?? throw new InvalidOperationException(t.Message(new("App", "WindowUnavailable")));
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
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
                        AddSelectedItems([new(
                            Guid.NewGuid(),
                            item,
                            t.Message(new("App", "ClipboardText")),
                            TextLength(clipboardText),
                            "clipboard")]);
                        return;
                    }
                }

                if (data.Contains(StandardDataFormats.Bitmap))
                {
                    var bitmap = await FromClipboardBitmapAsync(data, CancellationToken.None);
                    AddSelectedItems([bitmap with
                    {
                        DisplayName = t.Message(new("App", "ClipboardImage")),
                    }]);
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
            updateSelectedItems(current => [.. current, .. newItems]);
            setPickerMessage(t.Message(new("App", "ItemsAdded"), ("count", newItems.Count)));
            updateTransfer(_ => TransferUiState.Idle(t.Message(new("App", "SendHint"))));
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
                Props.ConsumeShareTargetPayload(payload.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                setPickerMessage(t.Message(
                    new("App", "ShareTargetFailed"),
                    ("error", exception.Message)));
                Props.ConsumeShareTargetPayload(payload.Id);
            }
        }

        async Task StartSendAsync(LocalSendDevice device, string? pin)
        {
            if (Props.Node?.State != LocalSendNodeState.Running || selectedItems.Count == 0)
                return;

            var cancellation = new CancellationTokenSource();
            sendCancellationRef.Current?.Dispose();
            sendCancellationRef.Current = cancellation;
            updateTransfer(_ => new(
                TransferState.Preparing,
                device.Alias,
                0,
                selectedItems.Sum(static item => item.Length),
                t.Message(new("App", "PreparingForDevice"), ("device", device.Alias)),
                IsError: false));
            PublishTransferOverlay(
                device,
                selectedItems.Select(static item => item.Item).ToArray(),
                new(
                    TransferState.Preparing,
                    device.Alias,
                    0,
                    selectedItems.Sum(static item => item.Length),
                    t.Message(new("App", "PreparingForDevice"), ("device", device.Alias)),
                    IsError: false),
                isPending: true);

            try
            {
                var result = await sendMutation.RunAsync(new(
                    device,
                    selectedItems.Select(static item => item.Item).ToArray(),
                    pin,
                    cancellation.Token));
                var resultState = ResultState(
                    t,
                    result,
                    device.Alias,
                    selectedItems.Sum(static item => item.Length));
                updateTransfer(_ => resultState);
                PublishTransferOverlay(
                    device,
                    selectedItems.Select(static item => item.Item).ToArray(),
                    resultState,
                    isPending: false);
                if (result.IsSuccess)
                {
                    AppNotificationService.Show(
                        t.Message(new("App", "NotificationSendCompleteTitle")),
                        selectedItems.Count == 1
                            ? t.Message(
                                new("App", "NotificationSendCompleteOne"),
                                ("device", device.Alias))
                            : t.Message(
                                new("App", "NotificationSendCompleteMany"),
                                ("count", selectedItems.Count),
                                ("device", device.Alias)),
                        "send-complete");
                    updateSelectedItems(_ => Array.Empty<SelectedSendItem>());
                    setPickerMessage(t.Message(new("App", "NothingSelected")));
                }
            }
            catch (PinRequiredException exception)
            {
                Props.SetTransferOverlay(null);
                setPinError(exception.InvalidPin ? t.Message(new("App", "PinIncorrect")) : null);
                setPinTarget(device);
                updateTransfer(current => current with
                {
                    State = TransferState.WaitingForAcceptance,
                    Message = t.Message(new("App", "TargetRequiresPin")),
                    IsError = exception.InvalidPin,
                });
            }
            catch (PinRateLimitedException)
            {
                var errorState = new TransferUiState(
                    TransferState.Failed,
                    device.Alias,
                    0,
                    selectedItems.Sum(static item => item.Length),
                    t.Message(new("App", "PinRateLimited")),
                    IsError: true);
                updateTransfer(_ => errorState);
                PublishTransferOverlay(device, selectedItems.Select(static item => item.Item).ToArray(), errorState, false);
            }
            catch (Exception exception)
            {
                var errorState = new TransferUiState(
                    TransferState.Failed,
                    device.Alias,
                    0,
                    selectedItems.Sum(static item => item.Length),
                    exception.Message,
                    IsError: true);
                updateTransfer(_ => errorState);
                PublishTransferOverlay(device, selectedItems.Select(static item => item.Item).ToArray(), errorState, false);
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
            bool isPending)
        {
            Props.SetTransferOverlay(new(
                Props.Runtime.Identity,
                device,
                ContentSummary(t, items),
                state.State ?? TransferState.Preparing,
                state.BytesTransferred,
                state.TotalBytes,
                state.Message,
                isPending,
                state.IsError,
                () =>
                {
                    sendCancellationRef.Current?.Cancel();
                    updateTransfer(current => current with
                    {
                        Message = t.Message(new("App", "CancellingTransfer")),
                    });
                }));
        }
    }

    private static string ContentSummary(IntlAccessor t, IReadOnlyList<SendItem> items)
    {
        if (items.Count == 1 && items[0] is SendTextItem)
            return t.Message(new("App", "ContentOneTextMessage"));
        if (items.Count == 1)
            return t.Message(new("App", "ContentOneFile"), ("file", items[0].FileName));
        return t.Message(new("App", "ContentManyItems"), ("count", items.Count));
    }

    private static Element SelectionTile(string label, string icon, Action onClick, IntlAccessor t) =>
        Button(
            VStack(8,
                Icon(icon).AccessibilityHidden(),
                BodyStrong(label)),
            onClick)
        .MinHeight(104)
        .HAlign(HorizontalAlignment.Stretch)
        .AutomationName(t.Message(new("App", "ChooseItem"), ("item", label)));

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
            Button(Icon("Delete"), remove)
                .AutomationName(t.Message(new("App", "RemoveItem"), ("item", item.DisplayName)))
                .ToolTip(t.Message(new("App", "Remove")))
                .Grid(column: 2))
        .Padding(12)
        .CornerRadius(8)
        .Background(Theme.SubtleFill)
        .WithBorder(Theme.CardStroke, 1);

    private static Element DeviceCard(
        LocalSendDevice device,
        FavoriteDevice? favorite,
        bool isEnabled,
        Action<FrameworkElement?> onClick,
        Action onFavorite,
        IntlAccessor t)
    {
        var displayName = favorite?.Name ?? device.Alias;
        var favoriteName = favorite is null
            ? t.Message(new("App", "FavoriteDevice"), ("device", displayName))
            : t.Message(new("App", "EditFavoriteDevice"), ("device", displayName));

        return Grid(
            columns: [GridSize.Star()],
            rows: [GridSize.Auto],
            Component<DeviceIdentityCard, DeviceIdentityCardProps>(new(
                displayName,
                device.DeviceModel,
                device.DeviceType,
                RemoteDeviceNumber(device),
                DeviceConnectedKey(device.Fingerprint),
                onClick,
                t.Message(new("App", "SendToDevice"), ("device", displayName)),
                isEnabled,
                TrailingReserve: 56,
                AnimationRole: DeviceIdentityCardAnimationRole.Source))
                .Grid(0, 0),
            Button(Icon(favorite is null ? "\uEB51" : "\uEB52"), onFavorite)
                .AutomationName(favoriteName)
                .ToolTip(favorite is null
                    ? t.Message(new("App", "AddFavoriteTitle"))
                    : t.Message(new("App", "EditFavorite")))
                .MinWidth(64)
                .MinHeight(64)
                .Resources(static resources => resources
                    .Set("ButtonBackground", Theme.Ref("SubtleFillColorTransparentBrush"))
                    .Set("ButtonBackgroundPointerOver", Theme.Ref("SubtleFillColorSecondaryBrush"))
                    .Set("ButtonBackgroundPressed", Theme.Ref("SubtleFillColorTertiaryBrush"))
                    .Set("ButtonBorderBrush", Theme.Ref("SubtleFillColorTransparentBrush")))
                .HAlign(HorizontalAlignment.Right)
                .VAlign(VerticalAlignment.Center)
                .Margin(right: 8)
                .Grid(0, 0))
            .MinHeight(104)
            .HAlign(HorizontalAlignment.Stretch);
    }

    private static Element EmptyDevices(
        IntlAccessor t,
        LocalSendNodeState state,
        string? discoveryWarning,
        Element searchingAnimation) =>
        FlexColumn(
            state == LocalSendNodeState.Faulted
                ? Icon("\uE783").AccessibilityHidden()
                : searchingAnimation,
            Subtitle(state == LocalSendNodeState.Faulted
                ? t.Message(new("App", "NetworkStartFailed"))
                : t.Message(new("App", "SearchingDevices"))),
            TextBlock(state == LocalSendNodeState.Faulted
                    ? t.Message(new("App", "PortInUseHint"))
                    : discoveryWarning is not null
                        ? t.Message(new("App", "DiscoveryScanHint"))
                        : t.Message(new("App", "SameNetworkHint")))
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.WrapWholeWords)) with
        {
            RowGap = 12,
            AlignItems = FlexAlign.Center,
            JustifyContent = FlexJustify.Center,
        };

    private static void PlaySearchingAnimation(AnimatedVisualPlayer? player)
    {
        if (player is null)
            return;

        player.Source = new Tonarink.SearchingDevices();
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

            return Directory.EnumerateFiles(folder.FullName, "*", SearchOption.AllDirectories)
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
                .ToArray();
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

    private static TransferUiState ResultState(
        IntlAccessor t,
        TransferResult result,
        string deviceAlias,
        long requestedBytes) => result.State switch
        {
            TransferState.Completed => new(
                result.State,
                deviceAlias,
                result.BytesTransferred,
                result.BytesTransferred,
                t.Message(new("App", "SentToDevice"), ("device", deviceAlias)),
                IsError: false),
            TransferState.Cancelled => new(
                result.State,
                deviceAlias,
                result.BytesTransferred,
                requestedBytes,
                t.Message(new("App", "TransferCancelled")),
                IsError: false),
            _ => new(
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

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
