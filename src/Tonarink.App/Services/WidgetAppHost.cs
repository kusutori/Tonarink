using System.Text.Json;
using System.Text.Json.Serialization;
using LocalSendDotNet;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;

static class WidgetAppHost
{
    public const string MutexName = @"Local\Tonarink.App.Running";
    public const string CommandEventName = @"Local\Tonarink.WidgetCommand";
    public const string SnapshotFileName = "widget-snapshot.json";
    public const string CommandFileName = "widget-command.json";

    private static readonly object Gate = new();
    private static Mutex? RunningMutex;
    private static EventWaitHandle? CommandEvent;
    private static RegisteredWaitHandle? CommandWait;
    private static bool started;

    private static AppRuntimeState Runtime = AppRuntimeState.Initial;
    private static AppSettings Settings = AppSettings.Default;
    private static OutgoingTransferViewState? Outgoing;
    private static WidgetTransferInfo? Incoming;
    private static bool ServerDesired = true;

    public static event Action<string>? CommandReceived;

    public static void Start()
    {
        lock (Gate)
        {
            if (started)
                return;

            started = true;
            RunningMutex = new Mutex(initiallyOwned: true, MutexName, out _);
            CommandEvent = new EventWaitHandle(false, EventResetMode.AutoReset, CommandEventName);
            CommandWait = ThreadPool.RegisterWaitForSingleObject(
                CommandEvent,
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
            if (!started)
                return;

            started = false;
            Incoming = null;
            Outgoing = null;
            ServerDesired = false;
            Runtime = AppRuntimeState.Initial with { NodeState = LocalSendNodeState.Stopped };
        }

        WriteSnapshot();

        lock (Gate)
        {
            CommandWait?.Unregister(null);
            CommandWait = null;
            CommandEvent?.Dispose();
            CommandEvent = null;
            RunningMutex?.Dispose();
            RunningMutex = null;
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
            Runtime = runtime;
            Settings = settings;
            Outgoing = outgoing;
            ServerDesired = serverDesired;
        }

        WriteSnapshot();
    }

    public static void SetIncoming(WidgetTransferInfo? incoming)
    {
        lock (Gate)
            Incoming = incoming;
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
            runtime = Runtime;
            settings = Settings;
            outgoing = Outgoing;
            incoming = Incoming;
            serverDesired = ServerDesired;
        }

        var transfer = incoming is null
            ? OfferTransfer(runtime, settings.LanguageIndex) ?? OutgoingTransfer(outgoing)
            : ToFile(incoming);
        var snapshot = new WidgetSnapshotFile
        {
            Schema = 1,
            ServerRunning = runtime.NodeState == LocalSendNodeState.Running,
            ServerBusy = runtime.NodeState is LocalSendNodeState.Starting or LocalSendNodeState.Stopping,
            ServerDesired = serverDesired,
            Alias = settings.ResolvedAlias,
            Language = settings.LanguageIndex switch
            {
                1 => "zh-CN",
                2 => "en-US",
                _ => null,
            },
            Devices = runtime.NodeState == LocalSendNodeState.Running
                ? runtime.Devices.Select(static device => new WidgetDeviceFile
                {
                    Alias = device.Alias,
                    Type = device.DeviceType.ToString().ToLowerInvariant(),
                }).ToList()
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

    private static WidgetTransferFile? OfferTransfer(AppRuntimeState runtime, int languageIndex)
    {
        var request = runtime.IncomingTransfers.FirstOrDefault();
        if (request is null)
            return null;

        var chinese = IsChinese(languageIndex);
        var title = request.Items.Count == 1
            ? request.Items[0].FileName
            : chinese
                ? $"{request.Items.Count} 个文件"
                : $"{request.Items.Count} files";
        return new WidgetTransferFile
        {
            Incoming = true,
            Title = title,
            Peer = request.Sender.Alias,
            Status = chinese ? "等待接收" : "Waiting to receive",
            TotalBytes = request.Items.Sum(static item => item.Size),
            Indeterminate = true,
        };
    }

    private static WidgetTransferFile? OutgoingTransfer(OutgoingTransferViewState? outgoing)
    {
        if (outgoing is not { IsPending: true })
            return null;

        return new WidgetTransferFile
        {
            Incoming = false,
            Title = outgoing.ContentSummary,
            Peer = outgoing.Receiver.Alias,
            Status = outgoing.Status,
            BytesTransferred = outgoing.BytesTransferred,
            TotalBytes = outgoing.TotalBytes,
            Indeterminate = outgoing.TotalBytes <= 0
                || outgoing.State is TransferState.Preparing or TransferState.WaitingForAcceptance,
        };
    }

    private static bool IsChinese(int languageIndex) =>
        languageIndex == 1
        || (languageIndex == 0 && WidgetLocale.IsChinese());
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

static class WidgetLocale
{
    public static bool IsChinese()
    {
        try
        {
            return Windows.System.UserProfile.GlobalizationPreferences.Languages.Any(static language =>
                language.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            AppDiagnostics.Report("Could not resolve the system language for the widget", exception);
            return false;
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WidgetSnapshotFile))]
[JsonSerializable(typeof(WidgetCommandFile))]
internal sealed partial class WidgetHostJsonContext : JsonSerializerContext;
