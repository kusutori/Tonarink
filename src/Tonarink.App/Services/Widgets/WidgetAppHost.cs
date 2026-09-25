using System.Text.Json;
using System.Text.Json.Serialization;
using LocalSendDotNet;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Localization;

namespace Tonarink.Services.Widgets;

static class WidgetAppHost
{
    public const string MutexName = @"Local\Tonarink.App.Running";
    public const string CommandEventName = @"Local\Tonarink.WidgetCommand";
    public const string SnapshotFileName = "widget-snapshot.json";
    public const string CommandFileName = "widget-command.json";

    private static readonly Lock Gate = new();
    private static Mutex? _runningMutex;
    private static EventWaitHandle? _commandEvent;
    private static RegisteredWaitHandle? _commandWait;
    private static bool _started;

    private static AppRuntimeState _runtime = AppRuntimeState.Initial;
    private static AppSettings _settings = AppSettings.Default;
    private static OutgoingTransferViewState? _outgoing;
    private static WidgetTransferInfo? _incoming;
    private static bool _serverDesired = true;

    public static event Action<string>? CommandReceived;

    public static WidgetTransferInfo? Incoming
    {
        get
        {
            lock (Gate)
                return _incoming;
        }
    }

    public static void Start()
    {
        lock (Gate)
        {
            if (_started)
                return;

            _started = true;
            _runningMutex = new Mutex(initiallyOwned: true, MutexName, out _);
            _commandEvent = new EventWaitHandle(false, EventResetMode.AutoReset, CommandEventName);
            _commandWait = ThreadPool.RegisterWaitForSingleObject(
                _commandEvent,
                static (_, _) => DrainCommand(),
                null,
                -1,
                executeOnlyOnce: false);
        }

        WriteSnapshot();
        DrainCommand();
    }

    public static void Stop()
    {
        lock (Gate)
        {
            if (!_started)
                return;

            _started = false;
            _incoming = null;
            _outgoing = null;
            _serverDesired = false;
            _runtime = AppRuntimeState.Initial with { NodeState = LocalSendNodeState.Stopped };
        }

        WriteSnapshot();

        lock (Gate)
        {
            _commandWait?.Unregister(null);
            _commandWait = null;
            _commandEvent?.Dispose();
            _commandEvent = null;
            _runningMutex?.Dispose();
            _runningMutex = null;
        }
    }

    public static void Update(
        AppRuntimeState runtime,
        AppSettings settings,
        OutgoingTransferViewState? outgoing,
        bool serverDesired)
    {
        lock (Gate)
        {
            _runtime = runtime;
            _settings = settings;
            _outgoing = outgoing;
            _serverDesired = serverDesired;
        }

        WriteSnapshot();
    }

    public static void SetIncoming(WidgetTransferInfo? incoming)
    {
        lock (Gate)
            _incoming = incoming;
        WriteSnapshot();
    }

    private static void DrainCommand()
    {
        var path = Path.Combine(AppPlatform.DataDirectory, CommandFileName);
        string? verb = null;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    AppDiagnostics.Report("Could not delete a consumed widget command", exception);
                }

                var command = JsonSerializer.Deserialize(json, WidgetHostJsonContext.Default.WidgetCommandFile);
                verb = command?.Verb?.Trim();
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Report("Could not read the widget command", exception);
        }

        if (string.IsNullOrWhiteSpace(verb))
            return;

        Dispatch(() => CommandReceived?.Invoke(verb));
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = ReactorApp.UIDispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () => action());
    }

    private static void WriteSnapshot()
    {
        AppRuntimeState runtime;
        AppSettings settings;
        OutgoingTransferViewState? outgoing;
        WidgetTransferInfo? incoming;
        bool serverDesired;
        lock (Gate)
        {
            runtime = _runtime;
            settings = _settings;
            outgoing = _outgoing;
            incoming = _incoming;
            serverDesired = _serverDesired;
        }

        var t = AppIntl.For(settings);
        var transfer = incoming is null
            ? OfferTransfer(runtime, t) ?? OutgoingTransfer(outgoing)
            : ToFile(incoming);
        var snapshot = new WidgetSnapshotFile
        {
            Schema = 2,
            ServerRunning = runtime.NodeState == LocalSendNodeState.Running,
            ServerBusy = runtime.NodeState is LocalSendNodeState.Starting or LocalSendNodeState.Stopping,
            ServerDesired = serverDesired,
            Alias = settings.ResolvedAlias,
            Language = AppLocale.Resolve(settings.LanguageIndex),
            Chrome = Chrome(t),
            Devices = runtime.NodeState == LocalSendNodeState.Running
                ?
                [
                    .. runtime.Devices.Select(static device => new WidgetDeviceFile
                    {
                        Alias = device.Alias,
                        Type = device.DeviceType.ToString().ToLowerInvariant(),
                    })
                ]
                : [],
            Transfer = transfer,
        };

        try
        {
            Directory.CreateDirectory(AppPlatform.DataDirectory);
            var path = Path.Combine(AppPlatform.DataDirectory, SnapshotFileName);
            var temp = path + ".tmp";
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(snapshot, WidgetHostJsonContext.Default.WidgetSnapshotFile));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Report("Could not write the widget snapshot", exception);
        }
    }

    private static WidgetTransferFile ToFile(WidgetTransferInfo incoming) => new()
    {
        Incoming = true,
        Title = incoming.Title,
        Peer = incoming.Peer,
        Status = incoming.Status,
        BytesTransferred = incoming.BytesTransferred,
        TotalBytes = incoming.TotalBytes,
        Indeterminate = incoming.Indeterminate,
    };

    private static WidgetTransferFile? OfferTransfer(AppRuntimeState runtime, IntlAccessor t)
    {
        var request = runtime.IncomingTransfers.FirstOrDefault();
        if (request is null)
            return null;

        var title = request.Items.Count == 1
            ? request.Items[0].FileName
            : t.Message(new("App", "WidgetFileCount"), ("count", request.Items.Count));
        return new WidgetTransferFile
        {
            Incoming = true,
            Title = title,
            Peer = request.Sender.Alias,
            Status = t.Message(new("App", "TrayWaitingReceive")),
            TotalBytes = request.Items.Sum(static item => item.Size),
            Indeterminate = true,
        };
    }

    private static WidgetChromeFile Chrome(IntlAccessor t) => new()
    {
        AppStatusOpen = t.Message(new("App", "WidgetAppOpen")),
        AppStatusClosed = t.Message(new("App", "WidgetAppClosedStatus")),
        ServerLabel = t.Message(new("App", "TrayReceiveService")),
        ServerOn = t.Message(new("App", "WidgetServerOn")),
        ServerOff = t.Message(new("App", "WidgetServerOff")),
        HintAppClosed = t.Message(new("App", "WidgetHintAppClosed")),
        HintServerOn = t.Message(new("App", "WidgetHintServerOn")),
        HintServerOff = t.Message(new("App", "WidgetHintServerOff")),
        EmptyNoDevices = t.Message(new("App", "TrayNoDevices")),
        EmptyReceivingOff = t.Message(new("App", "WidgetReceivingOff")),
        EmptyAppClosed = t.Message(new("App", "WidgetAppClosed")),
        HistoryEmpty = t.Message(new("App", "HistoryEmpty")),
        NearbyTab = t.Message(new("App", "TrayNearbyTab")),
        HistoryTab = t.Message(new("App", "TrayHistoryTab")),
        NearbyCount = t.Message(new("App", "WidgetNearbyCount")),
        HistoryCount = t.Message(new("App", "WidgetHistoryCount")),
        FromPeer = t.Message(new("App", "TrayFromPeer")),
        ToPeer = t.Message(new("App", "TrayToPeer")),
    };

    private static WidgetTransferFile? OutgoingTransfer(OutgoingTransferViewState? outgoing)
    {
        if (outgoing is not OutgoingTransferViewState.Pending(var transfer))
            return null;

        return new WidgetTransferFile
        {
            Incoming = false,
            Title = transfer.ContentSummary,
            Peer = transfer.Receiver.Alias,
            Status = transfer.Status,
            BytesTransferred = transfer.BytesTransferred,
            TotalBytes = transfer.TotalBytes,
            Indeterminate = transfer.TotalBytes <= 0
                            || transfer.State is TransferState.Preparing or TransferState.WaitingForAcceptance,
        };
    }

}

sealed record WidgetTransferInfo(
    string Title,
    string Peer,
    string Status,
    long BytesTransferred,
    long TotalBytes,
    bool Indeterminate);

sealed class WidgetSnapshotFile
{
    public int Schema { get; set; } = 1;
    public bool ServerRunning { get; set; }
    public bool ServerBusy { get; set; }
    public bool ServerDesired { get; set; }
    public string? Alias { get; set; }
    public string? Language { get; set; }
    public List<WidgetDeviceFile>? Devices { get; set; }
    public WidgetTransferFile? Transfer { get; set; }
    public WidgetChromeFile? Chrome { get; set; }
}

sealed class WidgetChromeFile
{
    public string? AppStatusOpen { get; set; }
    public string? AppStatusClosed { get; set; }
    public string? ServerLabel { get; set; }
    public string? ServerOn { get; set; }
    public string? ServerOff { get; set; }
    public string? HintAppClosed { get; set; }
    public string? HintServerOn { get; set; }
    public string? HintServerOff { get; set; }
    public string? EmptyNoDevices { get; set; }
    public string? EmptyReceivingOff { get; set; }
    public string? EmptyAppClosed { get; set; }
    public string? HistoryEmpty { get; set; }
    public string? NearbyTab { get; set; }
    public string? HistoryTab { get; set; }
    public string? NearbyCount { get; set; }
    public string? HistoryCount { get; set; }
    public string? FromPeer { get; set; }
    public string? ToPeer { get; set; }
}

sealed class WidgetDeviceFile
{
    public string? Alias { get; set; }
    public string? Type { get; set; }
}

sealed class WidgetTransferFile
{
    public bool Incoming { get; set; }
    public string? Title { get; set; }
    public string? Peer { get; set; }
    public string? Status { get; set; }
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public bool Indeterminate { get; set; }
}

sealed class WidgetCommandFile
{
    public string? Verb { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WidgetSnapshotFile))]
[JsonSerializable(typeof(WidgetChromeFile))]
[JsonSerializable(typeof(WidgetCommandFile))]
internal sealed partial class WidgetHostJsonContext : JsonSerializerContext;
