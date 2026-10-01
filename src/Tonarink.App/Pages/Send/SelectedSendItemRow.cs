using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Windows.System;
using MenuFlyoutItemBase = Microsoft.UI.Reactor.Core.MenuFlyoutItemBase;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Pages.Send;

sealed record SelectedSendItemRowProps(
    SelectedSendItem Item,
    bool IsSelected,
    bool ShowPreview,
    bool CanPreview,
    FilePreviewProvider PreviewProvider,
    string PreviewExecutablePath,
    bool CanShare,
    Action Select,
    Func<Task> Share,
    Action Rename,
    Action UndoRename,
    Action RedoRename,
    Action Remove);

sealed class SelectedSendItemRow : Component<SelectedSendItemRowProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var item = Props.Item;
        var previewCommand = UseCommand(new Command
        {
            Label = t.Message(new("App", "Preview")),
            Icon = new FontIconData(AppIcons.Preview),
            Accelerator = Accelerator(VirtualKey.Space),
            CanExecute = Props.CanPreview,
            Execute = () => FilePreviewLauncher.TryPreview(
                Props.PreviewProvider,
                item.LocalPath,
                Props.PreviewExecutablePath),
        });
        var renameCommand = UseCommand(new Command
        {
            Label = t.Message(new("App", "Rename")),
            Icon = new FontIconData(AppIcons.Rename),
            Accelerator = Accelerator(VirtualKey.F2),
            Execute = Props.Rename,
        });
        var shareCommand = UseCommand(new Command
        {
            Label = t.Message(new("App", "Share")),
            Icon = new FontIconData(AppIcons.Share),
            Accelerator = Accelerator(VirtualKey.F8),
            CanExecute = Props.CanShare,
            ExecuteAsync = Props.Share,
        });
        var undoRenameCommand = UseCommand(new Command
        {
            Label = t.Message(new("App", "Undo")),
            Icon = new FontIconData(AppIcons.Undo),
            Accelerator = Accelerator(VirtualKey.Z, VirtualKeyModifiers.Control),
            CanExecute = item.IsRenamed,
            Execute = Props.UndoRename,
        });
        var redoRenameCommand = UseCommand(new Command
        {
            Label = t.Message(new("App", "Redo")),
            Icon = new FontIconData(AppIcons.Redo),
            Accelerator = Accelerator(VirtualKey.Y, VirtualKeyModifiers.Control),
            CanExecute = item.CanRedoRename,
            Execute = Props.RedoRename,
        });
        var removeCommand = UseCommand(new Command
        {
            Label = t.Message(new("App", "Remove")),
            Icon = new FontIconData(AppIcons.Delete),
            Accelerator = Accelerator(VirtualKey.Delete),
            Execute = Props.Remove,
        });

        Element ContextMenu() =>
            MenuItems(
            [
                .. Props.ShowPreview
                    ? [MenuItem(previewCommand)]
                    : Array.Empty<MenuFlyoutItemBase>(),
                MenuItem(shareCommand),
                MenuSeparator(),
                MenuItem(renameCommand),
                MenuItem(undoRenameCommand),
                MenuItem(redoRenameCommand),
                MenuSeparator(),
                MenuItem(removeCommand),
            ]);
        var content = Grid(
            columns: [GridSize.Auto, GridSize.Star()],
            rows: [GridSize.Auto],
            Icon(FileTypeGlyphs.ForSendItem(item.Kind, item.DisplayName)).AccessibilityHidden()
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 0),
            VStack(2,
                    TextBlock(item.DisplayName)
                        .TextTrimming(TextTrimming.CharacterEllipsis)
                        .ToolTip(item.DisplayName)
                        .Foreground(Theme.PrimaryText),
                    Caption(item.IsRenamed
                            ? t.Message(
                                new("App", "SendItemRenamed"),
                                ("kind", ItemKindLabel(t, item.Kind)),
                                ("size", FormatBytes(item.Length)))
                            : t.Message(
                                new("App", "ItemKindAndSize"),
                                ("kind", ItemKindLabel(t, item.Kind)),
                                ("size", FormatBytes(item.Length))))
                        .Foreground(item.IsRenamed ? Theme.SystemCaution : Theme.SecondaryText))
                .Margin(horizontal: 12, vertical: 0)
                .Grid(column: 1));
        Element itemContent = Button(content.Margin(right: 52), Props.Select)
            .GhostButton()
            .Padding(12)
            .HAlign(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName(t.Message(
                new("App", Props.IsSelected ? "DeselectSendItem" : "SelectSendItem"),
                ("item", item.DisplayName)))
            .HelpText(Props.CanPreview
                ? t.Message(new("App", "SendItemPreviewHint"))
                : string.Empty);

        var row = Border(
                Grid(
                    columns: [GridSize.Star()],
                    rows: [GridSize.Auto],
                    itemContent
                        .WithContextFlyout(ContextMenu())
                        .Grid(row: 0),
                    AnimatedButtons.Delete(
                            t.Message(new("App", "RemoveItem"), ("item", item.DisplayName)),
                            () => removeCommand.Execute?.Invoke(),
                            toolTip: removeCommand.Label,
                            isEnabled: removeCommand.IsEnabled)
                        .WithContextFlyout(ContextMenu())
                        .HAlign(HorizontalAlignment.Right)
                        .VAlign(VerticalAlignment.Center)
                        .Margin(right: 12)
                        .Grid(row: 0)))
            .CornerRadius(8)
            .Background(Theme.SubtleFill)
            .WithBorder(
                Props.IsSelected ? Theme.Accent : Theme.CardStroke,
                1)
            .WithContextFlyout(ContextMenu());
        return CommandHost(
            [previewCommand, shareCommand, renameCommand, undoRenameCommand, redoRenameCommand, removeCommand],
            row);
    }

    private static string ItemKindLabel(IntlAccessor t, string kind) => kind switch
    {
        "text" => t.Message(new("App", "Text")),
        "clipboard" => t.Message(new("App", "Clipboard")),
        "folder" => t.Message(new("App", "Folder")),
        _ => t.Message(new("App", "File")),
    };
}
