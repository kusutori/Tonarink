using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;

namespace Tonarink.WidgetProvider;

[Guid(ComServer.Clsid)]
public sealed partial class WidgetProvider : IWidgetProvider
{
    internal static readonly ManualResetEvent Idle = new(false);
    internal static readonly AutoResetEvent Work = new(false);
    private static readonly Dictionary<string, ReceiveWidget> Instances = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Activated = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static bool recovered;
    private static Timer? refresh;

    public WidgetProvider() => WidgetLog.Write("WidgetProvider constructed");

    public void CreateWidget(WidgetContext widgetContext)
    {
        try
        {
            WidgetLog.Write($"CreateWidget id={widgetContext.Id} definition={widgetContext.DefinitionId}");
            if (!string.Equals(widgetContext.DefinitionId, ReceiveWidget.DefinitionId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unknown widget '{widgetContext.DefinitionId}'.");

            lock (Gate)
            {
                Instances[widgetContext.Id] = new ReceiveWidget(widgetContext.Id);
                Idle.Reset();
            }

            Push(widgetContext.Id, includeTemplate: true);
            WidgetLog.Write("CreateWidget pushed");
        }
        catch (Exception exception)
        {
            WidgetLog.Write($"CreateWidget failed {exception}");
            throw;
        }
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        _ = customState;
        WidgetLog.Write($"DeleteWidget id={widgetId}");
        lock (Gate)
        {
            Instances.Remove(widgetId);
            Activated.Remove(widgetId);
            if (Instances.Count == 0)
            {
                StopRefreshUnlocked();
                Idle.Set();
            }
        }
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var verb = actionInvokedArgs.Verb ?? "";
        var widgetId = actionInvokedArgs.WidgetContext.Id;
        WidgetLog.Write($"OnActionInvoked verb={verb} id={widgetId}");

        try
        {
            if (string.Equals(verb, "open", StringComparison.Ordinal))
            {
                WidgetCommands.OpenApp();
                return;
            }

            if (string.Equals(verb, "toggle", StringComparison.Ordinal))
            {
                if (!WidgetSnapshot.ServerIsBusy())
                    WidgetCommands.ToggleServer(WidgetSnapshot.ServerIsOn());
                Push(widgetId, includeTemplate: false);
                return;
            }

            if (string.Equals(verb, "nearby", StringComparison.Ordinal)
                || string.Equals(verb, "history", StringComparison.Ordinal))
            {
                lock (Gate)
                {
                    if (!Instances.TryGetValue(widgetId, out var widget))
                        widget = Instances[widgetId] = new ReceiveWidget(widgetId);

                    widget.Page = string.Equals(verb, "history", StringComparison.Ordinal)
                        ? WidgetSnapshot.HistoryPage
                        : WidgetSnapshot.NearbyPage;
                }

                Push(widgetId, includeTemplate: false);
            }
        }
        catch (Exception exception)
        {
            WidgetLog.Write($"OnActionInvoked failed {exception}");
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs) =>
        Push(contextChangedArgs.WidgetContext.Id, includeTemplate: false);

    public void Activate(WidgetContext widgetContext)
    {
        WidgetLog.Write($"Activate id={widgetContext.Id}");
        lock (Gate)
        {
            Activated.Add(widgetContext.Id);
            Idle.Reset();
            EnsureRefreshUnlocked();
        }

        Push(widgetContext.Id, includeTemplate: true);
    }

    public void Deactivate(string widgetId)
    {
        WidgetLog.Write($"Deactivate id={widgetId}");
        lock (Gate)
        {
            Activated.Remove(widgetId);
            if (Activated.Count == 0)
                StopRefreshUnlocked();
        }
    }

    public static void PumpWork()
    {
        try
        {
            RefreshActivated();
        }
        catch (Exception exception)
        {
            WidgetLog.Write($"PumpWork failed {exception}");
        }
    }

    private static void Push(string widgetId, bool includeTemplate)
    {
        Recover();
        ReceiveWidget widget;
        lock (Gate)
        {
            if (!Instances.TryGetValue(widgetId, out widget!))
                widget = Instances[widgetId] = new ReceiveWidget(widgetId);
        }

        string data;
        try
        {
            data = widget.GetData();
        }
        catch (Exception exception)
        {
            WidgetLog.Write($"GetData failed {exception}");
            data = """{"title":"Tonarink","appRunning":false,"appStatusLabel":"","statusIcon":"","serverOn":false,"serverLabel":"","serverValue":"","serverColor":"default","serverHint":"","isNearby":true,"isHistory":false,"hasTransfer":false,"hasProgressBar":false,"hasDevices":false,"hasHistoryItems":false,"deviceCount":0,"deviceCountLabel":"","historyCountLabel":"","emptyLabel":"","historyEmptyLabel":"","nearbyTab":"","historyTab":"","nearbyWeight":"bolder","historyWeight":"default","nearbyColor":"accent","historyColor":"default","transferTitle":"","transferPeer":"","transferStatus":"","transferProgress":"","devices":[],"history":[]}""";
        }

        var options = new WidgetUpdateRequestOptions(widgetId)
        {
            Data = data,
            CustomState = widget.Page,
        };
        if (includeTemplate)
            options.Template = ReceiveWidget.GetTemplate();

        WidgetManager.GetDefault().UpdateWidget(options);
    }

    private static void RefreshActivated()
    {
        string[] ids;
        lock (Gate)
            ids = Activated.Count == 0 ? [] : Activated.ToArray();

        foreach (var id in ids)
        {
            try
            {
                Push(id, includeTemplate: false);
            }
            catch (Exception exception)
            {
                WidgetLog.Write($"Refresh failed {exception.Message}");
            }
        }
    }

    private static void EnsureRefreshUnlocked()
    {
        refresh ??= new Timer(
            static _ =>
            {
                try
                {
                    Work.Set();
                }
                catch (ObjectDisposedException exception)
                {
                    WidgetLog.Write($"Widget work signal was already disposed: {exception.Message}");
                }
            },
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private static void StopRefreshUnlocked()
    {
        refresh?.Dispose();
        refresh = null;
    }

    private static void Recover()
    {
        if (recovered)
            return;

        recovered = true;
        try
        {
            var infos = WidgetManager.GetDefault().GetWidgetInfos();
            if (infos is null)
                return;

            var stale = new List<string>();
            lock (Gate)
            {
                foreach (var info in infos)
                {
                    var context = info.WidgetContext;
                    if (!string.Equals(context.DefinitionId, ReceiveWidget.DefinitionId, StringComparison.Ordinal))
                    {
                        stale.Add(context.Id);
                        continue;
                    }

                    var page = string.Equals(info.CustomState, WidgetSnapshot.HistoryPage, StringComparison.Ordinal)
                        ? WidgetSnapshot.HistoryPage
                        : WidgetSnapshot.NearbyPage;
                    Instances[context.Id] = new ReceiveWidget(context.Id, page);
                }

                if (Instances.Count > 0)
                    Idle.Reset();
            }

            foreach (var id in stale)
                WidgetManager.GetDefault().DeleteWidget(id);
        }
        catch (Exception exception)
        {
            WidgetLog.Write($"Recover failed {exception}");
        }
    }
}
