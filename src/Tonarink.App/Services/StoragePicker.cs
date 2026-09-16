using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Tonarink.Services;

sealed class StoragePicker(Window? window, string windowUnavailableMessage)
{
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
        // Keep the projected WinRT collection; materializing a StorageFile[] here
        // makes CsWinRT require additional unsafe ABI code during Native AOT builds.
        return await picker.PickMultipleFilesAsync();
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
        var nativeWindow = window
                           ?? throw new InvalidOperationException(windowUnavailableMessage);
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow));
    }
}
