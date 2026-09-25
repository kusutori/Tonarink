using System.Net;
using LocalSendDotNet.Protocol.V2;

namespace LocalSendDotNet;

internal sealed record IncomingDecision(bool Accepted, AcceptTransferOptions? Options);

internal sealed class IncomingSession
{
    private int _remaining;
    private readonly List<TransferredItemResult> _results = [];
    private readonly object _gate = new();
    private readonly object _tokenGate = new();
    private readonly Dictionary<string, long> _itemProgress = new(StringComparer.Ordinal);
    private long _totalBytes;

    public required Guid RequestId { get; init; }
    public required Guid TransferId { get; init; }
    public required string SessionId { get; init; }
    public required IPAddress RemoteAddress { get; init; }
    public required PrepareUploadRequestDto Request { get; init; }
    public required IncomingTransferRequest PublicRequest { get; init; }
    public required TaskCompletionSource<IncomingDecision> Decision { get; init; }
    public required TaskCompletionSource<ReceiveOutcome> Completion { get; init; }
    public Dictionary<string, string> Tokens { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Destinations { get; } = new(StringComparer.Ordinal);
    public bool VerifySha256 { get; set; } = true;
    public CancellationTokenSource Cancellation { get; } = new();
    public IProgress<TransferProgress>? Progress { get; set; }

    public void InitializeAccepted(IEnumerable<string> ids)
    {
        var accepted = ids.ToArray();
        _remaining = accepted.Length;
        _totalBytes = accepted.Sum(id => Request.Files[id].Size);
    }

    public bool TryConsumeToken(string itemId, string token)
    {
        lock (_tokenGate)
        {
            if (!Tokens.TryGetValue(itemId, out var expected) || !StringComparer.Ordinal.Equals(expected, token))
                return false;
            Tokens.Remove(itemId);
            return true;
        }
    }

    public Dictionary<string, string> TokenSnapshot()
    {
        lock (_tokenGate) return new(Tokens, StringComparer.Ordinal);
    }

    public void FileCompleted(string itemId, string fileName, long bytes, string path)
    {
        lock (_gate)
        {
            _results.Add(new(itemId, fileName, bytes, path));
            if (Interlocked.Decrement(ref _remaining) == 0)
                Completion.TrySetResult(new ReceiveOutcome.Completed(TransferId, _results.ToArray()));
        }
    }

    public void ReportProgress(string itemId, long bytes)
    {
        lock (_gate)
        {
            _itemProgress[itemId] = bytes;
            Progress?.Report(new(TransferId, itemId, TransferDirection.Receive, TransferState.Transferring, _itemProgress.Values.Sum(), _totalBytes));
        }
    }

    public void Fail(string code, string message, string? itemId = null)
    {
        Completion.TrySetResult(new ReceiveOutcome.Failed(
            TransferId,
            _results.ToArray(),
            new TransferFailure(code, message, itemId)));
        Cancellation.Cancel();
    }

    public void Cancel() => Completion.TrySetResult(new ReceiveOutcome.Cancelled(TransferId, _results.ToArray()));
}
