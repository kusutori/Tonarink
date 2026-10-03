using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Tonarink.Components.Animations;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Components.Transfers;

sealed record IncomingFileCardModel(
    IncomingTransferRequest Request,
    IntlAccessor T,
    bool Expanded,
    bool CanEdit,
    bool ReduceMotion,
    bool Animating,
    double Width,
    double? Height,
    string AnimationKey,
    string DestinationDirectory,
    IReadOnlySet<string> SelectedItemIds,
    IReadOnlyDictionary<string, string> TargetFileNames,
    IReadOnlyDictionary<string, string> RedoFileNames,
    string? FolderError,
    ElementTheme Theme,
    Action ToggleExpanded,
    Action CompleteAnimation,
    Action<FrameworkElement?> SetFileCardElement,
    Action<FrameworkElement> ClearFileCardElement,
    Action<string> ToggleItem,
    Action ToggleSelectAll,
    Action Reset,
    Action OpenQuickActions,
    Command<IncomingFileCommandTarget> RenameCommand,
    Command<IncomingFileCommandTarget> UndoRenameCommand,
    Command<IncomingFileCommandTarget> RedoRenameCommand,
    Func<Task> PickDirectory);

sealed record IncomingFileCommandTarget(string ItemId, string DisplayName);

sealed record IncomingRenameState(
    IReadOnlyDictionary<string, string> TargetFileNames,
    IReadOnlyDictionary<string, string> RedoFileNames);

static class IncomingFileCard
{
    public static Element Build(IncomingFileCardModel model)
    {
        var request = model.Request;
        var t = model.T;
        Element fileCard = Card(
                Grid(
                        columns: [GridSize.Star()],
                        rows: [GridSize.Auto, model.Expanded ? GridSize.Star() : GridSize.Auto],
                        Grid(
                            columns: [GridSize.Star(), GridSize.Auto],
                            rows: [GridSize.Auto],
                            TextBlock(t.Message(
                                    new("App", "SelectedIncomingFiles"),
                                    ("selected", model.SelectedItemIds.Count),
                                    ("count", request.Items.Count)))
                                .SemiBold()
                                .VAlign(VerticalAlignment.Center)
                                .Grid(column: 0),
                            Button(
                                    Icon(model.Expanded ? AppIcons.Contract : AppIcons.Expand).AccessibilityHidden(),
                                    model.ToggleExpanded)
                                .AutomationName(t.Message(new(
                                    "App",
                                    model.Expanded ? "HideReceiveOptions" : "ShowReceiveOptions")))
                                .ToolTip(t.Message(new(
                                    "App",
                                    model.Expanded ? "HideReceiveOptions" : "ShowReceiveOptions")))
                                .IsEnabled(model.CanEdit)
                                .MinWidth(40)
                                .MinHeight(40)
                                .SubtleButton()
                                .Grid(column: 1)),
                        model.Expanded
                            ? ReceiveOptions(model).Grid(row: 1)
                            : VStack(8, CollapsedRows(model)).Grid(row: 1)) with
                {
                    RowSpacing = 12,
                })
            .Width(model.Width)
            .HAlign(HorizontalAlignment.Center);
        if (model.Height is { } height)
            fileCard = fileCard.Height(height);

        return fileCard
            .WithKey(model.Expanded ? "incoming-file-options-expanded" : "incoming-file-options-collapsed")
            .OnMountAdd(element =>
            {
                model.SetFileCardElement(element);
                if (!model.ReduceMotion && model.Animating)
                {
                    DeviceConnectedAnimation.StartDestinationAfterLayout(
                        model.AnimationKey,
                        element,
                        _ => model.CompleteAnimation());
                }
            })
            .OnUnmountAdd(element => model.ClearFileCardElement(element));
    }

    public static IReadOnlySet<string> ToggleItem(IReadOnlySet<string> current, string itemId)
    {
        var next = new HashSet<string>(current, StringComparer.Ordinal);
        if (!next.Remove(itemId))
            next.Add(itemId);
        return next;
    }

    public static bool? SelectAllState(int selected, int total) => (selected, total) switch
    {
        (0, _) => false,
        var (count, all) when count == all => true,
        _ => null,
    };

    public static IReadOnlySet<string> ToggleSelectAll(
        IReadOnlySet<string> selected,
        IReadOnlyList<IncomingItem> items) =>
        selected.Count == items.Count
            ? new HashSet<string>(StringComparer.Ordinal)
            : items.Select(static item => item.Id).ToHashSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> AllItemIds(IReadOnlyList<IncomingItem> items) =>
        items.Select(static item => item.Id).ToHashSet(StringComparer.Ordinal);

    public static IncomingRenameState EmptyRenameState() => new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal));

    public static IncomingRenameState UndoRename(
        IncomingRenameState current,
        string itemId)
    {
        if (!current.TargetFileNames.TryGetValue(itemId, out var renamedFileName))
            return current;

        var targetFileNames = new Dictionary<string, string>(
            current.TargetFileNames,
            StringComparer.Ordinal);
        var redoFileNames = new Dictionary<string, string>(
            current.RedoFileNames,
            StringComparer.Ordinal)
        {
            [itemId] = renamedFileName,
        };
        targetFileNames.Remove(itemId);
        return new(targetFileNames, redoFileNames);
    }

    public static IncomingRenameState RedoRename(
        IncomingRenameState current,
        string itemId)
    {
        if (!current.RedoFileNames.TryGetValue(itemId, out var redoFileName))
            return current;

        var targetFileNames = new Dictionary<string, string>(
            current.TargetFileNames,
            StringComparer.Ordinal)
        {
            [itemId] = redoFileName,
        };
        return current with { TargetFileNames = targetFileNames };
    }

    public static IncomingRenameState ApplyQuickActionNames(
        IncomingRenameState current,
        IReadOnlyList<IncomingItem> items,
        IReadOnlyDictionary<string, string> names)
    {
        var targetFileNames = new Dictionary<string, string>(
            current.TargetFileNames,
            StringComparer.Ordinal);
        var redoFileNames = new Dictionary<string, string>(
            current.RedoFileNames,
            StringComparer.Ordinal);
        foreach (var (itemId, fileName) in names)
        {
            var originalName = items.First(item => item.Id == itemId).FileName;
            if (string.Equals(fileName, originalName, StringComparison.Ordinal))
                targetFileNames.Remove(itemId);
            else
                targetFileNames[itemId] = fileName;
            redoFileNames.Remove(itemId);
        }

        return new(targetFileNames, redoFileNames);
    }

    public static IncomingRenameState CommitRename(
        IncomingRenameState current,
        string itemId,
        string fileName,
        string originalName)
    {
        var targetFileNames = new Dictionary<string, string>(
            current.TargetFileNames,
            StringComparer.Ordinal);
        var redoFileNames = new Dictionary<string, string>(
            current.RedoFileNames,
            StringComparer.Ordinal);
        if (string.Equals(fileName, originalName, StringComparison.Ordinal))
            targetFileNames.Remove(itemId);
        else
            targetFileNames[itemId] = fileName;
        redoFileNames.Remove(itemId);
        return new(targetFileNames, redoFileNames);
    }

    private static Element?[] CollapsedRows(IncomingFileCardModel model)
    {
        var request = model.Request;
        var t = model.T;
        return request.Items.Take(5).Select(item =>
                Grid(
                        columns: [GridSize.Star(), GridSize.Auto],
                        rows: [GridSize.Auto],
                        TextBlock(item.FileName)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .ToolTip(item.FileName)
                            .Grid(column: 0),
                        Caption(FormatBytes(item.Size))
                            .Foreground(Theme.SecondaryText)
                            .Grid(column: 1))
                    .WithKey(item.Id))
            .Cast<Element?>()
            .Append(request.Items.Count > 5
                ? Caption(t.Message(
                        new("App", "MoreItems"),
                        ("count", request.Items.Count - 5)))
                    .Foreground(Theme.SecondaryText)
                : null)
            .ToArray<Element?>();
    }

    private static Element ReceiveOptions(IncomingFileCardModel model)
    {
        var request = model.Request;
        var t = model.T;
        return Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Auto, GridSize.Auto, GridSize.Star()],
                VStack(4,
                        Grid(
                            columns: [GridSize.Star(), GridSize.Auto],
                            rows: [GridSize.Auto],
                            VStack(2,
                                    Caption(t.Message(new("App", "ReceiveSaveDirectory")))
                                        .Foreground(Theme.SecondaryText),
                                    TextBlock(model.DestinationDirectory)
                                        .TextTrimming(TextTrimming.CharacterEllipsis)
                                        .ToolTip(model.DestinationDirectory))
                                .Grid(column: 0),
                            AnimatedButtons.OpenFolder(
                                    t.Message(new("App", "ChangeSaveLocation")),
                                    () => _ = model.PickDirectory(),
                                    toolTip: t.Message(new("App", "ChangeSaveLocation")),
                                    isEnabled: model.CanEdit)
                                .MinWidth(40)
                                .MinHeight(40)
                                .Grid(column: 1)),
                        model.FolderError is null
                            ? null
                            : Caption(model.FolderError)
                                .Foreground(Theme.SystemCritical)
                                .LiveRegion(AutomationLiveSetting.Assertive))
                    .Grid(row: 0),
                Grid(
                        columns: [GridSize.Star(), GridSize.Auto],
                        rows: [GridSize.Auto],
                        TextBlock(t.Message(new("App", "IncomingFiles")))
                            .SemiBold()
                            .VAlign(VerticalAlignment.Center)
                            .Grid(column: 0),
                        HStack(8,
                                AnimatedButtons.QuickActions(
                                        t.Message(new("App", "QuickActionsTitle")),
                                        model.OpenQuickActions,
                                        isEnabled: model.CanEdit)
                                    .VAlign(VerticalAlignment.Center),
                                AnimatedButtons.Undo(
                                        t.Message(new("App", "ResetReceiveOptions")),
                                        model.Reset,
                                        isEnabled: model.CanEdit)
                                    .VAlign(VerticalAlignment.Center),
                                AnimatedButtons.SelectAll(
                                        SelectAllState(model.SelectedItemIds.Count, request.Items.Count),
                                        _ => model.ToggleSelectAll(),
                                        t.Message(new(
                                            "App",
                                            model.SelectedItemIds.Count == request.Items.Count
                                                ? "DeselectAllIncomingFiles"
                                                : "SelectAllIncomingFiles")),
                                        isEnabled: model.CanEdit)
                                    .VAlign(VerticalAlignment.Center))
                            .HAlign(HorizontalAlignment.Right)
                            .VAlign(VerticalAlignment.Center)
                            .Grid(column: 1))
                    .Grid(row: 1),
                (LazyVStack(
                        request.Items,
                        static item => item.Id,
                        (item, index) => ReceiveItemRow(model, item)
                            .PositionInSet(index + 1, request.Items.Count)) with
                    {
                        Spacing = 8,
                    })
                    .Padding(left: 4, top: 4, right: 16, bottom: 4)
                    .HAlign(HorizontalAlignment.Stretch)
                    .VAlign(VerticalAlignment.Stretch)
                    .Grid(row: 2)) with
        {
            RowSpacing = 12,
        };
    }

    private static Element ReceiveItemRow(IncomingFileCardModel model, IncomingItem item)
    {
        var t = model.T;
        var isSelected = model.SelectedItemIds.Contains(item.Id);
        var isRenamed = model.TargetFileNames.ContainsKey(item.Id);
        var displayName = isRenamed
            ? model.TargetFileNames[item.Id]
            : item.FileName;
        var renameHint = t.Message(
            new("App", "RenameSuccess"),
            ("size", FormatBytes(item.Size)));
        var canUndoRename = isRenamed && model.CanEdit;
        var canRedoRename = model.CanEdit
                            && model.RedoFileNames.TryGetValue(item.Id, out var redoFileName)
                            && (!model.TargetFileNames.TryGetValue(item.Id, out var targetFileName)
                                || !string.Equals(targetFileName, redoFileName, StringComparison.Ordinal));
        var commandTarget = new IncomingFileCommandTarget(item.Id, displayName);

        Element FileRowMenu() => MenuItems(
            MenuItem(model.RenameCommand, commandTarget),
            MenuItem(model.UndoRenameCommand, commandTarget) with
            {
                IsEnabled = canUndoRename,
            },
            MenuItem(model.RedoRenameCommand, commandTarget) with
            {
                IsEnabled = canRedoRename,
            });

        return Border(
                Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    Button(
                            Grid(
                                    columns: [GridSize.Auto, GridSize.Star()],
                                    rows: [GridSize.Auto],
                                    Border(Icon(FileTypeGlyphs.ForFileName(displayName)).AccessibilityHidden())
                                        .Size(40, 40)
                                        .CornerRadius(8)
                                        .Background(Theme.SubtleFill)
                                        .Grid(column: 0),
                                    VStack(2,
                                            TextBlock(displayName)
                                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                                .ToolTip(displayName)
                                                .TextAlignment(TextAlignment.Left)
                                                .HAlign(HorizontalAlignment.Stretch),
                                            Caption(isRenamed ? renameHint : FormatBytes(item.Size))
                                                .Foreground(isRenamed ? Theme.SystemCaution : Theme.SecondaryText)
                                                .TextAlignment(TextAlignment.Left)
                                                .HAlign(HorizontalAlignment.Stretch))
                                        .VAlign(VerticalAlignment.Center)
                                        .HAlign(HorizontalAlignment.Stretch)
                                        .Grid(column: 1))
                                with
                            {
                                ColumnSpacing = 12
                            },
                            () => model.ToggleItem(item.Id))
                        .AutomationName(t.Message(
                            new("App", isSelected ? "DeselectIncomingFile" : "SelectIncomingFile"),
                            ("file", displayName)))
                        .HAlign(HorizontalAlignment.Stretch)
                        .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                        .GhostButton()
                        .WithContextFlyout(FileRowMenu())
                        .Grid(column: 0),
                    HStack(
                            AnimatedButtons.Undo(
                                    model.UndoRenameCommand.Label,
                                    () => model.UndoRenameCommand.Execute?.Invoke(commandTarget),
                                    isEnabled: canUndoRename)
                                .WithContextFlyout(FileRowMenu()),
                            AnimatedButtons.Redo(
                                    model.RedoRenameCommand.Label,
                                    () => model.RedoRenameCommand.Execute?.Invoke(commandTarget),
                                    isEnabled: canRedoRename)
                                .WithContextFlyout(FileRowMenu()),
                            AnimatedButtons.Rename(
                                    t.Message(
                                        new("App", "RenameIncomingFile"),
                                        ("file", displayName)),
                                    () => model.RenameCommand.Execute?.Invoke(commandTarget),
                                    model.RenameCommand.Label,
                                    model.RenameCommand.IsEnabled)
                                .WithContextFlyout(FileRowMenu()))
                        .Margin(8)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 1)))
            .CornerRadius(8)
            .Background(Theme.CardBackground)
            .WithBorder(isSelected ? Theme.Accent : Theme.CardStroke, 2)
            .WithContextFlyout(FileRowMenu());
    }
}
