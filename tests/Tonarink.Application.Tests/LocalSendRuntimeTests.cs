using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Tonarink.Application;
using Tonarink.LocalSend;

namespace Tonarink.Application.Tests;

[TestClass]
public sealed class LocalSendRuntimeTests
{
    [TestMethod]
    [Timeout(30_000)]
    public async Task TextAndFileTransferWriteExpectedBytes()
    {
        await using var pair = await RuntimePair.StartAsync();
        var text = "hello from hybrid runtime";
        var payload = Encoding.UTF8.GetBytes("file-bytes-42");
        var filePath = Path.Combine(pair.Root, "payload.bin");
        await File.WriteAllBytesAsync(filePath, payload);

        var sendTask = pair.Sender.SendToAddressAsync($"127.0.0.1:{pair.ReceiverPort}",
        [
            new ShareItem(Guid.NewGuid(), "message.txt", Encoding.UTF8.GetByteCount(text), "text/plain", TextContent: text),
            new ShareItem(Guid.NewGuid(), "payload.bin", payload.Length, "application/octet-stream", filePath,
                OpenRead: _ => ValueTask.FromResult<Stream>(File.OpenRead(filePath))),
        ]);
        var offer = await pair.WaitForOfferAsync(sendTask);
        Assert.Contains(item => item.Name == "message.txt", offer.Items);
        await pair.Receiver.AcceptAsync(offer, offer.Items.Select(static item => item.Id).ToHashSet());
        await sendTask;

        Assert.AreEqual(text, await File.ReadAllTextAsync(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "message.txt")));
        Assert.AreSequenceEqual(payload, await File.ReadAllBytesAsync(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "payload.bin")));
        Assert.AreEqual(TransferStatus.Completed, pair.ReceiverState.Transfers[0].Status);
        Assert.AreEqual(TransferStatus.Completed, pair.SenderState.Transfers[0].Status);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task SendWithoutRequiredPinReturnsOutcomeAndSucceedsWithPin()
    {
        await using var pair = await RuntimePair.StartAsync(receiver => receiver with { ReceivePin = "2468" });
        var item = new ShareItem(Guid.NewGuid(), "secret.txt", 6, "text/plain", TextContent: "secret");

        var missingPin = await pair.Sender.SendToAddressAsync(
            $"127.0.0.1:{pair.ReceiverPort}",
            [item]);
        Assert.IsFalse(RequirePinRequired(missingPin).InvalidPin);

        var sendTask = pair.Sender.SendToAddressAsync($"127.0.0.1:{pair.ReceiverPort}", [item], pin: "2468");
        var offer = await pair.WaitForOfferAsync(sendTask);
        await pair.Receiver.AcceptAsync(offer, offer.Items.Select(static item => item.Id).ToHashSet());
        await sendTask;
        Assert.AreEqual(TransferStatus.Completed, pair.SenderState.Transfers[0].Status);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task PartialAcceptSavesOnlySelectedItems()
    {
        await using var pair = await RuntimePair.StartAsync();
        var sendTask = pair.Sender.SendToAddressAsync($"127.0.0.1:{pair.ReceiverPort}",
        [
            new ShareItem(Guid.NewGuid(), "keep.txt", 4, "text/plain", TextContent: "keep"),
            new ShareItem(Guid.NewGuid(), "skip.txt", 4, "text/plain", TextContent: "skip"),
        ]);
        var offer = await pair.WaitForOfferAsync(sendTask);
        var keep = Assert.ContainsSingle(offer.Items.Where(item => item.Name == "keep.txt"));
        await pair.Receiver.AcceptAsync(offer, new HashSet<Guid> { keep.Id });
        await sendTask;

        Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "keep.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "skip.txt")));
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task AutoAcceptSavesWithoutUiDecision()
    {
        await using var pair = await RuntimePair.StartAsync(receiver => receiver with { AutoAccept = true });
        var payload = "auto-accepted"u8.ToArray();
        var filePath = Path.Combine(pair.Root, "auto.bin");
        await File.WriteAllBytesAsync(filePath, payload);

        await pair.Sender.SendToAddressAsync($"127.0.0.1:{pair.ReceiverPort}",
        [
            new ShareItem(Guid.NewGuid(), "auto.bin", payload.Length, "application/octet-stream", filePath,
                OpenRead: _ => ValueTask.FromResult<Stream>(File.OpenRead(filePath))),
        ]);

        await WaitUntilAsync(() => File.Exists(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "auto.bin")), TimeSpan.FromSeconds(15));
        Assert.AreSequenceEqual(payload, await File.ReadAllBytesAsync(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "auto.bin")));
        Assert.IsEmpty(pair.ReceiverState.IncomingOffers);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task AliasAndReceivePinChangesApplyAfterRestart()
    {
        await using var pair = await RuntimePair.StartAsync(receiver => receiver with { Alias = "Original", ReceivePin = null });
        await pair.Receiver.StopAsync();
        await pair.ReceiverState.UpdateSettingsAsync(settings => settings with { Alias = "Renamed", ReceivePin = "9999" });
        await pair.Receiver.StartAsync();

        var missingPin = await pair.Sender.SendToAddressAsync(
            $"127.0.0.1:{pair.ReceiverPort}",
            [new ShareItem(Guid.NewGuid(), "ping.txt", 4, "text/plain", TextContent: "ping")]);
        Assert.IsFalse(RequirePinRequired(missingPin).InvalidPin);

        var sendTask = pair.Sender.SendToAddressAsync($"127.0.0.1:{pair.ReceiverPort}",
            [new ShareItem(Guid.NewGuid(), "ping.txt", 4, "text/plain", TextContent: "ping")], pin: "9999");
        var offer = await pair.WaitForOfferAsync(sendTask);
        Assert.AreEqual("Sender", offer.SenderAlias);
        Assert.Contains(device => device.Alias == "Renamed", pair.SenderState.Devices);
        await pair.Receiver.AcceptAsync(offer, offer.Items.Select(static item => item.Id).ToHashSet());
        await sendTask;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Timed out waiting for the expected condition.");
    }

    private static RuntimeSendResult.PinRequired RequirePinRequired(RuntimeSendResult result) => result switch
    {
        RuntimeSendResult.PinRequired required => required,
        _ => throw new AssertFailedException("Expected PIN-required result."),
    };
}


internal sealed class RuntimePair : IAsyncDisposable
{
    private RuntimePair(
        string root,
        TestPlatform senderPlatform,
        TestPlatform receiverPlatform,
        TonarinkAppState senderState,
        TonarinkAppState receiverState,
        LocalSendRuntime sender,
        LocalSendRuntime receiver,
        int receiverPort,
        RuntimeLog log)
    {
        Root = root;
        SenderPlatform = senderPlatform;
        ReceiverPlatform = receiverPlatform;
        SenderState = senderState;
        ReceiverState = receiverState;
        Sender = sender;
        Receiver = receiver;
        ReceiverPort = receiverPort;
        _log = log;
    }

    public string Root { get; }
    public TestPlatform SenderPlatform { get; }
    public TestPlatform ReceiverPlatform { get; }
    public TonarinkAppState SenderState { get; }
    public TonarinkAppState ReceiverState { get; }
    public LocalSendRuntime Sender { get; }
    public LocalSendRuntime Receiver { get; }
    public int ReceiverPort { get; }
    private readonly RuntimeLog _log;

    public static async Task<RuntimePair> StartAsync(Func<TonarinkSettings, TonarinkSettings>? configureReceiver = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "Tonarink-LocalSendTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var senderPort = GetFreePort();
        var receiverPort = GetFreePort();
        var senderPlatform = new TestPlatform(Path.Combine(root, "sender"));
        var receiverPlatform = new TestPlatform(Path.Combine(root, "receiver"));
        var senderState = new TonarinkAppState(senderPlatform);
        var receiverState = new TonarinkAppState(receiverPlatform);
        await senderState.InitializeAsync();
        await receiverState.InitializeAsync();
        await senderState.UpdateSettingsAsync(settings => settings with { Alias = "Sender", Port = senderPort });
        await receiverState.UpdateSettingsAsync(settings =>
        {
            var updated = settings with { Alias = "Receiver", Port = receiverPort };
            return configureReceiver is null ? updated : configureReceiver(updated);
        });
        var log = new RuntimeLog();
        var sender = new LocalSendRuntime(senderState, senderPlatform, log);
        var receiver = new LocalSendRuntime(receiverState, receiverPlatform, log);
        var pair = new RuntimePair(root, senderPlatform, receiverPlatform, senderState, receiverState, sender, receiver, receiverPort, log);
        try
        {
            await Task.WhenAll(receiver.StartAsync(), sender.StartAsync());
            return pair;
        }
        catch
        {
            await pair.DisposeAsync();
            throw;
        }
    }

    public async Task<IncomingOffer> WaitForOfferAsync(Task<RuntimeSendResult> sender, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<IncomingOffer>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler()
        {
            if (ReceiverState.IncomingOffers.Count > 0)
                tcs.TrySetResult(ReceiverState.IncomingOffers[0]);
        }
        ReceiverState.Changed += Handler;
        try
        {
            Handler();
            // A failed sender cannot ever publish an offer. Observe it rather
            // than replacing its useful error with a receiver-wait timeout.
            var completed = await Task.WhenAny(tcs.Task, sender)
                .WaitAsync(timeout ?? TimeSpan.FromSeconds(20));
            if (completed == sender && !tcs.Task.IsCompleted)
            {
                var result = await sender.ConfigureAwait(false);
                Assert.Fail($"Sender finished before an incoming offer: {result}; receiver port={ReceiverPort}, running={Receiver.IsRunning}.");
            }
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            ReceiverState.Changed -= Handler;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Sender.DisposeAsync();
        await Receiver.DisposeAsync();
        _log.Dispose();
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

// Bounded, test-owned diagnostics are captured in TRX stdout. Never discard the
// HTTPS failure just because the production address fallback later fails HTTP.
internal sealed class RuntimeLog : ILoggerFactory
{
    private readonly ConcurrentQueue<string> _lines = new();
    public ILogger CreateLogger(string categoryName) => new CapturedLogger(categoryName, _lines);
    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
    public void Dispose() => Console.WriteLine(string.Join(Environment.NewLine, _lines));

    private sealed class CapturedLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            lines.Enqueue($"{DateTimeOffset.UtcNow:O} {logLevel} {category}: {formatter(state, exception)} {exception}");
            while (lines.Count > 200)
                lines.TryDequeue(out _);
        }
    }
}

internal sealed class TestPlatform : IPlatformServices
{
    private readonly ImportedShareStore _imports;

    public TestPlatform(string root)
    {
        DataDirectory = Path.Combine(root, "data");
        DownloadDirectory = Path.Combine(root, "downloads");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(DownloadDirectory);
        _imports = new ImportedShareStore(Path.Combine(root, "shared"));
    }

    public TonarinkSettings? Stored { get; set; }
    public PlatformCapabilities Capabilities => new("Test", true, true, false, true, true, true, false, []);
    public string DataDirectory { get; }
    public string DownloadDirectory { get; }
    public string DefaultAlias => "Test device";
    public string DeviceModel => "Test model";
    public TonarinkDeviceKind DeviceKind => TonarinkDeviceKind.Desktop;
    public Task<TonarinkSettings?> LoadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored);
    public Task SaveSettingsAsync(TonarinkSettings settings, CancellationToken cancellationToken = default)
    {
        Stored = settings;
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<ShareItem>> PickFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ShareItem>>([]);
    public Task<IReadOnlyList<ShareItem>> PickFoldersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ShareItem>>([]);
    public Task<ShareItem> ImportSharedFileAsync(string fileName, Stream source, string contentType, CancellationToken cancellationToken = default) =>
        _imports.ImportAsync(fileName, source, contentType, cancellationToken);
    public void ReleaseShareItem(ShareItem item) => _imports.Release(item);
    public void ReleaseUnreferencedShareItems(IReadOnlyList<ShareItem> stillHeld) => _imports.CleanupUnreferenced(stillHeld);
    public ValueTask<Stream> OpenReadAsync(ShareItem item, CancellationToken cancellationToken = default)
    {
        if (item.OpenRead is not null)
            return item.OpenRead(cancellationToken);
        if (item.NativePath is { Length: > 0 } path)
            return ValueTask.FromResult<Stream>(File.OpenRead(path));
        return ValueTask.FromResult<Stream>(Stream.Null);
    }
    public Task<string?> ReadClipboardTextAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task WriteClipboardTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PublishReceivedFileAsync(string path, string contentType, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
