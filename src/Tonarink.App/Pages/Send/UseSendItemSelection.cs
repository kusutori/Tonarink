using LocalSendDotNet;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Localization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static Tonarink.Pages.Send.SelectedSendItemOperations;
using static Tonarink.Pages.Send.SendPageVisuals;

namespace Tonarink.Pages.Send;

sealed record SendItemSelectionController(
    string Message,
    Action<string> SetMessage,
    Action<IReadOnlyCollection<SelectedSendItem>> Add,
    Func<Task> PickFileAsync,
    Func<Task> PickFolderAsync,
    Func<Task> AddClipboardAsync,
    Func<DragData, Task> AddDroppedItemsAsync);

static class SendItemSelectionHooks
{
    public static SendItemSelectionController UseSendItemSelection(
        this RenderContext context,
        StoragePicker storagePicker,
        string message,
        Action<string> setMessage,
        ShareTargetPayload? shareTargetPayload,
        Action<Guid> consumeShareTargetPayload,
        Action<Func<IReadOnlyList<SelectedSendItem>, IReadOnlyList<SelectedSendItem>>> updateSelectedItems,
        Action resetTransfer,
        Action<SuggestedContactSend> requestSuggestedContactSend,
        IntlAccessor t)
    {
        var payloadId = shareTargetPayload?.Id ?? Guid.Empty;

        context.UseEffect(() =>
        {
            if (shareTargetPayload is not { } payload)
                return static () => { };

            var cancellation = new CancellationTokenSource();
            _ = ImportShareTargetPayloadAsync(payload, cancellation.Token);
            return () =>
            {
                cancellation.Cancel();
                cancellation.Dispose();
            };
        }, payloadId);

        return new(
            message,
            setMessage,
            Add,
            PickFileAsync,
            PickFolderAsync,
            AddClipboardAsync,
            AddDroppedItemsAsync);

        void Add(IReadOnlyCollection<SelectedSendItem> newItems)
        {
            updateSelectedItems(current => (SelectedSendItem[])[.. current, .. newItems]);
            setMessage(t.Message(new("App", "ItemsAdded"), ("count", newItems.Count)));
            resetTransfer();
        }

        async Task PickFileAsync()
        {
            try
            {
                var files = await storagePicker.PickFilesAsync(t.Message(new("App", "Add")));
                if (files.Count == 0)
                    return;

                var selected = new List<SelectedSendItem>(files.Count);
                foreach (var file in files)
                {
                    selected.Add(await SelectedSendItemReader.FromStorageFileAsync(
                        file,
                        file.Name,
                        CancellationToken.None));
                }
                Add(selected);
            }
            catch (Exception exception)
            {
                setMessage(t.Message(
                    new("App", "PickFileFailed"),
                    ("error", exception.Message)));
            }
        }

        async Task PickFolderAsync()
        {
            try
            {
                var folder = await storagePicker.PickFolderAsync(t.Message(new("App", "AddFolder")));
                if (folder is null)
                    return;

                var selected = await SelectedSendItemReader.FromFolderAsync(folder, CancellationToken.None);
                if (selected.Count == 0)
                {
                    setMessage(t.Message(new("App", "FolderEmpty")));
                    return;
                }

                Add(selected);
            }
            catch (Exception exception)
            {
                setMessage(t.Message(
                    new("App", "PickFolderFailed"),
                    ("error", exception.Message)));
            }
        }

        async Task AddClipboardAsync()
        {
            try
            {
                var data = Clipboard.GetContent();
                if (data.Contains(StandardDataFormats.StorageItems))
                {
                    var storageItems = await data.GetStorageItemsAsync();
                    var selected = new List<SelectedSendItem>();
                    foreach (var storageItem in storageItems)
                    {
                        switch (storageItem)
                        {
                            case StorageFile file:
                                selected.Add(await SelectedSendItemReader.FromStorageFileAsync(
                                    file,
                                    file.Name,
                                    CancellationToken.None));
                                break;
                            case StorageFolder folder:
                                selected.AddRange(await SelectedSendItemReader.FromFolderAsync(
                                    folder,
                                    CancellationToken.None));
                                break;
                        }
                    }

                    if (selected.Count > 0)
                    {
                        Add(selected);
                        return;
                    }
                }

                if (data.Contains(StandardDataFormats.Text))
                {
                    var clipboardText = await data.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(clipboardText))
                    {
                        var item = new SendTextItem(clipboardText, "clipboard.txt");
                        Add((SelectedSendItem[])
                        [
                            new(
                                Guid.NewGuid(),
                                item,
                                t.Message(new("App", "ClipboardText")),
                                TextLength(clipboardText),
                                "clipboard")
                        ]);
                        return;
                    }
                }

                if (data.Contains(StandardDataFormats.Bitmap))
                {
                    var bitmap = await SelectedSendItemReader.FromClipboardBitmapAsync(
                        data,
                        CancellationToken.None);
                    Add((SelectedSendItem[])
                    [
                        bitmap with
                        {
                            DisplayName = t.Message(new("App", "ClipboardImage")),
                        }
                    ]);
                    return;
                }

                setMessage(t.Message(new("App", "ClipboardEmpty")));
            }
            catch (Exception exception)
            {
                setMessage(t.Message(
                    new("App", "ClipboardReadFailed"),
                    ("error", exception.Message)));
            }
        }

        async Task ImportShareTargetPayloadAsync(
            ShareTargetPayload payload,
            CancellationToken cancellationToken)
        {
            try
            {
                var imported = new List<SelectedSendItem>();
                foreach (var sharedItem in payload.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (sharedItem)
                    {
                        case ShareTargetItem.FileSystem { IsDirectory: true } directory:
                            imported.AddRange(await SelectedSendItemReader.FromFolderPathAsync(
                                directory.Path,
                                cancellationToken));
                            break;

                        case ShareTargetItem.FileSystem file:
                            var fileInfo = new FileInfo(file.Path);
                            if (!fileInfo.Exists)
                            {
                                throw new FileNotFoundException(
                                    "The shared file is no longer available.",
                                    file.Path);
                            }
                            imported.Add(new SelectedSendItem(
                                Guid.NewGuid(),
                                new SendFileItem(fileInfo.FullName, fileInfo.Name),
                                fileInfo.Name,
                                fileInfo.Length,
                                "file",
                                fileInfo.FullName));
                            break;

                        case ShareTargetItem.Text sharedText:
                            imported.Add(new SelectedSendItem(
                                Guid.NewGuid(),
                                new SendTextItem(sharedText.Value, sharedText.FileName),
                                sharedText.FileName,
                                TextLength(sharedText.Value),
                                "text"));
                            break;
                    }
                }

                if (imported.Count == 0)
                    throw new InvalidDataException(t.Message(new("App", "ShareTargetEmpty")));

                Add(imported);
                if (!string.IsNullOrWhiteSpace(payload.SuggestedContactFingerprint))
                {
                    requestSuggestedContactSend(new(
                        Guid.NewGuid(),
                        payload.SuggestedContactFingerprint));
                }
                consumeShareTargetPayload(payload.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The page or share activation was replaced while files were being imported.
            }
            catch (Exception exception)
            {
                setMessage(t.Message(
                    new("App", "ShareTargetFailed"),
                    ("error", exception.Message)));
                consumeShareTargetPayload(payload.Id);
            }
        }

        async Task AddDroppedItemsAsync(DragData dragData)
        {
            try
            {
                var selected = await SelectedSendItemReader.ReadDroppedAsync(dragData);

                if (selected.Count == 0)
                {
                    setMessage(t.Message(new("App", "DroppedItemsEmpty")));
                    return;
                }

                Add(selected);
            }
            catch (Exception exception)
            {
                setMessage(t.Message(
                    new("App", "DropItemsFailed"),
                    ("error", exception.Message)));
            }
        }
    }
}
