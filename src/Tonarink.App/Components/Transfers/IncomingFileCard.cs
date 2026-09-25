using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Components;

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
    Func<Task> PickDirectory);

sealed record IncomingFileCommandTarget(string ItemId, string DisplayName);

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
                                    Icon(model.Expanded ? "\uE73F" : "\uE740").AccessibilityHidden(),
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

    public static Element RenameDialog(
        IntlAccessor t,
        IncomingTransferRequest request,
        ElementTheme theme,
        string? renameItemId,
        string renameFileName,
        Action<string> setRenameFileName,
        Action<string?> setRenameItemId,
        Action<Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>> updateTargetFileNames)
    {
        var validName = IsValidTargetFileName(renameFileName);
        return (ContentDialog(
                t.Message(new("App", "Rename")),
                VStack(6,
                    TextBox(renameFileName, setRenameFileName)
                        .Header(t.Message(new("App", "Name")))
                        .AutomationName(t.Message(new("App", "Name")))
                        .HelpText(validName || string.IsNullOrWhiteSpace(renameFileName)
                            ? string.Empty
                            : t.Message(new("App", "InvalidFileName")))
                        .Required(),
                    validName || string.IsNullOrWhiteSpace(renameFileName)
                        ? null
                        : Caption(t.Message(new("App", "InvalidFileName")))
                            .Foreground(Theme.SystemCritical)
                            .LiveRegion(AutomationLiveSetting.Assertive)),
                primaryButtonText: t.Message(new("App", "Save"))) with
        {
            IsOpen = renameItemId is not null,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = validName,
            OnClosed = result =>
            {
                var itemId = renameItemId;
                if (result == ContentDialogResult.Primary && itemId is not null && validName)
                {
                    var originalName = request.Items.First(item => item.Id == itemId).FileName;
                    updateTargetFileNames(current => CommitRename(
                        current,
                        itemId,
                        renameFileName.Trim(),
                        originalName));
                }

                setRenameItemId(null);
                setRenameFileName(string.Empty);
            },
        }).Themed(theme);
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

    public static IReadOnlyDictionary<string, string> UndoRename(
        IReadOnlyDictionary<string, string> current,
        string itemId)
    {
        var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
        next.Remove(itemId);
        return next;
    }

    public static IReadOnlyDictionary<string, string> ApplyQuickActionNames(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyList<IncomingItem> items,
        IReadOnlyDictionary<string, string> names)
    {
        var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
        foreach (var (itemId, fileName) in names)
        {
            var originalName = items.First(item => item.Id == itemId).FileName;
            if (string.Equals(fileName, originalName, StringComparison.Ordinal))
                next.Remove(itemId);
            else
                next[itemId] = fileName;
        }

        return next;
    }

    public static IReadOnlyDictionary<string, string> CommitRename(
        IReadOnlyDictionary<string, string> current,
        string itemId,
        string fileName,
        string originalName)
    {
        var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
        if (string.Equals(fileName, originalName, StringComparison.Ordinal))
            next.Remove(itemId);
        else
            next[itemId] = fileName;
        return next;
    }

    public static bool IsValidTargetFileName(string value)
    {
        var name = value.Trim();
        return name.Length > 0
               && !name.EndsWith('.')
               && !name.EndsWith(' ')
               && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
               && !name.Contains(':', StringComparison.Ordinal);
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
        var rows = request.Items.Select((item, index) => ReceiveItemRow(model, item)
            .PositionInSet(index + 1, request.Items.Count)
            .WithKey(item.Id)).ToArray<Element?>();
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
                            Button(
                                    Icon("\uE8DA").AccessibilityHidden(),
                                    () => _ = model.PickDirectory())
                                .AutomationName(t.Message(new("App", "ChangeSaveLocation")))
                                .ToolTip(t.Message(new("App", "ChangeSaveLocation")))
                                .IsEnabled(model.CanEdit)
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
                        HStack(8,
                                TextBlock(t.Message(new("App", "IncomingFiles")))
                                    .SemiBold()
                                    .VAlign(VerticalAlignment.Center),
                                Button(
                                        Icon("\uEF60").AccessibilityHidden(),
                                        model.OpenQuickActions)
                                    .AutomationName(t.Message(new("App", "QuickActionsTitle")))
                                    .ToolTip(t.Message(new("App", "QuickActionsTitle")))
                                    .IsEnabled(model.CanEdit)
                                    .MinWidth(40)
                                    .MinHeight(40)
                                    .VAlign(VerticalAlignment.Center),
                                Button(
                                        Icon("\uE7A7").AccessibilityHidden(),
                                        model.Reset)
                                    .AutomationName(t.Message(new("App", "ResetReceiveOptions")))
                                    .ToolTip(t.Message(new("App", "ResetReceiveOptions")))
                                    .IsEnabled(model.CanEdit)
                                    .MinWidth(40)
                                    .MinHeight(40)
                                    .VAlign(VerticalAlignment.Center))
                            .HAlign(HorizontalAlignment.Left)
                            .VAlign(VerticalAlignment.Center)
                            .Grid(column: 0),
                        ThreeStateCheckBox(
                                SelectAllState(model.SelectedItemIds.Count, request.Items.Count),
                                _ => model.ToggleSelectAll())
                            .AutomationName(t.Message(new(
                                "App",
                                model.SelectedItemIds.Count == request.Items.Count
                                    ? "DeselectAllIncomingFiles"
                                    : "SelectAllIncomingFiles")))
                            .ToolTip(t.Message(new(
                                "App",
                                model.SelectedItemIds.Count == request.Items.Count
                                    ? "DeselectAllIncomingFiles"
                                    : "SelectAllIncomingFiles")))
                            .IsEnabled(model.CanEdit)
                            .Scale(1.3f)
                            .MinWidth(32)
                            .MinHeight(32)
                            .VAlign(VerticalAlignment.Center)
                            .Grid(column: 1))
                    .Grid(row: 1),
                ScrollView(
                        VStack(8, rows)
                            .Padding(left: 4, top: 4, right: 16, bottom: 4))
                    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
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
        var commandTarget = new IncomingFileCommandTarget(item.Id, displayName);

        Element FileRowMenu() => MenuItems(
            MenuItem(model.UndoRenameCommand, commandTarget) with
            {
                IsEnabled = canUndoRename,
            },
            MenuItem(model.RenameCommand, commandTarget));

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
                            Button(
                                    Icon(model.UndoRenameCommand.Icon!).AccessibilityHidden(),
                                    () => model.UndoRenameCommand.Execute?.Invoke(commandTarget))
                                .AutomationName(model.UndoRenameCommand.Label)
                                .ToolTip(model.UndoRenameCommand.Label)
                                .IsEnabled(canUndoRename)
                                .MinWidth(40)
                                .MinHeight(40)
                                .WithContextFlyout(FileRowMenu()),
                            Button(
                                    Icon(model.RenameCommand.Icon!).AccessibilityHidden(),
                                    () => model.RenameCommand.Execute?.Invoke(commandTarget))
                                .AutomationName(t.Message(
                                    new("App", "RenameIncomingFile"),
                                    ("file", displayName)))
                                .ToolTip(model.RenameCommand.Label)
                                .IsEnabled(model.RenameCommand.IsEnabled)
                                .MinWidth(40)
                                .MinHeight(40)
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
