using System.Net;
using LocalSendDotNet.Protocol.V2;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class IncomingCancellationTests
{
    [TestMethod]
    public async Task CancellationIntentTurnsFollowingBodyFailureIntoCancellationWithoutClosingBodyEarly()
    {
        var session = CreateSession();
        using (session.Cancellation)
        {
            session.FileCompleted("a", "a.bin", 3, "owned/a.bin");
            session.RequestCancellation();
            Assert.IsFalse(session.Cancellation.IsCancellationRequested);
            Assert.IsFalse(session.Completion.Task.IsCompleted);
            session.Fail(TransferFailureCodes.ReceiveFailed, "Unexpected EOF", "b");
            var result = await session.Completion.Task;
            var cancelledResult = result switch
            {
                ReceiveOutcome.Cancelled cancelled => cancelled,
                _ => throw new AssertFailedException("A body closed after cancellation intent must not become a receive failure.")
            };
            Assert.AreEqual(session.TransferId, cancelledResult.TransferId);
            Assert.AreEqual("a.bin", Assert.ContainsSingle(cancelledResult.Items).FileName);
            Assert.IsTrue(session.Cancellation.IsCancellationRequested);
        }
    }

    [TestMethod]
    public async Task BodyFailureWithoutCancellationRetainsItsOriginalErrorAndItem()
    {
        var session = CreateSession();
        using (session.Cancellation)
        {
            session.Fail(TransferFailureCodes.ChecksumMismatch, "Wrong digest", "b");
            var failure = (await session.Completion.Task).RequireFailed().Failure;
            Assert.AreEqual(TransferFailureCodes.ChecksumMismatch, failure.Code);
            Assert.AreEqual("Wrong digest", failure.Message);
            Assert.AreEqual("b", failure.ItemId);
            Assert.IsTrue(session.Cancellation.IsCancellationRequested);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LateCancellationNeverRewritesAlreadyCommittedSuccessOrFailure(bool success)
    {
        var session = CreateSession();
        using (session.Cancellation)
        {
            if (success)
            {
                session.FileCompleted("a", "a.bin", 3, "owned/a.bin");
                session.FileCompleted("b", "b.bin", 3, "owned/b.bin");
            }
            else
            {
                session.Fail(TransferFailureCodes.ReceiveFailed, "Original failure", "a");
            }
            var committed = await session.Completion.Task;
            session.RequestCancellation();
            session.Cancel();
            session.Fail(TransferFailureCodes.TransferTimeout, "Late timeout", "b");
            var final = await session.Completion.Task;
            if (success)
            {
                CollectionAssert.AreEqual(committed.RequireCompleted().Items.ToArray(), final.RequireCompleted().Items.ToArray());
                Assert.HasCount(2, final.RequireCompleted().Items);
            }
            else
            {
                Assert.AreEqual(committed.RequireFailed().Failure, final.RequireFailed().Failure);
                Assert.AreEqual("Original failure", final.RequireFailed().Failure.Message);
                Assert.AreEqual("a", final.RequireFailed().Failure.ItemId);
            }
        }
    }

    private static IncomingSession CreateSession()
    {
        var requestId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString("N");
        var sender = new LocalSendDevice("Sender", "2.2", null, LocalSendDeviceType.Desktop, "test-fingerprint", false,
            [new DeviceEndpoint(IPAddress.Loopback, 12345, LocalSendProtocol.Https)], DateTimeOffset.UtcNow);
        var session = new IncomingSession
        {
            RequestId = requestId,
            TransferId = transferId,
            SessionId = sessionId,
            RemoteAddress = IPAddress.Loopback,
            Request = new PrepareUploadRequestDto
            {
                Info = new DeviceInfoDto { Alias = "Sender", Version = "2.2", Fingerprint = sender.Fingerprint },
                Files = new Dictionary<string, FileDto>
                {
                    ["a"] = new() { Id = "a", FileName = "a.bin", Size = 3, FileType = "application/octet-stream" },
                    ["b"] = new() { Id = "b", FileName = "b.bin", Size = 3, FileType = "application/octet-stream" }
                }
            },
            PublicRequest = new(requestId, transferId, sessionId, sender, [], DateTimeOffset.UtcNow),
            Decision = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Completion = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        session.InitializeAccepted(["a", "b"]);
        return session;
    }
}
