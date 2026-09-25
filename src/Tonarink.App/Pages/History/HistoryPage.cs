using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Pages.History;

sealed record HistoryPageProps(
    string DownloadDirectory,
    ElementTheme Theme,
    Guid? JumpListHistoryId,
    Action<Guid> ConsumeJumpListHistory);

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
        var entryCommands = new HistoryEntryCommands(
            UseCommand(UseMemo(() => new Command<ReceiveHistoryEntry>
                {
                    Label = t.Message(new("App", "HistoryOpenFile")),
                    Icon = new SymbolIconData("OpenFile"),
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
                    Icon = new SymbolIconData("Folder"),
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
                    Icon = new FontIconData("\uE946"),
                    Execute = entry => setInfoEntry(entry),
                },
                t.Locale)),
            UseCommand(UseMemo(() => new Command<ReceiveHistoryEntry>
                {
                    Label = t.Message(new("App", "HistoryDeleteItem")),
                    Icon = new SymbolIconData("Delete"),
                    Accelerator = Accelerator(VirtualKey.Delete),
                    Execute = entry => ReceiveHistoryStore.Remove(entry.Id),
                },
                t.Locale)));

        UseEffect(() =>
        {
            if (Props.JumpListHistoryId is not { } historyId)
                return;

            var entry = entries.FirstOrDefault(candidate => candidate.Id == historyId);
            if (entry is null)
                Props.ConsumeJumpListHistory(historyId);
            else
                setInfoEntry(entry);
        }, Props.JumpListHistoryId, entries);

        var actions = FlexRow(
                Button(HStack(Icon("\uE8DA").AccessibilityHidden(), t.Message(new("App", "HistoryOpenDirectory"))),
                        OpenDownloadDirectory)
                    .AutomationName(t.Message(new("App", "HistoryOpenDirectory"))),
                Button(HStack(Icon("\uE74D").AccessibilityHidden(), t.Message(new("App", "HistoryDeleteAll"))),
                        () => setConfirmClear(true))
                    .AutomationName(t.Message(new("App", "HistoryDeleteAll")))
                    .IsEnabled(entries.Count > 0)
                    .Resources(static resources => resources
                        .Set("ButtonForeground", Theme.SystemCritical)
                        .Set("ButtonForegroundPointerOver", Theme.SystemCritical)
                        .Set("ButtonForegroundPressed", Theme.SystemCritical)
                        .Set("ButtonForegroundDisabled", Theme.DisabledText)))
            with
        {
            ColumnGap = 8,
            Wrap = FlexWrap.Wrap
        };

        Element list = entries switch
        {
            [] => Caption(t.Message(new("App", "HistoryEmpty")))
                .Foreground(Theme.SecondaryText),
            _ => VStack(8, [
                .. entries.Select((entry, index) =>
                    HistoryRow(entry, t, entryCommands)
                        .PositionInSet(index + 1, entries.Count)
                        .WithKey(entry.Id.ToString("N")))
            ]),
        };

        return Border(
                FlexColumn(
                        actions,
                        ScrollView(list)
                            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                            .Flex(grow: 1, basis: 0),
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
                                    ReceiveHistoryStore.Clear();
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
                    Button(Icon("\uE712").AccessibilityHidden())
                        .SubtleButton()
                        .AutomationName(t.Message(new("App", "HistoryEntryActions"), ("file", entry.FileName)))
                        .MinWidth(40)
                        .MinHeight(40)
                        .VAlign(VerticalAlignment.Center)
                        .WithFlyout(MenuItems(
                            FlyoutPlacementMode.BottomEdgeAlignedRight,
                            [
                                MenuItem(commands.Open, entry) with { IsEnabled = exists },
                                MenuItem(commands.Reveal, entry) with { IsEnabled = exists },
                                MenuItem(commands.ShowInfo, entry),
                                MenuItem(commands.Delete, entry),
                            ]))
                        .Grid(column: 2)))
            .Padding(12)
            .CornerRadius(8)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke);
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
