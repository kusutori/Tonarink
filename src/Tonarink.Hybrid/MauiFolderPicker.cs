using Tonarink.Application;

namespace Tonarink.Hybrid;

internal static class MauiFolderPicker
{
#if ANDROID
    internal const int RequestCode = 17042;
    internal static TaskCompletionSource<Android.Net.Uri?>? Pending;
#endif

    public static async Task<IReadOnlyList<ShareItem>> PickAsync(ImportedShareStore store, CancellationToken cancellationToken)
    {
#if ANDROID
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("Android activity is unavailable.");
        var pending = new TaskCompletionSource<Android.Net.Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending = pending;
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionOpenDocumentTree);
        intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission | Android.Content.ActivityFlags.GrantPersistableUriPermission);
#pragma warning disable CA1422, CS0618
        activity.StartActivityForResult(intent, RequestCode);
#pragma warning restore CA1422, CS0618
        var uri = await pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (uri is null)
            return [];
        try
        {
            activity.ContentResolver?.TakePersistableUriPermission(uri, Android.Content.ActivityFlags.GrantReadUriPermission);
        }
        catch (Java.Lang.SecurityException)
        {
            // Persistable permission is optional; the tree is still readable for this session.
        }

        var documentId = Android.Provider.DocumentsContract.GetTreeDocumentId(uri)
            ?? throw new IOException("Android could not open the selected folder.");
        var folderName = QueryDisplayName(activity, uri, documentId) ?? "folder";
        var items = new List<ShareItem>();
        await ImportDocumentTreeAsync(activity, uri, documentId, folderName, store, items, cancellationToken).ConfigureAwait(false);
        return items;
#elif IOS || MACCATALYST
        var picker = new UIKit.UIDocumentPickerViewController([UniformTypeIdentifiers.UTTypes.Folder], asCopy: false);
        var pending = new TaskCompletionSource<Foundation.NSUrl?>(TaskCreationOptions.RunContinuationsAsynchronously);
        picker.DidPickDocumentAtUrls += (_, args) => pending.TrySetResult(args.Urls.FirstOrDefault());
        picker.WasCancelled += (_, _) => pending.TrySetResult(null);
        var controller = Platform.GetCurrentUIViewController()
            ?? throw new InvalidOperationException("iOS view controller is unavailable.");
        controller.PresentViewController(picker, true, null);
        var url = await pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (url?.Path is not { Length: > 0 } path)
            return [];
        var accessed = url.StartAccessingSecurityScopedResource();
        try
        {
            return await ShareFolderItems.ImportDirectoryAsync(path, store, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (accessed)
                url.StopAccessingSecurityScopedResource();
        }
#elif WINDOWS
        var path = await MainThread.InvokeOnMainThreadAsync(PickWindowsFolderPathAsync)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(path)
            ? []
            : ShareFolderItems.FromDirectory(path, cancellationToken);
#else
        await Task.CompletedTask;
        return [];
#endif
    }

#if ANDROID
    private static async Task ImportDocumentTreeAsync(
        Android.App.Activity activity,
        Android.Net.Uri treeUri,
        string documentId,
        string relativePrefix,
        ImportedShareStore store,
        List<ShareItem> items,
        CancellationToken cancellationToken)
    {
        var resolver = activity.ContentResolver
            ?? throw new IOException("Android content resolver is unavailable.");
        var children = Android.Provider.DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, documentId);
        if (children is null)
            return;
        using var cursor = resolver.Query(children,
            [
                Android.Provider.DocumentsContract.Document.ColumnDocumentId,
                Android.Provider.DocumentsContract.Document.ColumnDisplayName,
                Android.Provider.DocumentsContract.Document.ColumnMimeType,
            ], null, null, null);
        if (cursor is null)
            return;

        var childIds = new List<(string Id, string Name, string Mime)>();
        while (cursor.MoveToNext())
        {
            var id = cursor.GetString(0);
            var name = cursor.GetString(1);
            var mime = cursor.GetString(2);
            if (string.IsNullOrWhiteSpace(id))
                continue;
            childIds.Add((id, string.IsNullOrWhiteSpace(name) ? "shared-file" : name, mime ?? "application/octet-stream"));
        }

        foreach (var child in childIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(child.Mime, Android.Provider.DocumentsContract.Document.MimeTypeDir, StringComparison.Ordinal))
            {
                await ImportDocumentTreeAsync(activity, treeUri, child.Id, $"{relativePrefix}/{child.Name}", store, items, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var fileUri = Android.Provider.DocumentsContract.BuildDocumentUriUsingTree(treeUri, child.Id);
            if (fileUri is null)
                continue;
            await using var source = resolver.OpenInputStream(fileUri);
            if (source is null)
                continue;
            items.Add(await store.ImportAsync($"{relativePrefix}/{child.Name}", source,
                child.Mime == "application/octet-stream" ? ShareFolderItems.GuessContentType(child.Name) : child.Mime,
                cancellationToken).ConfigureAwait(false));
        }
    }

    private static string? QueryDisplayName(Android.App.Activity activity, Android.Net.Uri treeUri, string documentId)
    {
        var documentUri = Android.Provider.DocumentsContract.BuildDocumentUriUsingTree(treeUri, documentId);
        if (documentUri is null)
            return null;
        using var cursor = activity.ContentResolver?.Query(documentUri,
            [Android.Provider.DocumentsContract.Document.ColumnDisplayName], null, null, null);
        return cursor?.MoveToFirst() == true ? cursor.GetString(0) : null;
    }
#endif

#if WINDOWS
    private static async Task<string?> PickWindowsFolderPathAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads,
        };
        picker.FileTypeFilter.Add("*");
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("Windows window handle is unavailable.");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
#endif
}
