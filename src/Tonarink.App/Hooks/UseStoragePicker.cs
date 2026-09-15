using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Localization;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Tonarink;

sealed class StoragePicker
{
    private readonly ReactorWindow? _window;
    private readonly IntlAccessor _t;

    public StoragePicker(ReactorWindow? window, IntlAccessor t)
    {
        _window = window;
        _t = t;
    }

    public async Task<IReadOnlyList<StorageFile>> PickFilesAsync(
        string commitButtonText,
        PickerLocationId startLocation = PickerLocationId.Downloads)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = startLocation,
            CommitButtonText = commitButtonText,
        };
        picker.FileTypeFilter.Add("*");
        Initialize(picker);
        var files = await picker.PickMultipleFilesAsync();
        return files is { Count: > 0 } ? files.ToArray() : [];
    }

    public async Task<StorageFolder?> PickFolderAsync(
        string commitButtonText,
        PickerLocationId startLocation = PickerLocationId.Downloads)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = startLocation,
            CommitButtonText = commitButtonText,
        };
        picker.FileTypeFilter.Add("*");
        Initialize(picker);
        return await picker.PickSingleFolderAsync();
    }

    private void Initialize(object picker)
    {
        var nativeWindow = _window?.NativeWindow
                           ?? throw new InvalidOperationException(_t.Message(new("App", "WindowUnavailable")));
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow));
    }
}
