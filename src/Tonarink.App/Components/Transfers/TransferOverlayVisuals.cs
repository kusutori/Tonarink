using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Transfers;

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

        (TaskbarProgressState? State, double? Value) progress = transfer.State switch
        {
            TransferState.Preparing or TransferState.WaitingForAcceptance =>
                (TaskbarProgressState.Indeterminate, null),
            TransferState.Transferring when transfer.TotalBytes > 0 =>
                (TaskbarProgressState.Normal, transfer.Fraction),
            TransferState.Transferring => (TaskbarProgressState.Indeterminate, null),
            TransferState.Failed =>
                (TaskbarProgressState.Error, transfer.TotalBytes > 0 ? transfer.Fraction : 1),
            TransferState.Completed or TransferState.Cancelled => (null, null),
        };
        if (progress.State is null)
        {
            ClearTaskbarProgress(window);
            return;
        }
        taskbar.Progress.State = progress.State.Value;
        if (progress.Value is { } fraction)
            taskbar.Progress.Value = fraction;
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
                    Icon(AppIcons.Verify).AccessibilityHidden(),
                    TextBlock(t.Message(new("App", "VerifyAction")))),
                onClick)
            .AutomationName(t.Message(new("App", "VerifyAction")))
            .IsEnabled(isEnabled)
            .MinWidth(120);


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
