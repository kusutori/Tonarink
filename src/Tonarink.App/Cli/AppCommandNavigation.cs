using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Tonarink.Cli;

static class AppCommandNavigation
{
    // WinUI permits one ContentDialog per XamlRoot. Finish the previous launch
    // dialog before navigating to another; never discard an unrelated editor.
    public static async Task PrepareAsync(XamlRoot? root, string favoritesTitle, string historyTitle, CancellationToken token)
    {
        if (root is null) return;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (FindDialog(popup.Child) is not { } dialog) continue;
            if (dialog.Title is not string title || (title != favoritesTitle && title != historyTitle))
                throw new CliException("Close the current dialog before opening a device or history entry.", 4);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) => closed.TrySetResult();
            dialog.Closed += OnClosed;
            try
            {
                dialog.Hide();
                await closed.Task.WaitAsync(token);
            }
            finally { dialog.Closed -= OnClosed; }
            // Let Reactor's ShowAsync continuation consume the old activation
            // and update its controlled IsOpen state before publishing the next.
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (ReactorApp.UIDispatcher is not { } dispatcher || !dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => settled.TrySetResult()))
                throw new CliException("The app dispatcher is unavailable.", 3);
            await settled.Task.WaitAsync(token);
        }
    }

    private static ContentDialog? FindDialog(DependencyObject? node)
    {
        if (node is ContentDialog dialog) return dialog;
        if (node is null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindDialog(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }
}
