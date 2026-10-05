using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;

namespace Tonarink.Components.Shell;

sealed record NavigationPageProps(ShellNavigationCoordinator Navigation, Element Content);

sealed class NavigationPage : Component<NavigationPageProps>
{
    public override Element Render()
    {
        UseNavigationLifecycle(
            onNavigatingFrom: Props.Navigation.OnNavigatingFrom,
            onNavigatedTo: Props.Navigation.OnNavigatedTo);
        return Props.Content;
    }
}

// NavigationHost caches its outgoing page only when its transition completes.
// Returning to that route earlier mounts a duplicate; the old completion then
// puts the other instance in the cache. Serialize transitions at the lifecycle
// boundary, retaining the latest request rather than dropping rapid clicks.
sealed class ShellNavigationCoordinator(NavigationHandle<AppRoute> navigation) : IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private PendingNavigation? _pending;
    private bool _transitioning;
    private bool _draining;
    private bool _disposed;

    public void OnNavigatingFrom(NavigatingFromContext context)
    {
        if (_disposed || context.IsCancelled)
            return;
        if (Equals(context.Route, context.TargetRoute))
        {
            _pending = null;
            return;
        }
        if (_transitioning || _draining)
        {
            _pending = new((AppRoute)context.TargetRoute, context.Mode);
            context.Cancel();
            return;
        }

        _transitioning = true;
    }

    public void OnNavigatedTo(NavigatedToContext context)
    {
        if (_disposed || !Equals(context.Route, navigation.CurrentRoute))
            return;

        _transitioning = false;
        if (_pending is null || _draining)
            return;

        // Finish all of the current page's lifecycle callbacks before navigating
        // again. No timer: this callback runs after the outgoing page is cached.
        _draining = true;
        if (!_dispatcher.TryEnqueue(() =>
            {
                _draining = false;
                var pending = _pending;
                _pending = null;
                if (_disposed || pending is null || pending.Route == navigation.CurrentRoute)
                    return;

                switch (pending.Mode)
                {
                    case NavigationMode.Pop:
                        navigation.PopTo(route => route == pending.Route);
                        break;
                    case NavigationMode.Forward:
                        navigation.GoForward();
                        break;
                    case NavigationMode.Replace:
                        navigation.Replace(pending.Route);
                        break;
                    case NavigationMode.Reset:
                        navigation.Reset(pending.Route);
                        break;
                    default:
                        navigation.Navigate(pending.Route,
                            AppNavigation.IsDetail(pending.Route) ? AppNavigation.DrillIn : null);
                        break;
                }
            }))
        {
            _draining = false;
            _pending = null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _pending = null;
    }

    private sealed record PendingNavigation(AppRoute Route, NavigationMode Mode);
}
