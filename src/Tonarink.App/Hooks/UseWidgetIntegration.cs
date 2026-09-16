// This file supplies partial hook members for LocalizedAppShell in the root namespace.
// ReSharper disable once CheckNamespace

namespace Tonarink;

sealed partial class LocalizedAppShell
{
    private void UseWidgetIntegration(
        AppRuntimeState runtime,
        AppSettings settings,
        OutgoingTransferViewState? outgoingTransfer,
        bool serverDesired,
        Action restoreWindow,
        Action startServer,
        Action stopServer)
    {
        var commandHandler = UseRef<Action<string>?>();

        UseEffect(
            () => WidgetAppHost.Update(runtime, settings, outgoingTransfer, serverDesired),
            runtime,
            settings,
            serverDesired,
            outgoingTransfer is null,
            outgoingTransfer?.BytesTransferred ?? 0,
            outgoingTransfer?.TotalBytes ?? 0,
            (int?)outgoingTransfer?.State ?? -1);

        UseEffect(() =>
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
