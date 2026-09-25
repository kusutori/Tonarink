using CommunityToolkit.WinUI.Controls;
using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using static Microsoft.UI.Reactor.Factories;
using Tonarink.Components.Animations;
using static Tonarink.Components.Devices.DeviceVisuals;
using static Tonarink.Controls.SegmentedElement;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Components.Tray;

sealed class TrayFlyoutRoot : Component
{
    public override Element Render()
    {
        var snapshot = UseExternalStore(
            static listener =>
            {
                TrayFlyoutStore.Changed += listener;
                return () => TrayFlyoutStore.Changed -= listener;
            },
            static () => TrayFlyoutStore.Snapshot);
        var locale = AppLocale.Resolve(snapshot.Settings.LanguageIndex);
        var theme = AppTheme.ToElementTheme(snapshot.Settings.ThemeIndex);

        return LocaleProvider(
                locale,
                Component<TrayFlyoutPanel, TrayFlyoutPanelProps>(new(snapshot)),
                AppShell.Resources,
                defaultLocale: "en-US")
            .RequestedTheme(theme)
            .Backdrop(BackdropKind.Mica);
    }
}

sealed record TrayFlyoutPanelProps(TrayFlyoutSnapshot Snapshot);

sealed class TrayFlyoutPanel : Component<TrayFlyoutPanelProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var highContrast = UseHighContrast();
        var (isPinned, setPinned) = UseState(TrayFlyoutHost.IsPinned);
        var snapshot = Props.Snapshot;
        var runtime = snapshot.Runtime;
        var statusText = StatusText(t, runtime.NodeState, runtime.DiscoveryWarning);
        var statusColor = runtime.Error is not null || runtime.NodeState == LocalSendNodeState.Faulted
            ? Theme.SystemCritical
            : runtime.DiscoveryWarning is not null
                ? Theme.SystemCaution
                : runtime.NodeState == LocalSendNodeState.Running
                    ? Theme.SystemSuccess
                    : runtime.NodeState is LocalSendNodeState.Starting or LocalSendNodeState.Stopping
                        ? Theme.SystemAttention
                        : Theme.SecondaryText;
        var transfer = ResolveTransfer(t, snapshot);

        var body = FlexColumn(
                Header(t, statusText, statusColor, snapshot, isPinned, pinned =>
                {
                    TrayFlyoutHost.SetPinned(pinned);
                    setPinned(pinned);
                }),
                (transfer is null
                    ? Border(null).Height(0)
                    : TransferCard(transfer, t))
                    .WithKey("tray-transfer"),
                Component<Lists, TrayFlyoutListsProps>(new(snapshot))
                    .WithKey("tray-lists")
                    .Flex(grow: 1, basis: 0),
                Button(t.Message(new("App", "TrayOpen")), OpenApp)
                    .HAlign(HorizontalAlignment.Stretch)
                    .AutomationName(t.Message(new("App", "TrayOpen")))) with
        {
            RowGap = 12,
        };

        return Border(body)
            .Padding(16)
            .Background(Theme.LayerFill)
            .WithBorder(
                highContrast ? Theme.Ref("SystemColorButtonTextColorBrush") : Theme.Ref("SurfaceStrokeColorFlyoutBrush"),
                highContrast ? 2 : 1)
            .CornerRadius(8)
            .AutomationName("Tonarink")
            .Landmark(AutomationLandmarkType.Main);

        void OpenApp()
        {
            TrayFlyoutHost.Dismiss();
            TrayFlyoutStore.Restore();
        }
    }

    private static Element Header(
        IntlAccessor t,
        string statusText,
        ThemeRef statusColor,
        TrayFlyoutSnapshot snapshot,
        bool isPinned,
        Action<bool> setPinned) =>
        (FlexRow(
                VStack(2,
                        SubHeading("Tonarink")
                            .HeadingLevel(AutomationHeadingLevel.Level1),
                        HStack(8,
                                StatusDot(statusColor),
                                Caption(statusText)
                                    .Foreground(Theme.SecondaryText)
                                    .LiveRegion(AutomationLiveSetting.Polite)
                                    .TextWrapping(TextWrapping.NoWrap)
                                    .TextTrimming(TextTrimming.CharacterEllipsis)
                                    .ToolTip(statusText)))
                    .Flex(grow: 1, basis: 0),
                ToggleButton(
                        snapshot.ServerDesired ? "\uE768" : "\uE71A",
                        snapshot.ServerDesired,
                        on =>
                        {
                            if (on)
                                TrayFlyoutStore.StartServer();
                            else
                                TrayFlyoutStore.StopServer();
                        })
                    .FontFamily("Segoe Fluent Icons")
                    .FontSize(16)
                    .MinWidth(40)
                    .MinHeight(40)
                    .AutomationName(t.Message(new("App", "TrayReceiveService")))
                    .ToolTip(snapshot.ServerDesired
                        ? t.Message(new("App", "TrayReceiveOn"))
                        : t.Message(new("App", "TrayReceiveOff"))),
                ToggleButton(
                        isPinned ? "\uE77A" : "\uE718",
                        isPinned,
                        setPinned)
                    .FontFamily("Segoe Fluent Icons")
                    .FontSize(16)
                    .MinWidth(40)
                    .MinHeight(40)
                    .AutomationName(t.Message(new("App", isPinned ? "TrayUnpinPanel" : "TrayPinPanel")))
                    .ToolTip(t.Message(new("App", isPinned ? "TrayUnpinPanel" : "TrayPinPanel")))) with
        {
            AlignItems = FlexAlign.Center,
            ColumnGap = 12,
        });

    private static Element NearbyList(
        AppRuntimeState runtime,
        IntlAccessor t,
        string? dropTargetFingerprint,
        bool isSending,
        Action<string?> setDropTarget,
        Action<LocalSendDevice, Microsoft.UI.Reactor.Input.DragData> sendDropped)
    {
        if (runtime.NodeState == LocalSendNodeState.Faulted)
        {
            return Caption(t.Message(new("App", "NetworkStartFailed")))
                .Foreground(Theme.SystemCritical)
                .TextWrapping(TextWrapping.WrapWholeWords);
        }

        if (runtime.Devices.Count == 0)
        {
            return Caption(
                    runtime.NodeState == LocalSendNodeState.Running
                        ? t.Message(new("App", "SearchingDevices"))
                        : t.Message(new("App", "TrayNoDevices")))
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.WrapWholeWords);
        }

        return VStack(4, [
            .. runtime.Devices.Take(8).Select((device, index) =>
                DeviceRow(
                        device,
                        t,
                        string.Equals(dropTargetFingerprint, device.Fingerprint, StringComparison.Ordinal),
                        isSending,
                        setDropTarget,
                        sendDropped)
                    .PositionInSet(index + 1, runtime.Devices.Count)
                    .WithKey(device.Fingerprint))
        ]).HAlign(HorizontalAlignment.Stretch);
    }

    private static Element DeviceRow(
        LocalSendDevice device,
        IntlAccessor t,
        bool isDropTarget,
        bool isSending,
        Action<string?> setDropTarget,
        Action<LocalSendDevice, Microsoft.UI.Reactor.Input.DragData> sendDropped)
    {
        var row = Button(
                Grid(
                    columns: [GridSize.Auto, GridSize.Star()],
                    rows: [GridSize.Auto],
                    DeviceAvatar(device.DeviceType, 32)
                        .Grid(column: 0),
                    VStack(2,
                            TextBlock(device.Alias)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .ToolTip(device.Alias),
                            Caption(DeviceModel(t, device.DeviceModel, device.DeviceType))
                                .Foreground(Theme.SecondaryText)
                                .TextTrimming(TextTrimming.CharacterEllipsis))
                        .Margin(left: 12)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 1)),
                () =>
                {
                    TrayFlyoutHost.Dismiss();
                    TrayFlyoutStore.Restore();
                })
            .HAlign(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .SubtleButton()
            .AutomationName(device.Alias)
            .OnDragEnter(args =>
            {
                if (isSending || !args.Data.HasFormat(StandardDataFormats.StorageItems))
                    return;

                args.AcceptedOperation = DragOperations.Copy;
                setDropTarget(device.Fingerprint);
            })
            .OnDragOver(args =>
            {
                if (isSending || !args.Data.HasFormat(StandardDataFormats.StorageItems))
                    return;

                args.AcceptedOperation = DragOperations.Copy;
                args.UIOverride.Caption = t.Message(
                    new("App", "TrayDropToSend"),
                    ("device", device.Alias));
                args.UIOverride.IsCaptionVisible = true;
                args.UIOverride.IsGlyphVisible = true;
            })
            .OnDragLeave(_ => setDropTarget(null))
            .OnDrop(args =>
            {
                setDropTarget(null);
                if (isSending || !args.Data.HasFormat(StandardDataFormats.StorageItems))
                    return;

                args.AcceptedOperation = DragOperations.Copy;
                sendDropped(device, args.Data);
            }, acceptedOps: DragOperations.Copy);

        return isDropTarget
            ? row.Background(Theme.SystemAttentionBackground).WithBorder(Theme.SystemAttention)
            : row;
    }

    private static Element HistoryList(IReadOnlyList<ReceiveHistoryEntry> entries, IntlAccessor t)
    {
        if (entries.Count == 0)
        {
            return Caption(t.Message(new("App", "HistoryEmpty")))
                .Foreground(Theme.SecondaryText)
                .TextWrapping(TextWrapping.WrapWholeWords);
        }

        return VStack(4, [
            .. entries.Take(8).Select((entry, index) =>
                HistoryRow(entry, t)
                    .PositionInSet(index + 1, entries.Count)
                    .WithKey(entry.Id.ToString("N")))
        ]).HAlign(HorizontalAlignment.Stretch);
    }

    private static Element HistoryRow(ReceiveHistoryEntry entry, IntlAccessor t)
    {
        var when = entry.ReceivedAt.ToLocalTime().ToString("MM-dd HH:mm");
        var detail = $"{entry.SenderAlias}  ·  {when}";
        return Button(
                Grid(
                    columns: [GridSize.Auto, GridSize.Star()],
                    rows: [GridSize.Auto],
                    Border(Icon(FileTypeGlyphs.ForFileName(entry.Path)).AccessibilityHidden())
                        .Size(32, 32)
                        .CornerRadius(16)
                        .Background(Theme.SubtleFill)
                        .Grid(column: 0),
                    VStack(2,
                            TextBlock(entry.FileName)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .ToolTip(entry.FileName),
                            Caption(detail)
                                .Foreground(Theme.SecondaryText)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .ToolTip(detail))
                        .Margin(left: 12)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 1)),
                () =>
                {
                    if (File.Exists(entry.Path) || Directory.Exists(entry.Path))
                        ShellLauncher.Open(entry.Path);
                    else
                    {
                        TrayFlyoutHost.Dismiss();
                        TrayFlyoutStore.Restore();
                    }
                })
            .HAlign(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .SubtleButton()
            .AutomationName(entry.FileName);
    }

    private static Element TransferCard(TrayFlyoutTransfer transfer, IntlAccessor t)
    {
        var progress = transfer.TotalBytes <= 0
            ? 0
            : Math.Clamp(transfer.BytesTransferred * 100d / transfer.TotalBytes, 0, 100);
        return Button(
                VStack(8,
                    BodyStrong(transfer.Title)
                        .TextTrimming(TextTrimming.CharacterEllipsis)
                        .ToolTip(transfer.Title),
                    Caption(transfer.Peer)
                        .Foreground(Theme.SecondaryText)
                        .TextTrimming(TextTrimming.CharacterEllipsis),
                    Caption(transfer.Status)
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.WrapWholeWords),
                    transfer.Indeterminate
                        ? ProgressIndeterminate().AutomationName(transfer.Status)
                        : transfer.TotalBytes > 0
                            ? Progress(progress).AutomationName(transfer.Status)
                            : null,
                    transfer.TotalBytes > 0 && !transfer.Indeterminate
                        ? Caption($"{FormatBytes(transfer.BytesTransferred)} / {FormatBytes(transfer.TotalBytes)}")
                            .Foreground(Theme.TertiaryText)
                        : null),
                () =>
                {
                    TrayFlyoutHost.Dismiss();
                    TrayFlyoutStore.Restore();
                })
            .Padding(16)
            .CornerRadius(8)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke)
            .HAlign(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName($"{transfer.Title}, {transfer.Status}");
    }

    private static TrayFlyoutTransfer? ResolveTransfer(IntlAccessor t, TrayFlyoutSnapshot snapshot)
    {
        if (snapshot.IncomingProgress is { } incoming
            && !string.IsNullOrWhiteSpace(incoming.Title))
        {
            return new(
                incoming.Title,
                t.Message(new("App", "TrayFromPeer"), ("peer", incoming.Peer)),
                incoming.Status,
                incoming.BytesTransferred,
                incoming.TotalBytes,
                incoming.Indeterminate);
        }

        var offer = snapshot.Runtime.IncomingTransfers.FirstOrDefault();
        if (offer is not null)
        {
            var title = offer.Items.Count == 1
                ? offer.Items[0].FileName
                : t.Message(new("App", "PendingRequests"), ("count", offer.Items.Count));
            return new(
                title,
                t.Message(new("App", "TrayFromPeer"), ("peer", offer.Sender.Alias)),
                t.Message(new("App", "TrayWaitingReceive")),
                0,
                offer.Items.Sum(static item => item.Size),
                Indeterminate: true);
        }

        if (snapshot.Outgoing is { IsPending: true } outgoing)
        {
            return new(
                outgoing.ContentSummary,
                t.Message(new("App", "TrayToPeer"), ("peer", outgoing.Receiver.Alias)),
                outgoing.Status,
                outgoing.BytesTransferred,
                outgoing.TotalBytes,
                outgoing.TotalBytes <= 0
                || outgoing.State is TransferState.Preparing or TransferState.WaitingForAcceptance);
        }

        return null;
    }

    private static Element StatusDot(ThemeRef color) => Border(null)
        .Size(8, 8)
        .CornerRadius(4)
        .Background(color)
        .VAlign(VerticalAlignment.Center)
        .AccessibilityHidden();

    private static string StatusText(
        IntlAccessor t,
        LocalSendNodeState state,
        string? discoveryWarning) => state switch
        {
            LocalSendNodeState.Starting => t.Message(new("App", "NodeStarting")),
            LocalSendNodeState.Running when discoveryWarning is not null =>
                t.Message(new("App", "NodeDiscoveryLimited")),
            LocalSendNodeState.Running => t.Message(new("App", "NodeRunning")),
            LocalSendNodeState.Faulted => t.Message(new("App", "NodeFaulted")),
            LocalSendNodeState.Stopping => t.Message(new("App", "NodeStopping")),
            _ => t.Message(new("App", "NodeDisconnected")),
        };

    sealed class Lists : Component<TrayFlyoutListsProps>
    {
        public override Element Render()
        {
            var t = UseIntl();
            var theme = AppTheme.ToElementTheme(Props.Snapshot.Settings.ThemeIndex);
            var runtime = Props.Snapshot.Runtime;
            var history = UseExternalStore(
                static listener =>
                {
                    ReceiveHistoryStore.Changed += listener;
                    return () => ReceiveHistoryStore.Changed -= listener;
                },
                static () => ReceiveHistoryStore.Entries);
            var (tab, setTab) = UseState(0);
            var (dropTargetFingerprint, setDropTargetFingerprint) = UseState<string?>(null);
            var (isSending, setSending) = UseState(false);
            var (dropStatus, setDropStatus) = UseState<TrayDropStatus?>(null);
            var (pendingPin, setPendingPin) = UseState<TrayPendingSend?>(null);
            var (pin, setPin) = UseState(string.Empty);
            var nearby = t.Message(new("App", "TrayNearbyTab"));
            var historyTab = t.Message(new("App", "TrayHistoryTab"));
            var tabItems = UseMemo(
                () => new object[]
                {
                    new SegmentedItem { Content = nearby },
                    new SegmentedItem { Content = historyTab },
                },
                nearby,
                historyTab);

            var nearbyContent = ScrollView(
                    NearbyList(
                    runtime,
                    t,
                    dropTargetFingerprint,
                    isSending,
                    setDropTargetFingerprint,
                    (device, data) => _ = SendDroppedAsync(device, data)))
                .HorizontalContentAlignment(HorizontalAlignment.Stretch);
            var historyContent = ScrollView(HistoryList(history, t))
                .HorizontalContentAlignment(HorizontalAlignment.Stretch);

            var content = FlexColumn(
                    Segmented(
                            selectedIndex: tab,
                            onSelectedIndexChanged: setTab,
                            items: tabItems)
                        .HAlign(HorizontalAlignment.Stretch)
                        .WithKey("tray-segmented"),
                    dropStatus is not { } status
                        ? null
                        : Caption(status.Message)
                            .Foreground(status.IsError ? Theme.SystemCritical : Theme.SecondaryText)
                            .LiveRegion(status.IsError
                                ? AutomationLiveSetting.Assertive
                                : AutomationLiveSetting.Polite)
                            .TextWrapping(TextWrapping.WrapWholeWords),
                    Component<SegmentedContentSwitcher, SegmentedContentSwitcherProps>(
                            new(tab, nearbyContent, historyContent))
                        .Flex(grow: 1, basis: 0)) with
            {
                RowGap = 12,
            };

            return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                content.Grid(row: 0, column: 0),
                PinDialog().Grid(row: 0, column: 0));

            Element PinDialog()
            {
                var pending = pendingPin;
                return (ContentDialog(
                        t.Message(new("App", "PinRequiredTitle")),
                        VStack(8,
                            TextBlock(t.Message(
                                    new("App", "PinRequiredMessage"),
                                    ("device", pending?.Device.Alias ?? string.Empty)))
                                .TextWrapping(TextWrapping.WrapWholeWords),
                            PasswordBox(pin, setPin, placeholderText: t.Message(new("App", "PinPlaceholder")))
                                .Header(t.Message(new("App", "Pin")))
                                .AutomationName(t.Message(new("App", "Pin")))
                                .Required()
                                .MaxLength(32),
                            pending?.Error is null
                                ? null
                                : TextBlock(pending.Error)
                                    .Foreground(Theme.SystemCritical)
                                    .LiveRegion(AutomationLiveSetting.Assertive)),
                        primaryButtonText: t.Message(new("App", "PinConfirm"))) with
                {
                    IsOpen = pending is not null,
                    SecondaryButtonText = t.Message(new("App", "Cancel")),
                    DefaultButton = ContentDialogButton.Primary,
                    IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(pin),
                    OnClosed = result =>
                    {
                        var request = pendingPin;
                        var submittedPin = pin.Trim();
                        setPendingPin(null);
                        setPin(string.Empty);
                        if (result == ContentDialogResult.Primary
                            && request is not null
                            && submittedPin.Length > 0)
                        {
                            _ = SendPayloadAsync(request.Device, request.Payload, submittedPin);
                        }
                    },
                }).Themed(theme);
            }

            async Task SendDroppedAsync(
                LocalSendDevice device,
                Microsoft.UI.Reactor.Input.DragData data)
            {
                try
                {
                    setDropStatus(null);
                    var payload = await DroppedSendItemReader.ReadAsync(data);
                    if (payload.Items.Count == 0)
                    {
                        setDropStatus(new TrayDropStatus.Error(
                            t.Message(new("App", "DroppedItemsEmpty"))));
                        return;
                    }

                    await SendPayloadAsync(device, payload, pin: null);
                }
                catch (Exception exception)
                {
                    AppDiagnostics.Report("Could not prepare files dropped on a tray device", exception);
                    setDropStatus(new TrayDropStatus.Error(t.Message(
                        new("App", "DropItemsFailed"),
                        ("error", exception.Message))));
                }
            }

            async Task SendPayloadAsync(
                LocalSendDevice device,
                DroppedSendPayload payload,
                string? pin)
            {
                setSending(true);
                setDropStatus(new TrayDropStatus.Information(
                    t.Message(new("App", "SendingToDevice"), ("device", device.Alias))));
                try
                {
                    var result = await TrayFlyoutStore.SendAsync(new(
                        device,
                        payload.Items,
                        payload.TotalBytes,
                        pin));
                    switch (result)
                    {
                        case OutgoingTransferResult.Completed:
                            setDropStatus(new TrayDropStatus.Information(
                                t.Message(new("App", "SentToDevice"), ("device", device.Alias))));
                            break;
                        case OutgoingTransferResult.PinRequired required:
                            setDropStatus(null);
                            setPendingPin(new(
                                device,
                                payload,
                                required.InvalidPin ? t.Message(new("App", "PinIncorrect")) : null));
                            break;
                        case OutgoingTransferResult.PinRateLimited:
                            setDropStatus(new TrayDropStatus.Error(
                                t.Message(new("App", "PinRateLimited"))));
                            break;
                        case OutgoingTransferResult.Cancelled:
                            setDropStatus(new TrayDropStatus.Information(
                                t.Message(new("App", "TransferCancelled"))));
                            break;
                        case OutgoingTransferResult.Failed failed:
                            setDropStatus(new TrayDropStatus.Error(failed.Failure.Message));
                            break;
                        default:
                            setDropStatus(new TrayDropStatus.Error(
                                t.Message(new("App", "TransferFailed"))));
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    setDropStatus(new TrayDropStatus.Information(
                        t.Message(new("App", "TransferCancelled"))));
                }
                catch (Exception exception)
                {
                    AppDiagnostics.Report("Could not send files from the tray flyout", exception);
                    setDropStatus(new TrayDropStatus.Error(exception.Message));
                }
                finally
                {
                    setSending(false);
                }
            }
        }
    }
}

sealed record TrayFlyoutListsProps(TrayFlyoutSnapshot Snapshot);

sealed record TrayPendingSend(
    LocalSendDevice Device,
    DroppedSendPayload Payload,
    string? Error);

union TrayDropStatus(TrayDropStatus.Information, TrayDropStatus.Error)
{
    public sealed record Information(string Message);

    public sealed record Error(string Message);

    public string Message => this switch
    {
        Information(var message) => message,
        Error(var message) => message,
    };

    public bool IsError => this is Error;
}

sealed record TrayFlyoutTransfer(
    string Title,
    string Peer,
    string Status,
    long BytesTransferred,
    long TotalBytes,
    bool Indeterminate);
