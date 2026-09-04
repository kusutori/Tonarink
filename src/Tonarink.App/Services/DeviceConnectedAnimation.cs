using Microsoft.UI.Xaml;
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

    public static void NavigateToDestination(string key, UIElement source, Action navigate)
    {
        Prepare(key, source);
        navigate();
    }

    public static void StartDestinationWhenReady(string key, UIElement destination) =>
        destination.DispatcherQueue.TryEnqueue(() => TryStart(key, destination));

    public static void ReturnToSource(string key, UIElement destination, Action close)
    {
        Prepare(key, destination);
        close();

        destination.DispatcherQueue.TryEnqueue(() =>
        {
            if (Sources.TryGetValue(key, out var reference)
                && reference.TryGetTarget(out var source))
            {
                TryStart(key, source);
            }
        });
    }

    private static void Prepare(string key, UIElement source)
    {
        PreparedKeys.Remove(key);
        try
        {
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(key, source);
            PreparedKeys.Add(key);
        }
        catch (COMException)
        {
            // Animation is progressive enhancement; navigation must remain usable.
        }
    }

    private static void TryStart(string key, UIElement destination)
    {
        if (!PreparedKeys.Remove(key))
            return;

        try
        {
            ConnectedAnimationService.GetForCurrentView().GetAnimation(key)?.TryStart(destination);
        }
        catch (COMException)
        {
            // A disappearing window or target should degrade to the regular fade.
        }
    }
}
