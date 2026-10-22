using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tonarink.Components.Animations;

// Keep event subscriptions scoped to the mounted player, not to an old render.
static class AnimatedPlayerLifecycle
{
    private static readonly ConditionalWeakTable<AnimatedVisualPlayer, Subscription> Subscriptions = new();

    public static void Mount(
        AnimatedVisualPlayer player,
        IAnimatedVisualSource source,
        Action refreshForeground,
        Action restoreProgress)
    {
        Unmount(player);
        var subscription = new Subscription(refreshForeground, restoreProgress);
        Subscriptions.Add(player, subscription);
        player.Loaded += subscription.OnLoaded;
        player.ActualThemeChanged += subscription.OnThemeChanged;
        refreshForeground();
        player.Source = source;
        restoreProgress();
    }

    public static void Unmount(AnimatedVisualPlayer player)
    {
        if (Subscriptions.TryGetValue(player, out var subscription))
        {
            player.Loaded -= subscription.OnLoaded;
            player.ActualThemeChanged -= subscription.OnThemeChanged;
            Subscriptions.Remove(player);
        }

        player.Stop();
    }

    private sealed class Subscription(Action refreshForeground, Action restoreProgress)
    {
        public void OnLoaded(object sender, RoutedEventArgs args)
        {
            refreshForeground();
            restoreProgress();
        }

        public void OnThemeChanged(FrameworkElement sender, object args) => refreshForeground();
    }
}
