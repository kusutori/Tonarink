using Microsoft.UI.Reactor.Core;

namespace Tonarink.Hooks;

static class WidgetIntegrationHooks
{
    public static void UseWidgetIntegration(
        this RenderContext context,
        AppRuntimeState runtime,
        AppSettings settings,
        OutgoingTransferViewState? outgoingTransfer,
        bool serverDesired,
        Action restoreWindow,
        Action startServer,
        Action stopServer)
    {
        var commandHandler = context.UseRef<Action<string>?>();

        context.UseEffect(
            () => WidgetAppHost.Update(runtime, settings, outgoingTransfer, serverDesired),
            runtime,
            settings,
            serverDesired,
            outgoingTransfer is null,
            outgoingTransfer?.BytesTransferred ?? 0,
            outgoingTransfer?.TotalBytes ?? 0,
            (int?)outgoingTransfer?.State ?? -1);

        context.UseEffect(() =>
        {
            void OnCommand(string verb) => commandHandler.Current?.Invoke(verb);
            WidgetAppHost.CommandReceived += OnCommand;
            return () => WidgetAppHost.CommandReceived -= OnCommand;
        });

        commandHandler.Current = verb =>
        {
            if (string.Equals(verb, "open", StringComparison.OrdinalIgnoreCase))
            {
                restoreWindow();
                return;
            }

            if (string.Equals(verb, "stop-server", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(verb, "toggle-server", StringComparison.OrdinalIgnoreCase) && serverDesired))
            {
                stopServer();
                return;
            }

            if (string.Equals(verb, "start-server", StringComparison.OrdinalIgnoreCase)
                || string.Equals(verb, "toggle-server", StringComparison.OrdinalIgnoreCase))
                startServer();
        };
    }
}
