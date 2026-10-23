using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;

namespace Tonarink.Hooks.Shell;

static class JumpListHooks
{
    public static void UseJumpListIntegration(this RenderContext context, IntlAccessor t, string locale,
        bool isEnabled)
    {
        context.UseEffect(() =>
        {
            JumpListService.SetEnabled(isEnabled);
            var labels = new JumpListLabels(
                t.Message(new("App", "JumpListFavoriteDevicesGroup")),
                t.Message(new("App", "JumpListHistoryGroup")),
                device => t.Message(new("App", "SendToFavorite"), ("device", device)),
                file => t.Message(new("App", "HistoryOpenFileDescription"), ("file", file)));

            void Refresh() => _ = JumpListService.RefreshAsync(labels);

            if (isEnabled)
            {
                FavoriteDeviceStore.Changed += Refresh;
                ReceiveHistoryStore.Changed += Refresh;
            }
            Refresh();
            return () =>
            {
                FavoriteDeviceStore.Changed -= Refresh;
                ReceiveHistoryStore.Changed -= Refresh;
            };
        }, locale, isEnabled);
    }
}
