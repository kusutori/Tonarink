using System.Net;
using System.Net.Sockets;
using System.Text;
using LocalSendDotNet;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Tonarink.Pages.Send;
using Tonarink.Services.Transfers;
using Tonarink.TestSupport;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Tonarink.App.WinUI.Tests;

[TestClass]
public sealed class SelectedSendItemTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task RenameUndoAndRedoPreserveOriginalNamesIdentityAndActualFileSource()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "original.txt");
        var bytes = Encoding.UTF8.GetBytes("原始内容 🦊\r\n");
        await File.WriteAllBytesAsync(path, bytes);
        var original = new SelectedSendItem(Guid.NewGuid(), new SendFileItem(path, "folder/nested/original.txt"),
            "Original display", bytes.Length, SelectedSendItemKind.File, path);

        Assert.AreEqual(original, SelectedSendItemOperations.UndoRename(original));
        Assert.AreEqual(original, SelectedSendItemOperations.RedoRename(original));
        var first = SelectedSendItemOperations.Rename(original, "first.txt");
        var second = SelectedSendItemOperations.Rename(first, "最新.txt");
        Assert.AreEqual("folder/nested/最新.txt", second.Item.FileName);
        Assert.AreEqual(second.Item.FileName, second.DisplayName);
        Assert.AreEqual(original.Item.FileName, second.OriginalFileName);
        Assert.AreEqual(original.DisplayName, second.OriginalDisplayName);
        Assert.IsTrue(second.IsRenamed);
        Assert.IsFalse(second.CanRedoRename);
        Assert.AreEqual(original.Id, second.Id);
        Assert.AreEqual(original.LocalPath, second.LocalPath);
        Assert.AreEqual(original.Length, second.Length);
        Assert.AreEqual(original.Kind, second.Kind);

        var undone = SelectedSendItemOperations.UndoRename(second);
        Assert.IsFalse(undone.IsRenamed);
        Assert.IsTrue(undone.CanRedoRename);
        Assert.AreEqual(original.Item.FileName, undone.Item.FileName);
        Assert.AreEqual(original.DisplayName, undone.DisplayName);
        Assert.AreEqual(second.Item.FileName, undone.RedoFileName);
        var redone = SelectedSendItemOperations.RedoRename(undone);
        Assert.IsTrue(redone.IsRenamed);
        Assert.IsFalse(redone.CanRedoRename);
        Assert.AreEqual(second.Item.FileName, redone.Item.FileName);
        Assert.AreEqual(second.DisplayName, redone.DisplayName);

        var replaced = SelectedSendItemOperations.Rename(undone, "different.txt");
        Assert.IsFalse(replaced.CanRedoRename);
        Assert.IsNull(replaced.RedoFileName);
        Assert.IsNull(replaced.RedoDisplayName);
        var restored = SelectedSendItemOperations.Rename(replaced, "original.txt");
        Assert.IsFalse(restored.IsRenamed);
        Assert.AreEqual(original.DisplayName, restored.DisplayName);
        Assert.AreEqual("folder/nested/original.txt", original.Item.FileName);
        Assert.IsNull(original.OriginalFileName);
        await using var peers = await FileTransferPeers.CreateAsync();
        foreach (var selected in new[] { second, undone, redone, replaced, restored })
        {
            var saved = await peers.TransferAsync(selected);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(saved));
        }
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("", 0L)]
    [DataRow("A\n", 2L)]
    [DataRow("汉字", 6L)]
    [DataRow("🦊", 4L)]
    [DataRow("e\u0301", 3L)]
    public void TextLengthCountsUtf8BytesNotUtf16Characters(string text, long expected) =>
        Assert.AreEqual(expected, SelectedSendItemOperations.TextLength(text));

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("plain.txt", "plain.txt")]
    [DataRow("folder/nested/资料.txt", "资料.txt")]
    [DataRow(".hidden", ".hidden")]
    public void ProtocolLeafNameUsesOnlyTheLastProtocolSeparator(string name, string expected) =>
        Assert.AreEqual(expected, SelectedSendItemOperations.ProtocolLeafName(name));

    [TestMethod]
    [TestCategory("Integration")]
    public async Task FolderReaderIncludesRootAndNestedFilesWithStableProtocolNamesAndContent()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "资料 单元");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["资料 单元/root.bin"] = [0, 1, 255],
            ["资料 单元/nested/雪.txt"] = Encoding.UTF8.GetBytes("Unicode 🦊\n")
        };
        foreach (var (name, bytes) in expected)
            await File.WriteAllBytesAsync(Path.Combine(directory.Path, name.Replace('/', Path.DirectorySeparatorChar)), bytes);

        var items = await SelectedSendItemReader.FromFolderPathAsync(root, default);
        Assert.HasCount(2, items);
        Assert.HasCount(2, items.Select(item => item.Id).Distinct().ToArray());
        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), items.Select(item => item.Item.FileName).ToArray());
        await using var peers = await FileTransferPeers.CreateAsync();
        foreach (var item in items)
        {
            Assert.AreEqual(item.Item.FileName, item.DisplayName);
            Assert.AreEqual(SelectedSendItemKind.File, item.Kind);
            Assert.AreEqual(expected[item.Item.FileName].LongLength, item.Length);
            Assert.IsNotNull(item.LocalPath);
            Assert.IsTrue(Path.IsPathFullyQualified(item.LocalPath));
            Assert.IsTrue(File.Exists(item.LocalPath));
            var saved = await peers.TransferAsync(item);
            CollectionAssert.AreEqual(expected[item.Item.FileName], await File.ReadAllBytesAsync(saved));
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task FolderReaderDistinguishesEmptyMissingAndPreCancelledRequests()
    {
        using var directory = new TemporaryDirectory();
        Assert.IsEmpty(await SelectedSendItemReader.FromFolderPathAsync(directory.Path, default));
        var missing = Path.Combine(directory.Path, "missing");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => SelectedSendItemReader.FromFolderPathAsync(missing, default));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => SelectedSendItemReader.FromFolderPathAsync(missing, cancellation.Token));
        Assert.IsFalse(Directory.Exists(missing));
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(directory.Path).ToArray());
    }

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow(null)]
    public async Task FolderReaderRejectsInaccessibleLocalPath(string? path) =>
        await Assert.ThrowsAsync<IOException>(() => SelectedSendItemReader.FromFolderPathAsync(path!, default));

    [UITestMethod]
    [TestCategory("Integration")]
    public async Task StorageFileReaderUsesRealWindowsFileAndCancellationDoesNotPoisonFactory()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "资料.txt");
        var bytes = Encoding.UTF8.GetBytes("StorageFile 内容 🦊\r\n");
        await File.WriteAllBytesAsync(path, bytes);
        var file = await StorageFile.GetFileFromPathAsync(path);
        var selected = await SelectedSendItemReader.FromStorageFileAsync(file, "folder\\资料.txt", default);
        Assert.AreNotEqual(Guid.Empty, selected.Id);
        Assert.AreEqual("folder/资料.txt", selected.Item.FileName);
        Assert.AreEqual("folder\\资料.txt", selected.DisplayName);
        Assert.AreEqual(path, selected.LocalPath);
        Assert.AreEqual(bytes.LongLength, selected.Length);
        Assert.AreEqual("text/plain", selected.Item.ContentType);
        Assert.AreEqual(SelectedSendItemKind.File, selected.Kind);

        await using var peers = await FileTransferPeers.CreateAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => peers.SendAsync(selected, cancelled.Token));
        Assert.IsEmpty(peers.Receiver.GetPendingIncomingTransfers());
        for (var index = 0; index < 2; index++)
        {
            var result = await peers.TransferAsync(selected);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(result));
        }
        // Opening streams must not retain a handle after the owned stream closes.
        File.Delete(path);
        Assert.IsFalse(File.Exists(path));
        var failedSend = await peers.SendAsync(selected);
        var failure = failedSend switch
        {
            SendOutcome.Failed failed => failed.Failure,
            _ => throw new AssertFailedException("A removed Windows source file must fail to send.")
        };
        Assert.AreEqual(TransferFailureCodes.SourceIo, failure.Code);
        Assert.IsEmpty(peers.Receiver.GetPendingIncomingTransfers());
    }

    [UITestMethod]
    [TestCategory("Integration")]
    public async Task BitmapReaderUsesWindowsDataPackageWithoutChangingSystemClipboard()
    {
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jY8cAAAAASUVORK5CYII=");
        using var source = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(source))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        source.Seek(0);
        var package = new DataPackage();
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(source));
        var selected = await SelectedSendItemReader.FromClipboardBitmapAsync(package.GetView(), default);
        Assert.AreNotEqual(Guid.Empty, selected.Id);
        Assert.AreEqual("clipboard-image.png", selected.Item.FileName);
        Assert.AreEqual(selected.Item.FileName, selected.DisplayName);
        Assert.AreEqual("image/png", selected.Item.ContentType);
        Assert.AreEqual(SelectedSendItemKind.Clipboard, selected.Kind);
        Assert.IsNull(selected.LocalPath);
        Assert.AreEqual(bytes.LongLength, selected.Length);
        await using var peers = await FileTransferPeers.CreateAsync();
        for (var index = 0; index < 2; index++)
        {
            var result = await peers.TransferAsync(selected);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(result));
        }
    }

    // Exercise the private Core stream factory via its production public send
    // API, rather than adding a friend assembly or copying the factory logic.
    private sealed class FileTransferPeers : IAsyncDisposable
    {
        private readonly TemporaryDirectory _directory = new();
        private readonly LocalSendNode _sender;
        private readonly int _receiverPort;
        private LocalSendDevice _target = null!;
        public LocalSendNode Receiver { get; }

        private FileTransferPeers()
        {
            _receiverPort = FreePort();
            _sender = Node("Selection sender", "sender", FreePort());
            Receiver = Node("Selection receiver", "receiver", _receiverPort);
        }

        private LocalSendNode Node(string alias, string name, int port) => new(new LocalSendOptions
        {
            Alias = alias,
            Port = port,
            DataDirectory = Path.Combine(_directory.Path, name, "data"),
            DownloadDirectory = Path.Combine(_directory.Path, name, "downloads"),
            RequestTimeout = TimeSpan.FromSeconds(5),
            IncomingDecisionTimeout = TimeSpan.FromSeconds(5)
        });

        public static async Task<FileTransferPeers> CreateAsync()
        {
            var peers = new FileTransferPeers();
            try
            {
                await Task.WhenAll(peers._sender.StartAsync(), peers.Receiver.StartAsync());
                peers._target = await peers._sender.AddKnownDeviceAsync(
                    new DeviceEndpoint(IPAddress.Loopback, peers._receiverPort, LocalSendProtocol.Https), peers.Receiver.Identity!.Fingerprint);
                return peers;
            }
            catch
            {
                await peers.DisposeAsync();
                throw;
            }
        }

        public Task<SendOutcome> SendAsync(SelectedSendItem selected, CancellationToken cancellationToken = default) =>
            _sender.SendAsync(_target, (SendItem[])[selected.Item], new SendOptions { ComputeSha256 = true }, cancellationToken: cancellationToken);

        public async Task<string> TransferAsync(SelectedSendItem selected)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var receiving = AcceptNextAsync(deadline.Token);
            try
            {
                var sent = await SendAsync(selected, deadline.Token);
                Assert.IsTrue(sent is SendOutcome.Completed, "The actual Windows stream source must send successfully.");
                var received = await receiving;
                return received switch
                {
                    ReceiveOutcome.Completed complete => Assert.ContainsSingle(complete.Items).SavedPath!,
                    _ => throw new AssertFailedException("The actual Windows stream source must receive successfully.")
                };
            }
            finally
            {
                await deadline.CancelAsync();
                try { await receiving.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (OperationCanceledException) { }
            }
        }

        private async Task<ReceiveOutcome> AcceptNextAsync(CancellationToken cancellationToken)
        {
            await foreach (var request in Receiver.WatchIncomingTransfersAsync(cancellationToken))
                return await Receiver.AcceptAsync(request.RequestId, cancellationToken: cancellationToken);
            throw new AssertFailedException("The incoming stream ended without an offer.");
        }

        public async ValueTask DisposeAsync()
        {
            try { await Receiver.DisposeAsync(); }
            finally
            {
                try { await _sender.DisposeAsync(); }
                finally { await _directory.DisposeAsync(); }
            }
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
