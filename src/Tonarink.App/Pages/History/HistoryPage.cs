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
using MenuFlyoutItemBase = Microsoft.UI.Reactor.Core.MenuFlyoutItemBase;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Utilities.ByteSize;

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
        var entries = UseExternalStore(
            listener =>
            {
                ReceiveHistoryStore.Changed += listener;
                return () => ReceiveHistoryStore.Changed -= listener;
            },
            static () => ReceiveHistoryStore.Entries);
        var (infoEntry, setInfoEntry) = UseState<ReceiveHistoryEntry?>(null);
        var (confirmClear, setConfirmClear) = UseState(false);
        var (deleteShakeVersion, setDeleteShakeVersion) = UseState(0);
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
                    Execute = entry => ReceiveHistoryStore.Remove(entry.Id),
                },
                t.Locale)));

        var previousJumpListHistory = UseRef<Guid?>(null);
        UseEffect(() =>
        {
            if (Props.JumpListHistoryId is not { } historyId)
            {
                if (previousJumpListHistory.Current is not null) setInfoEntry(null);
                previousJumpListHistory.Current = null;
                return;
            }
            previousJumpListHistory.Current = historyId;

            var entry = entries.FirstOrDefault(candidate => candidate.Id == historyId);
            if (entry is null)
                Props.ConsumeJumpListHistory(historyId);
            else
                setInfoEntry(entry);
        }, Props.JumpListHistoryId, entries);

        var actions = FlexRow(
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

        Element list = entries switch
        {
            [] => ScrollView(
                    Caption(t.Message(new("App", "HistoryEmpty")))
                        .Foreground(Theme.SecondaryText))
                .HorizontalContentAlignment(HorizontalAlignment.Stretch),
            _ => (LazyVStack(
                    entries,
                    static entry => entry.Id.ToString("N"),
                    (entry, index) =>
                    HistoryRow(entry, t, entryCommands)
                        .PositionInSet(index + 1, entries.Count)) with
                {
                    Spacing = 8,
                })
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(VerticalAlignment.Stretch),
        };

        return Border(
                FlexColumn(
                        actions,
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
                                    ReceiveHistoryStore.Clear();
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
                                if (Props.JumpListHistoryId is { } historyId)
                                    Props.ConsumeJumpListHistory(historyId);
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
