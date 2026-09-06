using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

static class ShareTargetActivationBroker
{
    private const string InstanceKey = "Tonarink.Primary";
    private const string IngestedEventName = @"Local\Tonarink.ShareIngested";
    private const string ShareEventName = @"Local\Tonarink.ExplorerShare";
    private static readonly ConcurrentQueue<ShareTargetPayload> PendingPayloads = new();
    private static readonly EventWaitHandle Ingested = new(
        initialState: true,
        mode: EventResetMode.ManualReset,
        name: IngestedEventName);
    private static readonly object ExplorerShareGate = new();
    private static FileSystemWatcher? ExplorerShareWatcher;
    private static EventWaitHandle? ExplorerShareEvent;
    private static RegisteredWaitHandle? ExplorerShareWait;
    private static string LogFilePath => Path.Combine(AppPlatform.DataDirectory, "share-target.log");

    public static event EventHandler? ActivationReceived;

    public static bool HasPendingActivations => !PendingPayloads.IsEmpty;

    public static async Task<bool> RedirectToPrimaryInstanceAsync(Action? beforePrimaryActivationRead = null)
    {
        if (!AppPlatform.HasPackageIdentity())
        {
            beforePrimaryActivationRead?.Invoke();
            DrainExplorerShare();
            StartExplorerShareWatch();
            return false;
        }

        var current = AppInstance.GetCurrent();
        var primary = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!primary.IsCurrent)
        {
            Ingested.Reset();
            await primary.RedirectActivationToAsync(current.GetActivatedEventArgs());
            Ingested.WaitOne(TimeSpan.FromSeconds(15));
            return true;
        }

        primary.Activated += OnActivated;
        beforePrimaryActivationRead?.Invoke();
        await IngestAsync(current.GetActivatedEventArgs()).ConfigureAwait(true);
        StartExplorerShareWatch();
        return false;
    }

    public static bool TryDequeue(out ShareTargetPayload? payload) =>
        PendingPayloads.TryDequeue(out payload);

    private static void OnActivated(object? sender, AppActivationArguments activation) =>
        _ = IngestAsync(activation);

    private static async Task IngestAsync(AppActivationArguments? activation)
    {
        try
        {
            // AppActivationArguments.Data exposes the activation *interface*, not
            // necessarily the projected runtime class. The class check happens to
            // work in JIT builds, but can fail after Native AOT trimming because the
            // inspectable is materialized directly as the WinRT interface.
            if (activation?.Kind == ExtendedActivationKind.ShareTarget
                && activation.Data is IShareTargetActivatedEventArgs shareArgs)
            {
                var payload = await CaptureSharePayloadAsync(shareArgs).ConfigureAwait(false);
                if (payload is not null)
                    PendingPayloads.Enqueue(payload);
            }
            else if (activation?.Kind == ExtendedActivationKind.ShareTarget)
            {
                WriteDiagnostic(
                    $"Share activation data did not expose {nameof(IShareTargetActivatedEventArgs)} " +
                    $"(runtime type: {activation.Data?.GetType().FullName ?? "<null>"}).");
            }
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Failed to ingest a share activation.", exception);
        }
        finally
        {
            DrainExplorerShare();
            try
            {
                Ingested.Set();
            }
            catch
            {
            }

            ActivationReceived?.Invoke(null, EventArgs.Empty);
        }
    }

    private static void StartExplorerShareWatch()
    {
        if (ExplorerShareWatcher is not null)
            return;

        var directory = AppPlatform.ExplorerShareDirectory;
        DrainExplorerShare();
        var watcher = new FileSystemWatcher(directory)
        {
            Filter = "*.txt",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        watcher.Created += (_, _) => DrainExplorerShare();
        watcher.Changed += (_, _) => DrainExplorerShare();
        watcher.Renamed += (_, _) => DrainExplorerShare();
        ExplorerShareWatcher = watcher;

        ExplorerShareEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShareEventName);
        ExplorerShareWait = ThreadPool.RegisterWaitForSingleObject(
            ExplorerShareEvent,
            static (_, _) => DrainExplorerShare(),
            null,
            -1,
            executeOnlyOnce: false);
    }

    private static void DrainExplorerShare()
    {
        var directory = AppPlatform.ExplorerShareDirectory;
        if (!Directory.Exists(directory))
            return;

        var ingested = false;
        lock (ExplorerShareGate)
        {
            foreach (var file in Directory.GetFiles(directory, "*.txt"))
            {
                try
                {
                    string[] paths;
                    try
                    {
                        paths = File.ReadAllLines(file)
                            .Select(static path => path.Trim())
                            .Where(static path => path.Length > 0 && Path.Exists(path))
                            .ToArray();
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    File.Delete(file);
                    if (paths.Length == 0)
                        continue;

                    var items = paths
                        .Select(static path => (ShareTargetItem)new ShareTargetItem.FileSystem(
                            path,
                            Directory.Exists(path)))
                        .ToArray();
                    PendingPayloads.Enqueue(new ShareTargetPayload(Guid.NewGuid(), items));
                    ingested = true;
                }
                catch
                {
                }
            }
        }

        if (ingested)
            ActivationReceived?.Invoke(null, EventArgs.Empty);
    }

    private static Task<ShareTargetPayload?> CaptureSharePayloadAsync(
        IShareTargetActivatedEventArgs shareArgs)
    {
        var dispatcher = ReactorApp.UIDispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
            return CaptureSharePayloadCoreAsync(shareArgs);

        var completion = new TaskCompletionSource<ShareTargetPayload?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.High, () =>
                _ = CompleteOnDispatcherAsync(shareArgs, completion)))
        {
            return CaptureSharePayloadCoreAsync(shareArgs);
        }

        return completion.Task;
    }

    private static async Task CompleteOnDispatcherAsync(
        IShareTargetActivatedEventArgs shareArgs,
        TaskCompletionSource<ShareTargetPayload?> completion)
    {
        try
        {
            completion.TrySetResult(await CaptureSharePayloadCoreAsync(shareArgs).ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private static async Task<ShareTargetPayload?> CaptureSharePayloadCoreAsync(
        IShareTargetActivatedEventArgs shareArgs)
    {
        var operation = shareArgs.ShareOperation;
        operation.ReportStarted();
        try
        {
            var payload = await ReadShareTargetPayloadAsync(operation.Data).ConfigureAwait(true);
            operation.ReportDataRetrieved();
            operation.ReportCompleted();
            return payload;
        }
        catch
        {
            try
            {
                operation.ReportError("The shared content could not be read.");
            }
            catch
            {
            }

            throw;
        }
    }

    private static async Task<ShareTargetPayload> ReadShareTargetPayloadAsync(DataPackageView data)
    {
        var items = new List<ShareTargetItem>();
        if (data.Contains(StandardDataFormats.StorageItems))
        {
            var storageItems = await data.GetStorageItemsAsync();
            foreach (var storageItem in storageItems)
            {
                if (string.IsNullOrWhiteSpace(storageItem.Path))
                    continue;

                items.Add(new ShareTargetItem.FileSystem(
                    storageItem.Path,
                    storageItem is StorageFolder));
            }
        }
        else if (data.Contains(StandardDataFormats.WebLink))
        {
            var link = await data.GetWebLinkAsync();
            items.Add(new ShareTargetItem.Text(link.ToString(), "shared-link.txt"));
        }
        else if (data.Contains(StandardDataFormats.Text))
        {
            var text = await data.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
                items.Add(new ShareTargetItem.Text(text, "shared-text.txt"));
        }

        if (items.Count == 0)
            throw new InvalidDataException("The share did not contain accessible files or text.");

        return new ShareTargetPayload(Guid.NewGuid(), items);
    }

    private static void WriteDiagnostic(string message, Exception? exception = null)
    {
        var text = exception is null
            ? $"[share-target] {message}"
            : $"[share-target] {message} {exception}";
        Trace.WriteLine(text);

        try
        {
            Directory.CreateDirectory(AppPlatform.DataDirectory);
            File.AppendAllText(
                LogFilePath,
                $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}
