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
        if (!destination.DispatcherQueue.TryEnqueue(() => TryStart(key, destination, completed)))
            completed?.Invoke(false);
    }

    public static void ReturnToSource(string key, UIElement destination, Action close)
    {
        Prepare(key, destination);
        close();

        // Closing the overlay schedules a Reactor reconciliation. Wait for the
        // next composition frame so the source card's visual is visible again
        // before using it as the connected-animation destination.
        EventHandler<object> onRendering = null!;
        onRendering = (_, _) =>
        {
            CompositionTarget.Rendering -= onRendering;
            if (Sources.TryGetValue(key, out var reference)
                && reference.TryGetTarget(out var source))
            {
                TryStart(key, source);
            }
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

    private static void TryStart(
        string key,
        UIElement destination,
        Action<bool>? completed = null)
    {
        if (!PreparedKeys.Remove(key))
        {
            completed?.Invoke(false);
            return;
        }

        try
        {
            var animation = ConnectedAnimationService.GetForCurrentView().GetAnimation(key);
            if (animation is null)
            {
                completed?.Invoke(false);
                return;
            }

            if (completed is not null)
                animation.Completed += (_, _) => completed(true);

            if (!animation.TryStart(destination))
                completed?.Invoke(false);
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            // A disappearing window or target should degrade to the regular fade.
            completed?.Invoke(false);
        }
    }
}
