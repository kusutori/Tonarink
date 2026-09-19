using CommunityToolkit.WinUI.Controls;
using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Components.DeviceVisuals;
using static Tonarink.Controls.SegmentedElement;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Components;

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
                Header(t, statusText, statusColor, snapshot),
                (transfer is null
                    ? Border(null).Height(0)
                    : TransferCard(transfer, t))
                    .WithKey("tray-transfer"),
                Component<Lists, TrayFlyoutListsProps>(new(runtime))
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
            .CornerRadius(8);

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
        TrayFlyoutSnapshot snapshot)
    {
        return Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto, GridSize.Auto],
            SubHeading("Tonarink")
                .HeadingLevel(AutomationHeadingLevel.Level1)
                .Grid(row: 0, column: 0),
            ToggleSwitch(
                    snapshot.ServerDesired,
                    on =>
                    {
                        if (on)
                            TrayFlyoutStore.StartServer();
                        else
                            TrayFlyoutStore.StopServer();
                    })
                .AutomationName(t.Message(new("App", "TrayReceiveService")))
                .VAlign(VerticalAlignment.Center)
                .Grid(row: 0, column: 1, rowSpan: 2),
            HStack(8,
                    StatusDot(statusColor),
                    Caption(statusText)
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.WrapWholeWords))
                .VAlign(VerticalAlignment.Center)
                .Grid(row: 1, column: 0));
    }

    private static Element NearbyList(AppRuntimeState runtime, IntlAccessor t)
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
                DeviceRow(device, t)
                    .PositionInSet(index + 1, runtime.Devices.Count)
                    .WithKey(device.Fingerprint))
        ]).HAlign(HorizontalAlignment.Stretch);
    }

    private static Element DeviceRow(LocalSendDevice device, IntlAccessor t) =>
        Button(
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
            .AutomationName(device.Alias);

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
        return Card(
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
                        ? ProgressIndeterminate()
                        : transfer.TotalBytes > 0
                            ? Progress(progress)
                            : null,
                    transfer.TotalBytes > 0 && !transfer.Indeterminate
                        ? Caption($"{FormatBytes(transfer.BytesTransferred)} / {FormatBytes(transfer.TotalBytes)}")
                            .Foreground(Theme.TertiaryText)
                        : null))
            .OnTapped((_, _) =>
            {
                TrayFlyoutHost.Dismiss();
                TrayFlyoutStore.Restore();
            })
            .AutomationName(transfer.Title);
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
            var history = UseExternalStore(
                static listener =>
                {
                    ReceiveHistoryStore.Changed += listener;
                    return () => ReceiveHistoryStore.Changed -= listener;
                },
                static () => ReceiveHistoryStore.Entries);
            var (tab, setTab) = UseState(0);
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

            Element list = tab == 0
                ? NearbyList(Props.Runtime, t)
                : HistoryList(history, t);

            return FlexColumn(
                    Segmented(
                            selectedIndex: tab,
                            onSelectedIndexChanged: setTab,
                            items: tabItems)
                        .HAlign(HorizontalAlignment.Stretch)
                        .WithKey("tray-segmented"),
                    ScrollView(list)
                        .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                        .Flex(grow: 1, basis: 0)) with
                {
                    RowGap = 12,
                };
        }
    }
}

sealed record TrayFlyoutListsProps(AppRuntimeState Runtime);

sealed record TrayFlyoutTransfer(
    string Title,
    string Peer,
    string Status,
    long BytesTransferred,
    long TotalBytes,
    bool Indeterminate);
