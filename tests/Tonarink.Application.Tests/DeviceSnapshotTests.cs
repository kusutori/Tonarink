using System.Collections;
using System.Net;
using LocalSendDotNet;
using Tonarink.Application;

namespace Tonarink.Application.Tests;

[TestClass]
public sealed class DeviceSnapshotTests
{
    [TestMethod]
    [Timeout(30_000)]
    public async Task SendDuringSnapshotEnumerationKeepsPreviousPeerUntilReplacementIsReady()
    {
        await using var pair = await RuntimePair.StartAsync(settings => settings with { ReceivePin = "2468" });
        var (peer, item, corePeer) = await RegisterPeerAsync(pair);
        using var snapshot = new PausedSnapshot(corePeer with { Alias = "Updated receiver" });
        var replacement = Task.Run(() => pair.Sender.ReplaceDevices(snapshot));
        try
        {
            await snapshot.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(device => device.Id == peer.Id && device.Alias == peer.Alias, pair.SenderState.Devices);
            var result = await pair.Sender.SendAsync(peer, [item]).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(result is RuntimeSendResult.PinRequired, $"Expected PIN-required result, got {result}.");
            Assert.IsEmpty(pair.ReceiverState.IncomingOffers);
        }
        finally
        {
            snapshot.Release();
            await replacement.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Contains(device => device.Id == peer.Id && device.Alias == "Updated receiver", pair.SenderState.Devices);
        var send = pair.Sender.SendAsync(peer, [item], pin: "2468");
        var offer = await pair.WaitForOfferAsync(send);
        var received = await pair.Receiver.AcceptAsync(offer, offer.Items.Select(static value => value.Id).ToHashSet());
        Assert.IsTrue(received is RuntimeReceiveResult.Completed, $"Unexpected receive result: {received}.");
        var sent = await send;
        Assert.IsTrue(sent is RuntimeSendResult.Completed, $"Unexpected send result: {sent}.");
        Assert.AreEqual("snapshot", await File.ReadAllTextAsync(Path.Combine(pair.ReceiverPlatform.DownloadDirectory, item.Name)));
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task FaultedSnapshotEnumerationDoesNotPublishPartialPeerState()
    {
        await using var pair = await RuntimePair.StartAsync(settings => settings with { ReceivePin = "2468" });
        var (peer, item, corePeer) = await RegisterPeerAsync(pair);
        using var snapshot = new PausedSnapshot(corePeer with { Alias = "Must not be published" }, fail: true);
        snapshot.Release();
        Assert.ThrowsExactly<InvalidDataException>(() => pair.Sender.ReplaceDevices(snapshot));
        Assert.Contains(device => device.Id == peer.Id && device.Alias == peer.Alias, pair.SenderState.Devices);
        var result = await pair.Sender.SendAsync(peer, [item]).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(result is RuntimeSendResult.PinRequired, $"Expected PIN-required result, got {result}.");
        Assert.IsEmpty(pair.ReceiverState.IncomingOffers);
        Assert.IsFalse(pair.SenderState.Transfers.Any(activity => activity.Status == TransferStatus.Completed));
    }

    private static async Task<(NearbyDevice Peer, ShareItem Item, LocalSendDevice CorePeer)> RegisterPeerAsync(RuntimePair pair)
    {
        var path = Path.Combine(pair.Root, "snapshot.bin");
        await File.WriteAllBytesAsync(path, "snapshot"u8.ToArray());
        var item = new ShareItem(Guid.NewGuid(), "snapshot.bin", 8, "application/octet-stream", path,
            OpenRead: _ => ValueTask.FromResult<Stream>(File.OpenRead(path)));
        var result = await pair.Sender.SendToAddressAsync($"127.0.0.1:{pair.ReceiverPort}", [item]);
        Assert.IsTrue(result is RuntimeSendResult.PinRequired, $"Expected PIN-required result, got {result}.");
        var peer = Assert.ContainsSingle(pair.SenderState.Devices.Where(device => device.Alias == "Receiver"));
        // Keep the real peer's certificate identity and endpoint. Only the
        // enumeration boundary is controlled; send still uses actual HTTPS.
        var corePeer = new LocalSendDevice(peer.Alias, "2.1", peer.Model, LocalSendDeviceType.Desktop,
            peer.Id, false, [new DeviceEndpoint(IPAddress.Loopback, pair.ReceiverPort, LocalSendProtocol.Https)], peer.LastSeen);
        return (peer, item, corePeer);
    }

    private sealed class PausedSnapshot(LocalSendDevice peer, bool fail = false) : IReadOnlyList<LocalSendDevice>, IDisposable
    {
        private readonly ManualResetEventSlim _released = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count => 1;
        public LocalSendDevice this[int index] => index == 0 ? peer : throw new ArgumentOutOfRangeException(nameof(index));
        public void Release() => _released.Set();
        public void Dispose() => _released.Dispose();

        public IEnumerator<LocalSendDevice> GetEnumerator()
        {
            Entered.TrySetResult();
            if (!_released.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test-owned snapshot enumeration was not released.");
            if (fail)
                throw new InvalidDataException("Test-owned snapshot enumeration failed.");
            yield return peer;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
