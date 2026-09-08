using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.InteropServices;

internal static class DeviceConnectedAnimation
{
    private static readonly Dictionary<string, WeakReference<UIElement>> Sources =
        new(StringComparer.Ordinal);
    private static readonly HashSet<string> PreparedKeys = new(StringComparer.Ordinal);

    public static void RegisterSource(string key, UIElement source) =>
        Sources[key] = new(source);

    public static void UnregisterSource(string key, UIElement source)
    {
        if (Sources.TryGetValue(key, out var reference)
            && reference.TryGetTarget(out var current)
            && ReferenceEquals(current, source))
        {
            Sources.Remove(key);
        }
    }

    public static void NavigateToDestination(
        string key,
        UIElement source,
        Action navigate,
        ConnectedAnimationConfiguration? configuration = null)
    {
        Prepare(key, source, configuration);
        navigate();
    }

    public static void StartDestinationWhenReady(
        string key,
        UIElement destination,
        Action<bool>? completed = null)
    {
        if (!destination.DispatcherQueue.TryEnqueue(() =>
            {
                if (!TryStart(key, destination, completed))
                    StartDestinationAfterLayout(key, destination, completed);
            }))
        {
            completed?.Invoke(false);
        }
    }

    public static void StartDestinationAfterLayout(
        string key,
        UIElement destination,
        Action<bool>? completed = null)
    {
        const int maximumLayoutFrames = 8;
        var remainingLayoutFrames = maximumLayoutFrames;
        EventHandler<object> onRendering = null!;
        onRendering = (_, _) =>
        {
            remainingLayoutFrames--;
            if (destination.XamlRoot is null)
            {
                CompositionTarget.Rendering -= onRendering;
                PreparedKeys.Remove(key);
                completed?.Invoke(false);
                return;
            }

            var sized = destination is not FrameworkElement element
                || (element.ActualWidth > 0 && element.ActualHeight > 0);
            if (sized && TryStart(key, destination, completed))
            {
                CompositionTarget.Rendering -= onRendering;
                return;
            }

            if (remainingLayoutFrames > 0)
                return;

            CompositionTarget.Rendering -= onRendering;
            PreparedKeys.Remove(key);
            completed?.Invoke(false);
        };
        CompositionTarget.Rendering += onRendering;
    }

    public static void ReturnToSource(string key, UIElement destination, Action close)
    {
        Prepare(key, destination);
        close();

        // Closing the overlay only schedules a Reactor render. One composition
        // frame is often still before the source card is back in a live, sized
        // visual — TryStart then fails and the prepared animation is gone.
        const int maximumFrames = 8;
        var remainingFrames = maximumFrames;
        EventHandler<object> onRendering = null!;
        onRendering = (_, _) =>
        {
            remainingFrames--;
            if (!PreparedKeys.Contains(key))
            {
                CompositionTarget.Rendering -= onRendering;
                return;
            }

            if (Sources.TryGetValue(key, out var reference)
                && reference.TryGetTarget(out var source)
                && source.XamlRoot is not null
                && source is FrameworkElement element
                && element.ActualWidth > 0
                && element.ActualHeight > 0
                && TryStart(key, source))
            {
                CompositionTarget.Rendering -= onRendering;
                return;
            }

            if (remainingFrames > 0)
                return;

            CompositionTarget.Rendering -= onRendering;
            PreparedKeys.Remove(key);
        };
        CompositionTarget.Rendering += onRendering;
    }

    private static void Prepare(
        string key,
        UIElement source,
        ConnectedAnimationConfiguration? configuration = null)
    {
        PreparedKeys.Remove(key);
        try
        {
            var animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(key, source);
            if (configuration is not null)
                animation.Configuration = configuration;
            PreparedKeys.Add(key);
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            // Animation is progressive enhancement. A source can be detached by
            // an overlapping reconciliation before composition captures it.
        }
    }

    private static bool TryStart(
        string key,
        UIElement destination,
        Action<bool>? completed = null)
    {
        if (!PreparedKeys.Contains(key))
        {
            completed?.Invoke(false);
            return true;
        }

        try
        {
            var animation = ConnectedAnimationService.GetForCurrentView().GetAnimation(key);
            if (animation is null)
            {
                PreparedKeys.Remove(key);
                completed?.Invoke(false);
                return true;
            }

            if (!animation.TryStart(destination))
                return false;

            PreparedKeys.Remove(key);
            if (completed is not null)
                animation.Completed += (_, _) => completed(true);
            return true;
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            // Ancestor OpacityTransition can reject RenderTransform for a frame.
            return false;
        }
    }
}
