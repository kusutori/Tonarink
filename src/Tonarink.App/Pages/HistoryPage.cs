using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;
using static TransferOverlayVisuals;

sealed record HistoryPageProps(string DownloadDirectory, ElementTheme Theme);

sealed class HistoryPage : Component<HistoryPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var entries = UseExternalStore<IReadOnlyList<ReceiveHistoryEntry>>(
            listener =>
            {
                ReceiveHistoryStore.Changed += listener;
                return () => ReceiveHistoryStore.Changed -= listener;
            },
            static () => ReceiveHistoryStore.Entries);
        var (infoEntry, setInfoEntry) = UseState<ReceiveHistoryEntry?>(null);
        var (confirmClear, setConfirmClear) = UseState(false);

        var header = Heading(t.Message(new("App", "HistoryTitle")))
            .HeadingLevel(AutomationHeadingLevel.Level1);

        var actions = FlexRow(
                Button(t.Message(new("App", "HistoryOpenDirectory")), OpenDownloadDirectory)
                    .AutomationName(t.Message(new("App", "HistoryOpenDirectory"))),
                Button(t.Message(new("App", "HistoryDeleteAll")), () => setConfirmClear(true))
                    .AutomationName(t.Message(new("App", "HistoryDeleteAll")))
                    .IsEnabled(entries.Count > 0)
                    .Resources(static resources => resources
                        .Set("ButtonForeground", Theme.SystemCritical)
                        .Set("ButtonForegroundPointerOver", Theme.SystemCritical)
                        .Set("ButtonForegroundPressed", Theme.SystemCritical)
                        .Set("ButtonForegroundDisabled", Theme.DisabledText)))
            with
        { ColumnGap = 8, Wrap = FlexWrap.Wrap };

        Element list = entries.Count == 0
            ? Caption(t.Message(new("App", "HistoryEmpty")))
                .Foreground(Theme.SecondaryText)
            : VStack(8, entries.Select(entry =>
                HistoryRow(entry, t, setInfoEntry).WithKey(entry.Id.ToString("N"))).ToArray<Element?>());

        return Border(
                (FlexColumn(
                    header,
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
                        DefaultButton = ContentDialogButton.Close,
                        OnClosed = result =>
                        {
                            if (result == ContentDialogResult.Primary)
                                ReceiveHistoryStore.Clear();
                            setConfirmClear(false);
                        },
                    }).Set(dialog => dialog.RequestedTheme = Props.Theme),
                    (ContentDialog(
                        t.Message(new("App", "HistoryInfoTitle")),
                        infoEntry is null ? Empty() : HistoryInfoBody(infoEntry, t),
                        primaryButtonText: t.Message(new("App", "Close"))) with
                    {
                        IsOpen = infoEntry is not null,
                        DefaultButton = ContentDialogButton.Close,
                        OnClosed = _ => setInfoEntry(null),
                    }).Set(dialog => dialog.RequestedTheme = Props.Theme)) with
                { RowGap = 20 }))
            .Padding(36)
            .Landmark(AutomationLandmarkType.Main);

        void OpenDownloadDirectory()
        {
            var directory = Props.DownloadDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                return;

            Directory.CreateDirectory(directory);
            OpenPath(directory);
        }
    }

    private static Element HistoryRow(
        ReceiveHistoryEntry entry,
        Microsoft.UI.Reactor.Localization.IntlAccessor t,
        Action<ReceiveHistoryEntry?> setInfoEntry)
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
                    Button(Icon("\uE712"), null)
                        .SubtleButton()
                        .AutomationName(t.Message(new("App", "HistoryEntryActions"), ("file", entry.FileName)))
                        .MinWidth(40)
                        .MinHeight(40)
                        .VAlign(VerticalAlignment.Center)
                        .WithFlyout(MenuItems(
                            FlyoutPlacementMode.BottomEdgeAlignedRight,
                            [
                                MenuItem(
                                    t.Message(new("App", "HistoryOpenFile")),
                                    exists ? () => OpenPath(entry.Path) : null,
                                    icon: "OpenFile"),
                                MenuItem(
                                    t.Message(new("App", "HistoryShowInFolder")),
                                    exists ? () => RevealInExplorer(entry.Path) : null,
                                    icon: "Folder"),
                                MenuItem(
                                    t.Message(new("App", "HistoryInfo")),
                                    () => setInfoEntry(entry),
                                    icon: "\uE946"),
                                MenuItem(
                                    t.Message(new("App", "HistoryDeleteItem")),
                                    () => ReceiveHistoryStore.Remove(entry.Id),
                                    icon: "Delete"),
                            ]))
                        .Grid(column: 2)))
            .Padding(12)
            .CornerRadius(8)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke, 1);
    }

    private static Element HistoryInfoBody(
        ReceiveHistoryEntry entry,
        Microsoft.UI.Reactor.Localization.IntlAccessor t) =>
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
        Directory.Exists(path) ? "Folder" : FileTypeGlyphs.ForFileName(path);

    private static bool PathExists(string path) =>
        !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                OpenPath(path);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }
}
