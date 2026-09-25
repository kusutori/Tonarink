using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;

namespace Tonarink.Hooks.Shell;

static class PeopleSuggestionHooks
{
    public static void UsePeopleSuggestions(
        this RenderContext context,
        bool enabled,
        int favoriteRevision)
    {
        context.UseEffect(() =>
        {
            var cancellation = new CancellationTokenSource();
            var favorites = FavoriteDeviceStore.Entries.Values.ToArray();
            _ = PeopleSuggestionsService.SynchronizeAsync(enabled, favorites, cancellation.Token);
            return () =>
            {
                cancellation.Cancel();
                cancellation.Dispose();
            };
        }, enabled, favoriteRevision);
    }
}
