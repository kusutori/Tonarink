using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using static Microsoft.UI.Reactor.Factories;

static class TransferOverlayVisuals
{
    public static string DeviceConnectedKey(string fingerprint) => $"device:{fingerprint}";

    public static void UpdateTaskbarProgress(
        ReactorWindow? window,
        TaskbarTransferProgress transfer)
    {
        if (window is null)
            return;

        var taskbar = window.TaskbarItem;
        taskbar.Description = transfer.Description;

        switch (transfer.State)
        {
            case TransferState.Preparing or TransferState.WaitingForAcceptance:
                taskbar.Progress.State = TaskbarProgressState.Indeterminate;
                break;

            case TransferState.Transferring when transfer.TotalBytes > 0:
                taskbar.Progress.State = TaskbarProgressState.Normal;
                taskbar.Progress.Value = transfer.Fraction;
                break;

            case TransferState.Transferring:
                taskbar.Progress.State = TaskbarProgressState.Indeterminate;
                break;

            case TransferState.Failed:
                taskbar.Progress.State = TaskbarProgressState.Error;
                taskbar.Progress.Value = transfer.TotalBytes > 0 ? transfer.Fraction : 1;
                break;

            default:
                ClearTaskbarProgress(window);
                break;
        }
    }

    public static void ClearTaskbarProgress(ReactorWindow? window)
    {
        if (window is null)
            return;

        window.TaskbarItem.Progress.Clear();
        window.TaskbarItem.Description = null;
    }

    public static Element VerificationButton(
        IntlAccessor t,
        Action onClick,
        bool isEnabled = true) =>
        Button(
                HStack(8,
                    Icon("\uF760").AccessibilityHidden(),
                    TextBlock(t.Message(new("App", "VerifyAction")))),
                onClick)
            .AutomationName(t.Message(new("App", "VerifyAction")))
            .IsEnabled(isEnabled)
            .MinWidth(120);

    public static bool IsText(IncomingItem item) =>
        item.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

    public static string IncomingSummary(IntlAccessor t, IReadOnlyList<IncomingItem> items) =>
        items switch
        {
            [var item] when IsText(item) => t.Message(new("App", "IncomingTextSummary")),
            [var item] => t.Message(new("App", "IncomingFileSummary"), ("file", item.FileName)),
            _ => t.Message(new("App", "IncomingItemsSummary"), ("count", items.Count)),
        };
}

sealed record TaskbarTransferProgress(
    TransferState State,
    long BytesTransferred,
    long TotalBytes,
    string Description)
{
    public double Fraction => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesTransferred / (double)TotalBytes, 0, 1);
}
