using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;

namespace Tonarink.Services.WebShare;

/// <summary>Shares content from a WinUI desktop window through the Windows share sheet.</summary>
sealed class WindowsShareSource : IDisposable
{
    private readonly nint _windowHandle;
    private readonly DataTransferManager _manager;
    private readonly string _unavailableMessage;
    private Uri? _link;
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
        _title = title;
        DataTransferManagerInterop.ShowShareUIForWindow(_windowHandle);
    }

    public void Dispose() => _manager.DataRequested -= OnDataRequested;

    private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
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
