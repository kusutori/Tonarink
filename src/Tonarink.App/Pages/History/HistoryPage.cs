using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Tonarink.Components.Animations;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Utilities.ByteSize;
using MenuFlyoutItemBase = Microsoft.UI.Reactor.Core.MenuFlyoutItemBase;

namespace Tonarink.Pages.History;

sealed record HistoryEntryCommands(
    Command<ReceiveHistoryEntry> Open,
    Command<ReceiveHistoryEntry> Reveal,
    Command<ReceiveHistoryEntry> ShowInfo,
    Command<ReceiveHistoryEntry> Delete);

sealed class HistoryPage : Component<HistoryPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var history = Props.HistoryStore ?? ReceiveHistoryStore.Source;
        var entries = UseExternalStore(
            listener =>
            {
                history.Changed += listener;
                return () => history.Changed -= listener;
            },
            () => history.Entries);
        var (infoEntry, setInfoEntry) = UseState<ReceiveHistoryEntry?>(null);
        var (confirmClear, setConfirmClear) = UseState(false);
        var (deleteShakeVersion, setDeleteShakeVersion) = UseState(0);
        var (filter, setFilter) = UseState(HistoryFilter.Default);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var visibleEntries = UseMemo(() => filter.FilterEntries(entries, today), entries, filter, today);
        var entryCommands = new HistoryEntryCommands(
            UseCommand(UseMemo(() => new Command<ReceiveHistoryEntry>
            {
                Label = t.Message(new("App", "HistoryOpenFile")),
                Icon = new FontIconData(AppIcons.OpenFile),
                Accelerator = Accelerator(VirtualKey.Enter),
                Execute = entry =>
                {
                    if (PathExists(entry.Path))
                        ShellLauncher.Open(entry.Path);
                },
            },
                t.Locale)),
            UseCommand(UseMemo(() => new Command<ReceiveHistoryEntry>
            {
                Label = t.Message(new("App", "HistoryShowInFolder")),
                Icon = new FontIconData(AppIcons.Folder),
                Execute = entry =>
                {
                    if (PathExists(entry.Path))
                        ShellLauncher.Reveal(entry.Path);
                },
            },
                t.Locale)),
            UseCommand(UseMemo(() => new Command<ReceiveHistoryEntry>
            {
                Label = t.Message(new("App", "HistoryInfo")),
                Icon = new FontIconData(AppIcons.Details),
                Execute = entry => setInfoEntry(entry),
            },
                t.Locale)),
            UseCommand(UseMemo(() => new Command<ReceiveHistoryEntry>
            {
                Label = t.Message(new("App", "HistoryDeleteItem")),
                Icon = new FontIconData(AppIcons.Delete),
                Accelerator = Accelerator(VirtualKey.Delete),
                Execute = entry => history.Remove(entry.Id),
            },
                t.Locale, history)));

        var previousNavigationHistory = UseRef<Guid?>(null);
        UseEffect(() =>
        {
            if (Props.NavigationHistoryId is not { } historyId)
            {
                if (previousNavigationHistory.Current is not null) setInfoEntry(null);
                previousNavigationHistory.Current = null;
                return;
            }
            previousNavigationHistory.Current = historyId;

            var entry = entries.FirstOrDefault(candidate => candidate.Id == historyId);
            if (entry is null)
                Props.ConsumeNavigationHistory(historyId);
            else
                setInfoEntry(entry);
        }, Props.NavigationHistoryId, entries);

        var fileActions = FlexRow(
                AnimatedButtons.OpenFolder(
                    t.Message(new("App", "HistoryOpenDirectory")),
                    OpenDownloadDirectory,
                    label: t.Message(new("App", "HistoryOpenDirectory")),
                    iconSize: 20),
                AnimatedButtons.Delete(
                    t.Message(new("App", "HistoryDeleteAll")),
                    () => setConfirmClear(true),
                    label: t.Message(new("App", "HistoryDeleteAll")),
                    isEnabled: entries.Count > 0,
                    critical: true,
                    shakeVersion: deleteShakeVersion,
                    iconSize: 20))
            with
        {
            ColumnGap = 8,
            Wrap = FlexWrap.Wrap
        };

        var filterActions = Component<HistoryFilterButton, HistoryFilterButtonProps>(new(filter, setFilter));

        var actions = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            fileActions.VAlign(VerticalAlignment.Center).Margin(right: 12).Grid(column: 0),
            filterActions.VAlign(VerticalAlignment.Center).Grid(column: 1));

        Element list = visibleEntries switch
        {
            [] => ScrollView(
                    Caption(t.Message(new("App", entries.Count == 0 ? "HistoryEmpty" : "HistoryNoMatches")))
                        .Foreground(Theme.SecondaryText))
                .HorizontalContentAlignment(HorizontalAlignment.Stretch),
            _ => (LazyVStack(
                    visibleEntries,
                    static entry => entry.Id.ToString("N"),
                    (entry, index) =>
                    HistoryRow(entry, t, entryCommands)
                        .PositionInSet(index + 1, visibleEntries.Count)) with
            {
                Spacing = 8,
            })
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(VerticalAlignment.Stretch),
        };

        return Border(
                FlexColumn(
                        actions,
                        filter.IsActive
                            ? Caption(t.Message(new("App", "HistoryFilterResults"),
                                    ("visible", visibleEntries.Count), ("total", entries.Count)))
                                .Foreground(Theme.SecondaryText)
                                .LiveRegion(AutomationLiveSetting.Polite)
                            : null,
                        list.Flex(grow: 1, basis: 0),
                        (ContentDialog(
                                t.Message(new("App", "HistoryDeleteAllConfirm")),
                                TextBlock(t.Message(new("App", "HistoryDeleteAllConfirmMessage")))
                                    .TextWrapping(TextWrapping.WrapWholeWords),
                                primaryButtonText: t.Message(new("App", "HistoryDeleteAll"))) with
                        {
                            IsOpen = confirmClear,
                            SecondaryButtonText = t.Message(new("App", "Cancel")),
                            DefaultButton = ContentDialogButton.Primary,
                            OnClosed = result =>
                            {
                                if (result == ContentDialogResult.Primary)
                                {
                                    history.Clear();
                                    setDeleteShakeVersion(deleteShakeVersion + 1);
                                }
                                setConfirmClear(false);
                            },
                        }).Themed(Props.Theme),
                        (ContentDialog(
                                t.Message(new("App", "HistoryInfoTitle")),
                                infoEntry is null ? Empty() : HistoryInfoBody(infoEntry, t),
                                primaryButtonText: t.Message(new("App", "Close"))) with
                        {
                            IsOpen = infoEntry is not null,
                            DefaultButton = ContentDialogButton.Primary,
                            OnClosed = _ =>
                            {
                                setInfoEntry(null);
                                if (Props.NavigationHistoryId is { } historyId)
                                    Props.ConsumeNavigationHistory(historyId);
                            },
                        }).Themed(Props.Theme)) with
                {
                    RowGap = 20
                })
            .Padding(AppLayout.PagePadding)
            .AutomationName(t.Message(new("App", "HistoryTitle")))
            .Landmark(AutomationLandmarkType.Main);

        void OpenDownloadDirectory()
        {
            var directory = Props.DownloadDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                return;

            Directory.CreateDirectory(directory);
            ShellLauncher.Open(directory);
        }
    }

    private static Element HistoryRow(
        ReceiveHistoryEntry entry,
        IntlAccessor t,
        HistoryEntryCommands commands)
    {
        var exists = PathExists(entry.Path);
        var receivedAt = entry.ReceivedAt.ToLocalTime();
        var subtitle = t.Message(
            new("App", "HistorySubtitle"),
            ("date", receivedAt.ToString("g")),
            ("size", FormatBytes(entry.Size)),
            ("sender", entry.SenderAlias));

        MenuFlyoutItemBase[] EntryMenuItems() =>
        [
            MenuItem(commands.Open, entry) with { IsEnabled = exists },
            MenuItem(commands.Reveal, entry) with { IsEnabled = exists },
            MenuItem(commands.ShowInfo, entry),
            MenuItem(commands.Delete, entry),
        ];

        return Border(
                Grid(
                    columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    Border(Icon(HistoryIcon(entry.Path)).AccessibilityHidden())
                        .Size(40, 40)
                        .CornerRadius(20)
                        .Background(Theme.SubtleFill)
                        .HAlign(HorizontalAlignment.Center)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 0),
                    VStack(4,
                            TextBlock(entry.FileName)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .ToolTip(entry.FileName),
                            Caption(subtitle)
                                .Foreground(Theme.SecondaryText)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .ToolTip(subtitle))
                        .Margin(horizontal: 12, vertical: 0)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 1),
                    AnimatedButtons.More(
                            t.Message(new("App", "HistoryEntryActions"), ("file", entry.FileName)),
                            MenuItems(
                                FlyoutPlacementMode.BottomEdgeAlignedRight,
                                EntryMenuItems()))
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 2)))
            .Padding(12)
            .CornerRadius(8)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke)
            .WithContextFlyout(MenuItems(EntryMenuItems()));
    }

    private static Element HistoryInfoBody(
        ReceiveHistoryEntry entry,
        IntlAccessor t) =>
        VStack(12,
            HistoryInfoRow(t.Message(new("App", "HistoryInfoFileName")), entry.FileName),
            HistoryInfoRow(t.Message(new("App", "HistoryInfoPath")), entry.Path),
            HistoryInfoRow(t.Message(new("App", "HistoryInfoSize")), FormatBytes(entry.Size)),
            HistoryInfoRow(t.Message(new("App", "HistoryInfoSender")), entry.SenderAlias),
            HistoryInfoRow(
                t.Message(new("App", "HistoryInfoTime")),
                entry.ReceivedAt.ToLocalTime().ToString("F")));

    private static Element HistoryInfoRow(string label, string value) =>
        VStack(4,
            Caption(label).Foreground(Theme.SecondaryText),
            TextBlock(value).TextWrapping(TextWrapping.WrapWholeWords));

    private static string HistoryIcon(string path) =>
        FileTypeGlyphs.ForPath(path);

    private static bool PathExists(string path) =>
        !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));
}
