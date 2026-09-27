using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Microsoft.UI.Xaml;

namespace Tonarink.Services.WebShare;

/// <summary>Shares content from a WinUI desktop window through the Windows share sheet.</summary>
sealed class WindowsShareSource : IDisposable
{
    private readonly nint _windowHandle;
    private readonly DataTransferManager _manager;
    private readonly string _unavailableMessage;
    private Uri? _link;
    private StorageFile[]? _storageItems;
    private string? _title;

    public WindowsShareSource(Window window, string unavailableMessage)
    {
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _unavailableMessage = unavailableMessage;
        _manager = DataTransferManagerInterop.GetForWindow(_windowHandle);
        _manager.DataRequested += OnDataRequested;
    }

    public void ShareLink(string url, string title)
    {
        _link = new Uri(url, UriKind.Absolute);
        _storageItems = null;
        _title = title;
        DataTransferManagerInterop.ShowShareUIForWindow(_windowHandle);
    }

    public async Task<bool> ShareFileAsync(string path, string title)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            _link = null;
            _storageItems = [file];
            _title = title;
            DataTransferManagerInterop.ShowShareUIForWindow(_windowHandle);
            return true;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Report("Could not open the Windows share sheet for a file", exception);
            return false;
        }
    }

    public void Dispose() => _manager.DataRequested -= OnDataRequested;

    private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        if (_storageItems is { Length: > 0 } storageItems)
        {
            var fileData = args.Request.Data;
            fileData.Properties.Title = _title ?? "Tonarink";
            fileData.SetStorageItems(storageItems, readOnly: true);
            fileData.RequestedOperation = DataPackageOperation.Copy;
            return;
        }

        if (_link is not { } link)
        {
            args.Request.FailWithDisplayText(_unavailableMessage);
            return;
        }

        var data = args.Request.Data;
        data.Properties.Title = _title ?? "Tonarink";
        data.SetWebLink(link);
        data.RequestedOperation = DataPackageOperation.Copy;
    }
}
