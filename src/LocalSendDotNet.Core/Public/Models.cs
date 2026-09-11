using System.Net;

namespace LocalSendDotNet;

/// <summary>Describes the form factor advertised by a LocalSend peer.</summary>
public enum LocalSendDeviceType
{
    /// <summary>A phone or tablet.</summary>
    Mobile,
    /// <summary>A desktop or laptop.</summary>
    Desktop,
    /// <summary>A web client.</summary>
    Web,
    /// <summary>A peer without an interactive display.</summary>
    Headless,
    /// <summary>A server peer.</summary>
    Server
}

/// <summary>Identifies the HTTP transport used by a peer endpoint.</summary>
public enum LocalSendProtocol
{
    /// <summary>Unencrypted HTTP.</summary>
    Http,
    /// <summary>HTTPS with LocalSend certificate fingerprint validation.</summary>
    Https
}

/// <summary>Identifies whether a transfer sends or receives data.</summary>
public enum TransferDirection
{
    /// <summary>Data is sent to a peer.</summary>
    Send,
    /// <summary>Data is received from a peer.</summary>
    Receive
}

/// <summary>Describes the current or final state of a transfer.</summary>
public enum TransferState
{
    /// <summary>Metadata or checksums are being prepared.</summary>
    Preparing,
    /// <summary>The sender is waiting for the receiver's decision.</summary>
    WaitingForAcceptance,
    /// <summary>Content bytes are being transferred.</summary>
    Transferring,
    /// <summary>The transfer completed successfully.</summary>
    Completed,
    /// <summary>The transfer was cancelled locally or remotely.</summary>
    Cancelled,
    /// <summary>The transfer failed.</summary>
    Failed
}

/// <summary>Describes a change to the in-memory device list.</summary>
public enum DeviceChangeKind
{
    /// <summary>A device was first observed.</summary>
    Added,
    /// <summary>Properties or endpoints of a known device changed.</summary>
    Updated,
    /// <summary>A device expired or was explicitly removed.</summary>
    Removed
}

/// <summary>Describes the lifecycle state of a <see cref="LocalSendNode"/>.</summary>
public enum LocalSendNodeState
{
    /// <summary>The node has not been started.</summary>
    Created,
    /// <summary>The node is loading identity and binding transports.</summary>
    Starting,
    /// <summary>The node is ready to discover and transfer.</summary>
    Running,
    /// <summary>The node is stopping active work.</summary>
    Stopping,
    /// <summary>The node stopped and cannot be restarted.</summary>
    Stopped,
    /// <summary>Startup failed; startup may be retried after correcting the cause.</summary>
    Faulted,
    /// <summary>The node has been disposed.</summary>
    Disposed
}

/// <summary>Identifies one network endpoint for a LocalSend peer.</summary>
public sealed record DeviceEndpoint
{
    /// <summary>Creates a normalized endpoint.</summary>
    /// <param name="address">The peer IP address.</param>
    /// <param name="port">The TCP LocalSend port.</param>
    /// <param name="protocol">The HTTP transport.</param>
    public DeviceEndpoint(IPAddress address, int port, LocalSendProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);
        Address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        Port = port;
        Protocol = protocol;
    }

    /// <summary>Gets the normalized IP address.</summary>
    public IPAddress Address { get; }
    /// <summary>Gets the TCP port.</summary>
    public int Port { get; }
    /// <summary>Gets the HTTP transport.</summary>
    public LocalSendProtocol Protocol { get; }
}

/// <summary>Represents a discovered or manually trusted LocalSend peer.</summary>
/// <param name="Alias">The display name advertised by the peer.</param>
/// <param name="ProtocolVersion">The peer protocol version string.</param>
/// <param name="DeviceModel">The optional peer model.</param>
/// <param name="DeviceType">The peer form factor.</param>
/// <param name="Fingerprint">The uppercase SHA-256 certificate fingerprint.</param>
/// <param name="SupportsDownload">Whether the peer advertises pull-download support.</param>
/// <param name="Endpoints">Known v2 endpoints.</param>
/// <param name="LastSeen">When the peer was last observed.</param>
public sealed record LocalSendDevice(
    string Alias,
    string ProtocolVersion,
    string? DeviceModel,
    LocalSendDeviceType DeviceType,
    string Fingerprint,
    bool SupportsDownload,
    IReadOnlyList<DeviceEndpoint> Endpoints,
    DateTimeOffset LastSeen)
{
    /// <summary>Gets the preferred endpoint, choosing HTTPS when available.</summary>
    public DeviceEndpoint? PreferredEndpoint => Endpoints.OrderByDescending(static endpoint => endpoint.Protocol == LocalSendProtocol.Https).FirstOrDefault();
}

/// <summary>Describes the persistent identity and listening transport of the local node.</summary>
/// <param name="Alias">The local display alias.</param>
/// <param name="ProtocolVersion">The implemented protocol version.</param>
/// <param name="DeviceModel">The configured device model.</param>
/// <param name="DeviceType">The configured device type.</param>
/// <param name="Fingerprint">The local certificate fingerprint.</param>
/// <param name="Port">The listening TCP port.</param>
/// <param name="Protocol">The configured HTTP transport.</param>
public sealed record LocalSendIdentity(
    string Alias,
    string ProtocolVersion,
    string? DeviceModel,
    LocalSendDeviceType DeviceType,
    string Fingerprint,
    int Port,
    LocalSendProtocol Protocol);

/// <summary>Reports an in-memory device-list change.</summary>
/// <param name="Kind">The kind of change.</param>
/// <param name="Device">The affected device snapshot.</param>
public sealed record DeviceChange(DeviceChangeKind Kind, LocalSendDevice Device);

/// <summary>Contains the result of probing a manually entered endpoint.</summary>
/// <param name="Device">The peer information returned by the endpoint.</param>
/// <param name="IdentityVerified">Whether TLS cryptographically bound the returned fingerprint.</param>
public sealed record DeviceProbeResult(LocalSendDevice Device, bool IdentityVerified);

/// <summary>Reports a node lifecycle transition.</summary>
/// <param name="Previous">The previous state.</param>
/// <param name="Current">The new state.</param>
/// <param name="Error">The startup error when entering <see cref="LocalSendNodeState.Faulted"/>.</param>
public sealed record LocalSendNodeStateChange(LocalSendNodeState Previous, LocalSendNodeState Current, Exception? Error = null);

/// <summary>Base type for content offered to a peer.</summary>
/// <param name="FileName">The protocol filename, which may contain safe relative path components.</param>
/// <param name="ContentType">The MIME content type.</param>
public abstract record SendItem(string FileName, string ContentType)
{
    internal abstract ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken);
    internal abstract long Length { get; }
}

/// <summary>Represents a file-system file to send.</summary>
public sealed record SendFileItem : SendItem
{
    /// <summary>Creates a file item.</summary>
    /// <param name="path">The local source path.</param>
    /// <param name="fileName">An optional protocol filename.</param>
    /// <param name="contentType">An optional MIME type; common types are inferred when omitted.</param>
    public SendFileItem(string path, string? fileName = null, string? contentType = null)
        : base(fileName ?? System.IO.Path.GetFileName(path), contentType ?? LocalSendContentTypes.GetForFileName(fileName ?? System.IO.Path.GetFileName(path)))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>Gets the local source path.</summary>
    public string Path { get; }
    internal override long Length => new FileInfo(Path).Length;
    internal override ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<Stream>(new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 512 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan));
}

/// <summary>Represents UTF-8 text content to send.</summary>
/// <param name="Text">The text content.</param>
/// <param name="Name">The protocol filename.</param>
public sealed record SendTextItem(string Text, string Name = "message.txt")
    : SendItem(Name, "text/plain")
{
    private byte[]? _bytes;
    private byte[] Bytes => _bytes ??= System.Text.Encoding.UTF8.GetBytes(Text);
    internal override long Length => Bytes.LongLength;
    internal override ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<Stream>(new MemoryStream(Bytes, writable: false));
}

/// <summary>Represents repeatable stream-backed content, such as a sandboxed UI file-picker result.</summary>
public sealed record SendStreamItem : SendItem
{
    private readonly Func<CancellationToken, ValueTask<Stream>> _openStream;

    /// <summary>Creates a stream-backed send item.</summary>
    /// <param name="fileName">The protocol filename.</param>
    /// <param name="length">The exact stream length.</param>
    /// <param name="openStream">A factory that returns a new readable stream for each invocation.</param>
    /// <param name="contentType">An optional MIME type.</param>
    public SendStreamItem(string fileName, long length, Func<CancellationToken, ValueTask<Stream>> openStream, string? contentType = null)
        : base(fileName, contentType ?? LocalSendContentTypes.GetForFileName(fileName))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
        LengthValue = length;
    }

    /// <summary>Gets the declared content length.</summary>
    public long LengthValue { get; }
    internal override long Length => LengthValue;
    internal override async ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        var stream = await _openStream(cancellationToken).ConfigureAwait(false);
        return stream ?? throw new LocalSendException("The stream factory returned null.");
    }
}

/// <summary>Describes one item offered by a remote sender.</summary>
/// <param name="Id">The protocol item identifier.</param>
/// <param name="FileName">The requested filename.</param>
/// <param name="Size">The declared byte length.</param>
/// <param name="ContentType">The declared MIME type.</param>
/// <param name="Sha256">An optional hexadecimal or Base64 SHA-256 digest.</param>
/// <param name="Preview">An optional peer-provided preview.</param>
/// <param name="Modified">The optional modification timestamp.</param>
/// <param name="Accessed">The optional access timestamp.</param>
public sealed record IncomingItem(
    string Id,
    string FileName,
    long Size,
    string ContentType,
    string? Sha256,
    string? Preview,
    DateTimeOffset? Modified,
    DateTimeOffset? Accessed);

/// <summary>Represents an incoming offer awaiting an application decision.</summary>
/// <param name="RequestId">The identifier used by accept and decline operations.</param>
/// <param name="TransferId">The stable transfer identifier used by cancellation and progress.</param>
/// <param name="SessionId">The internal peer session identifier exposed for diagnostics.</param>
/// <param name="Sender">The sending peer.</param>
/// <param name="Items">The offered items.</param>
/// <param name="ReceivedAt">When the offer was received.</param>
public sealed record IncomingTransferRequest(
    Guid RequestId,
    Guid TransferId,
    string SessionId,
    LocalSendDevice Sender,
    IReadOnlyList<IncomingItem> Items,
    DateTimeOffset ReceivedAt);

/// <summary>Configures acceptance of an incoming transfer.</summary>
public sealed class AcceptTransferOptions
{
    /// <summary>Gets the destination root, or <see langword="null"/> to use the configured default.</summary>
    public string? DestinationDirectory { get; init; }
    /// <summary>Gets the accepted item IDs, or <see langword="null"/> to accept every item.</summary>
    public IReadOnlyCollection<string>? AcceptedItemIds { get; init; }
    /// <summary>Gets optional target filenames keyed by item ID.</summary>
    public IReadOnlyDictionary<string, string>? TargetFileNames { get; init; }
    /// <summary>Gets whether advertised SHA-256 digests are verified while receiving files.</summary>
    public bool VerifySha256 { get; init; } = true;
}

/// <summary>Configures an outgoing transfer.</summary>
public sealed class SendOptions
{
    /// <summary>Gets the optional receiver PIN.</summary>
    public string? Pin { get; init; }
    /// <summary>Gets whether each item is pre-read to advertise a SHA-256 digest.</summary>
    public bool ComputeSha256 { get; init; }
}

/// <summary>Reports aggregate transfer progress.</summary>
/// <param name="TransferId">The transfer identifier.</param>
/// <param name="ItemId">The active item ID, when applicable.</param>
/// <param name="Direction">The transfer direction.</param>
/// <param name="State">The current state.</param>
/// <param name="BytesTransferred">Aggregate bytes transferred.</param>
/// <param name="TotalBytes">Aggregate declared bytes.</param>
public sealed record TransferProgress(
    Guid TransferId,
    string? ItemId,
    TransferDirection Direction,
    TransferState State,
    long BytesTransferred,
    long TotalBytes);

/// <summary>Describes the final result for one selected item.</summary>
/// <param name="ItemId">The protocol item ID.</param>
/// <param name="FileName">The protocol filename.</param>
/// <param name="BytesTransferred">The transferred byte count.</param>
/// <param name="SavedPath">The receiver path, when applicable.</param>
public sealed record TransferredItemResult(string ItemId, string FileName, long BytesTransferred, string? SavedPath);

/// <summary>Describes a transfer failure without exposing protocol DTOs.</summary>
/// <param name="Code">A stable code from <see cref="TransferFailureCodes"/> when known.</param>
/// <param name="Message">A diagnostic message.</param>
/// <param name="ItemId">The failing item ID, when applicable.</param>
public sealed record TransferFailure(string Code, string Message, string? ItemId = null);

/// <summary>Contains the final outcome of a send or receive operation.</summary>
/// <param name="TransferId">The transfer identifier.</param>
/// <param name="Direction">The transfer direction.</param>
/// <param name="State">The final state.</param>
/// <param name="Items">Successfully transferred items.</param>
/// <param name="Failure">Failure details when <paramref name="State"/> is failed.</param>
public sealed record TransferResult(
    Guid TransferId,
    TransferDirection Direction,
    TransferState State,
    IReadOnlyList<TransferredItemResult> Items,
    TransferFailure? Failure = null)
{
    /// <summary>Gets whether the transfer completed successfully.</summary>
    public bool IsSuccess => State == TransferState.Completed;
    /// <summary>Gets the sum of successfully transferred item bytes.</summary>
    public long BytesTransferred => Items.Sum(static item => item.BytesTransferred);
}

/// <summary>Stable failure-code constants for application error handling.</summary>
public static class TransferFailureCodes
{
    /// <summary>Preparation or receiver negotiation failed.</summary>
    public const string PrepareFailed = "prepare_failed";
    /// <summary>Uploading item content failed.</summary>
    public const string UploadFailed = "upload_failed";
    /// <summary>The peer TLS identity failed validation.</summary>
    public const string PeerIdentity = "peer_identity";
    /// <summary>The peer has no free transfer capacity.</summary>
    public const string PeerBusy = "peer_busy";
    /// <summary>The receiver declined the offer.</summary>
    public const string Declined = "declined";
    /// <summary>The local source could not be read or changed during transfer.</summary>
    public const string SourceIo = "source_io";
    /// <summary>The requested receive destination was invalid or unsafe.</summary>
    public const string InvalidDestination = "invalid_destination";
    /// <summary>Received content length differed from its declaration.</summary>
    public const string LengthMismatch = "length_mismatch";
    /// <summary>Received content failed digest verification.</summary>
    public const string ChecksumMismatch = "checksum_mismatch";
    /// <summary>Receiving or publishing content failed.</summary>
    public const string ReceiveFailed = "receive_failed";
    /// <summary>The accepted transfer did not finish before its timeout.</summary>
    public const string TransferTimeout = "transfer_timeout";
}
