using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Pages.Send.SelectedSendItemOperations;
using static Tonarink.Pages.Send.SendPageVisuals;
using static Tonarink.Utilities.ByteSize;

namespace Tonarink.Pages.Send;

sealed record SendItemsCardProps(
    IReadOnlyList<SelectedSendItem> Items,
    Action<Func<IReadOnlyList<SelectedSendItem>, IReadOnlyList<SelectedSendItem>>> UpdateItems,
    string PickerMessage,
    Action<string> SetPickerMessage,
    bool IsWideLayout,
    bool ExpandDragDropToEntireApp,
    bool FilePreviewEnabled,
    FilePreviewProvider PreviewProvider,
    string PreviewExecutablePath,
    bool HasNativeWindow,
    Func<SelectedSendItem, Task> ShareItemAsync,
    Action<SelectedSendItem> RenameItem,
    Func<DragData, Task> AddDroppedItemsAsync);

sealed class SendItemsCard : Component<SendItemsCardProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var (isFileDropActive, setFileDropActive) = UseState(false);
        var (selectedItemId, setSelectedItemId) = UseState<Guid?>(null);
        var items = Props.Items;
        var selectedHeader = items.Count == 0
            ? t.Message(new("App", "NothingSelected"))
            : t.Message(
                new("App", "SelectedItems"),
                ("count", items.Count),
                ("size", FormatBytes(items.Sum(static item => item.Length))));
        var isPreviewProviderAvailable = Props.FilePreviewEnabled
                                         && FilePreviewLauncher.IsAvailable(
                                             Props.PreviewProvider,
                                             Props.PreviewExecutablePath);

        Element selectedItemsContent = items switch
        {
            [] => ScrollView(EmptySelection(
                    isFileDropActive,
                    Props.PickerMessage,
                    Props.ExpandDragDropToEntireApp,
                    t))
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .VerticalContentAlignment(VerticalAlignment.Stretch),
            _ => (LazyVStack(
                    items,
                    static item => item.Id.ToString("N"),
                    (item, index) =>
                    Component<SelectedSendItemRow, SelectedSendItemRowProps>(new(
                            item,
                            selectedItemId == item.Id,
                            Props.FilePreviewEnabled,
                            isPreviewProviderAvailable
                            && FilePreviewLauncher.CanPreview(item.LocalPath),
                            Props.PreviewProvider,
                            Props.PreviewExecutablePath,
                            Props.HasNativeWindow
                            && item.LocalPath is { } path
                            && File.Exists(path),
                            () => setSelectedItemId(
                                selectedItemId == item.Id ? null : item.Id),
                            () => Props.ShareItemAsync(item),
                            () => Props.RenameItem(item),
                            () => Props.UpdateItems(current => (SelectedSendItem[])
                            [
                                .. current.Select(candidate => candidate.Id == item.Id
                                    ? UndoRename(candidate)
                                    : candidate)
                            ]),
                            () => Props.UpdateItems(current => (SelectedSendItem[])
                            [
                                .. current.Select(candidate => candidate.Id == item.Id
                                    ? RedoRename(candidate)
                                    : candidate)
                            ]),
                            () =>
                            {
                                if (selectedItemId == item.Id)
                                    setSelectedItemId(null);
                                Props.UpdateItems(current => (SelectedSendItem[])
                                [
                                    .. current.Where(candidate => candidate.Id != item.Id)
                                ]);
                            }))
                        .PositionInSet(index + 1, items.Count)) with
                {
                    Spacing = 8,
                })
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(VerticalAlignment.Stretch),
        };

        selectedItemsContent = Props.IsWideLayout
            ? selectedItemsContent.Flex(grow: 1, basis: 0)
            : selectedItemsContent.Height(AppLayout.NarrowSendItemsViewportHeight);

        var card = Card(
                FlexColumn(
                        FlexRow(
                                BodyStrong(selectedHeader).Flex(grow: 1, basis: 0),
                                items.Count == 0
                                    ? null
                                    : Button(t.Message(new("App", "Clear")), () =>
                                    {
                                        Props.UpdateItems(_ => []);
                                        setSelectedItemId(null);
                                        Props.SetPickerMessage(t.Message(new("App", "NothingSelected")));
                                    }).AutomationName(t.Message(new("App", "Clear")))) with
                        {
                            AlignItems = FlexAlign.Center,
                            ColumnGap = 8,
                        },
                        selectedItemsContent) with
                {
                    RowGap = 12,
                })
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch);
        if (!Props.ExpandDragDropToEntireApp)
        {
            card = card
                .OnDragEnter(args =>
                {
                    if (!args.Data.HasFormat(StandardDataFormats.StorageItems))
                        return;

                    args.AcceptedOperation = DragOperations.Copy;
                    setFileDropActive(true);
                })
                .OnDragOver(args =>
                {
                    if (!args.Data.HasFormat(StandardDataFormats.StorageItems))
                        return;

                    args.AcceptedOperation = DragOperations.Copy;
                    args.UIOverride.Caption = t.Message(new("App", "DropFilesCaption"));
                    args.UIOverride.IsCaptionVisible = true;
                    args.UIOverride.IsGlyphVisible = true;
                })
                .OnDragLeave(_ => setFileDropActive(false))
                .OnDrop(args =>
                {
                    setFileDropActive(false);
                    args.AcceptedOperation = DragOperations.Copy;
                    _ = Props.AddDroppedItemsAsync(args.Data);
                }, acceptedOps: DragOperations.Copy);
        }

        return isFileDropActive
            ? card
                .Background(Theme.SystemAttentionBackground)
                .WithBorder(Theme.SystemAttention)
            : card;
    }
}
