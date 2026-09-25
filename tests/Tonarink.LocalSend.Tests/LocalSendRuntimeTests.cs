using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Tonarink.Application;
using Tonarink.LocalSend;

namespace Tonarink.LocalSend.Tests;

[Collection(nameof(LocalSendRuntimeCollection))]
public sealed class LocalSendRuntimeTests
{
    [Fact(Timeout = 30_000)]
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
        var offer = await pair.WaitForOfferAsync();
        Assert.Contains(offer.Items, item => item.Name == "message.txt");
        await pair.Receiver.AcceptAsync(offer, offer.Items.Select(static item => item.Id).ToHashSet());
        await sendTask;

        Assert.Equal(text, await File.ReadAllTextAsync(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "message.txt")));
        Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "payload.bin")));
        Assert.Equal(TransferStatus.Completed, pair.ReceiverState.Transfers[0].Status);
        Assert.Equal(TransferStatus.Completed, pair.SenderState.Transfers[0].Status);
    }

    [Fact(Timeout = 30_000)]
    public async Task SendWithoutRequiredPinReturnsOutcomeAndSucceedsWithPin()
    {
        await using var pair = await RuntimePair.StartAsync(receiver => receiver with { ReceivePin = "2468" });
        var item = new ShareItem(Guid.NewGuid(), "secret.txt", 6, "text/plain", TextContent: "secret");

        var missingPin = await pair.Sender.SendToAddressAsync(
            $"127.0.0.1:{pair.ReceiverPort}",
            [item]);
        Assert.False(RequirePinRequired(missingPin).InvalidPin);

        var sendTask = pair.Sender.SendToAddressAsync($"127.0.0.1:{pair.ReceiverPort}", [item], pin: "2468");
        var offer = await pair.WaitForOfferAsync();
        await pair.Receiver.AcceptAsync(offer, offer.Items.Select(static item => item.Id).ToHashSet());
        await sendTask;
        Assert.Equal(TransferStatus.Completed, pair.SenderState.Transfers[0].Status);
    }

    [Fact(Timeout = 30_000)]
    public async Task PartialAcceptSavesOnlySelectedItems()
    {
        await using var pair = await RuntimePair.StartAsync();
        var sendTask = pair.Sender.SendToAddressAsync($"127.0.0.1:{pair.ReceiverPort}",
        [
            new ShareItem(Guid.NewGuid(), "keep.txt", 4, "text/plain", TextContent: "keep"),
            new ShareItem(Guid.NewGuid(), "skip.txt", 4, "text/plain", TextContent: "skip"),
        ]);
        var offer = await pair.WaitForOfferAsync();
        var keep = Assert.Single(offer.Items, item => item.Name == "keep.txt");
        await pair.Receiver.AcceptAsync(offer, new HashSet<Guid> { keep.Id });
        await sendTask;

        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "skip.txt")));
    }

    [Fact(Timeout = 30_000)]
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
        Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, "auto.bin")));
        Assert.Empty(pair.ReceiverState.IncomingOffers);
    }

    [Fact(Timeout = 30_000)]
    public async Task AliasAndReceivePinChangesApplyAfterRestart()
    {
        await using var pair = await RuntimePair.StartAsync(receiver => receiver with { Alias = "Original", ReceivePin = null });
        await pair.Receiver.StopAsync();
        await pair.ReceiverState.UpdateSettingsAsync(settings => settings with { Alias = "Renamed", ReceivePin = "9999" });
        await pair.Receiver.StartAsync();

        var missingPin = await pair.Sender.SendToAddressAsync(
            $"127.0.0.1:{pair.ReceiverPort}",
            [new ShareItem(Guid.NewGuid(), "ping.txt", 4, "text/plain", TextContent: "ping")]);
        Assert.False(RequirePinRequired(missingPin).InvalidPin);

        var sendTask = pair.Sender.SendToAddressAsync($"127.0.0.1:{pair.ReceiverPort}",
            [new ShareItem(Guid.NewGuid(), "ping.txt", 4, "text/plain", TextContent: "ping")], pin: "9999");
        var offer = await pair.WaitForOfferAsync();
        Assert.Equal("Sender", offer.SenderAlias);
        Assert.Contains(pair.SenderState.Devices, device => device.Alias == "Renamed");
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
        _ => throw new Xunit.Sdk.XunitException("Expected PIN-required result."),
    };
}

[CollectionDefinition(nameof(LocalSendRuntimeCollection), DisableParallelization = true)]
public sealed class LocalSendRuntimeCollection;

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
        int receiverPort)
    {
        Root = root;
        SenderPlatform = senderPlatform;
        ReceiverPlatform = receiverPlatform;
        SenderState = senderState;
        ReceiverState = receiverState;
        Sender = sender;
        Receiver = receiver;
        ReceiverPort = receiverPort;
    }

    public string Root { get; }
    public TestPlatform SenderPlatform { get; }
    public TestPlatform ReceiverPlatform { get; }
    public TonarinkAppState SenderState { get; }
    public TonarinkAppState ReceiverState { get; }
    public LocalSendRuntime Sender { get; }
    public LocalSendRuntime Receiver { get; }
    public int ReceiverPort { get; }

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
        var sender = new LocalSendRuntime(senderState, senderPlatform, NullLoggerFactory.Instance);
        var receiver = new LocalSendRuntime(receiverState, receiverPlatform, NullLoggerFactory.Instance);
        var pair = new RuntimePair(root, senderPlatform, receiverPlatform, senderState, receiverState, sender, receiver, receiverPort);
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

    public async Task<IncomingOffer> WaitForOfferAsync(TimeSpan? timeout = null)
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
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(20));
            await using var registration = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
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
