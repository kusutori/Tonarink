namespace LocalSendDotNet.FSharp

open LocalSendDotNet.Resources

open System
open System.IO
open System.Net
open System.Threading
open System.Threading.Tasks

/// Opaque identifiers prevent accidental interchange of protocol credentials.
[<Struct>]
type FileId = private FileId of string

[<RequireQualifiedAccess>]
module FileId =
    let value (FileId value) = value
    let create value =
        if String.IsNullOrWhiteSpace value then Error (CoreText.FileIdBlank)
        else Ok(FileId value)
    let fresh () = FileId(Guid.NewGuid().ToString("N"))

[<Struct>]
type SessionId = private SessionId of string

[<RequireQualifiedAccess>]
module SessionId =
    let value (SessionId value) = value
    let create value =
        if String.IsNullOrWhiteSpace value then Error (CoreText.SessionIdBlank)
        else Ok(SessionId value)
    let fresh () = SessionId(Guid.NewGuid().ToString("N"))

[<Struct>]
type UploadToken = private UploadToken of string

[<RequireQualifiedAccess>]
module UploadToken =
    let value (UploadToken value) = value
    let create value =
        if String.IsNullOrWhiteSpace value then Error (CoreText.UploadTokenBlank)
        else Ok(UploadToken value)
    let fresh () = UploadToken(Convert.ToHexString(Security.Cryptography.RandomNumberGenerator.GetBytes(32)))

[<Struct>]
type Fingerprint = private Fingerprint of string

[<RequireQualifiedAccess>]
module Fingerprint =
    let value (Fingerprint value) = value
    // HTTP discovery is unverified and may use non-certificate identifiers.
    let create value =
        if String.IsNullOrWhiteSpace value then Error (CoreText.FingerprintBlank)
        else Ok(Fingerprint(value.ToUpperInvariant()))
    let internal ofCertificate value = Fingerprint value

[<RequireQualifiedAccess>]
type DeviceType = Mobile | Desktop | Web | Headless | Server

[<RequireQualifiedAccess>]
type Protocol = Http | Https

type DeviceEndpoint = private { AddressValue: IPAddress; PortValue: int; ProtocolValue: Protocol }
with
    member this.Address = this.AddressValue
    member this.Port = this.PortValue
    member this.Protocol = this.ProtocolValue
    member this.BaseUri =
        let scheme = match this.Protocol with Protocol.Http -> "http" | Protocol.Https -> "https"
        UriBuilder(scheme, string this.Address, this.Port).Uri

[<RequireQualifiedAccess>]
module DeviceEndpoint =
    let create (address: IPAddress | null) port protocol =
        match address with
        | null -> Error (CoreText.EndpointAddressMissing)
        | _ when port < 1 || port > 65535 -> Error (CoreText.PortRange)
        | address ->
            Ok { AddressValue = if address.IsIPv4MappedToIPv6 then address.MapToIPv4() else address
                 PortValue = port; ProtocolValue = protocol }

type Device =
    { Alias: string
      Version: string
      Model: string option
      DeviceType: DeviceType
      Fingerprint: Fingerprint
      SupportsDownload: bool
      Endpoints: DeviceEndpoint list
      LastSeen: DateTimeOffset }
    member this.PreferredEndpoint =
        this.Endpoints |> List.sortBy (fun e -> e.Protocol <> Protocol.Https) |> List.tryHead

type LocalIdentity =
    { Alias: string
      Version: string
      Model: string option
      DeviceType: DeviceType
      Fingerprint: Fingerprint
      Port: int
      Protocol: Protocol }

type ProbeResult = { Device: Device; IdentityVerified: bool }

[<RequireQualifiedAccess>]
type DeviceChange = Added of Device | Updated of Device | Removed of Device

[<RequireQualifiedAccess>]
type NodeState = Created | Starting | Running | Stopping | Stopped | Faulted of exn | Disposed

type NodeStateChange = { Previous: NodeState; Current: NodeState }

/// A fresh readable stream must be returned on every call (hashing and upload are separate reads).
type StreamSource =
    { FileName: string
      Length: int64
      ContentType: string
      OpenRead: CancellationToken -> Task<Stream> }

type FileSource = { Path: string; FileName: string; ContentType: string }

[<RequireQualifiedAccess>]
type SendItem = File of FileSource | Text of text: string * fileName: string | Stream of StreamSource

type IncomingItem =
    { Id: FileId
      FileName: string
      Size: int64
      ContentType: string
      Sha256: string option
      Preview: string option
      Modified: DateTimeOffset option
      Accessed: DateTimeOffset option }

type IncomingRequest =
    { RequestId: Guid
      TransferId: Guid
      SessionId: SessionId
      Sender: Device
      Items: IncomingItem list
      ReceivedAt: DateTimeOffset }

[<RequireQualifiedAccess>]
type ItemSelection = All | Only of Set<FileId>

type ReceiveOptions =
    { DestinationDirectory: string option
      Selection: ItemSelection
      TargetFileNames: Map<FileId, string>
      VerifySha256: bool }
    static member Default =
        { DestinationDirectory = None; Selection = ItemSelection.All
          TargetFileNames = Map.empty; VerifySha256 = true }

[<RequireQualifiedAccess>]
type ReceiveDecision = Decline | Accept of ReceiveOptions

type SendOptions =
    { Pin: string option; ComputeSha256: bool }
    static member Default = { Pin = None; ComputeSha256 = false }

[<RequireQualifiedAccess>]
type TransferDirection = Send | Receive

[<RequireQualifiedAccess>]
type TransferPhase = Preparing | WaitingForAcceptance | Transferring | Completed | Cancelled | Failed

type TransferProgress =
    { TransferId: Guid
      ItemId: FileId option
      Direction: TransferDirection
      Phase: TransferPhase
      BytesTransferred: int64
      TotalBytes: int64 }

type TransferredItem = { Id: FileId; FileName: string; BytesTransferred: int64; SavedPath: string option }

[<RequireQualifiedAccess>]
type FailureCode =
    | PrepareFailed | UploadFailed | PeerIdentity | SourceIo | InvalidDestination
    | LengthMismatch | ChecksumMismatch | ReceiveFailed | TransferTimeout

type TransferFailure = { Code: FailureCode; Message: string; ItemId: FileId option }

[<RequireQualifiedAccess>]
type SendOutcome =
    | Completed of transferId: Guid * items: TransferredItem list
    | Cancelled of transferId: Guid * items: TransferredItem list
    | PinRequired of transferId: Guid * invalidPin: bool
    | RateLimited of transferId: Guid
    | PeerBusy of transferId: Guid
    | Declined of transferId: Guid
    | Failed of transferId: Guid * items: TransferredItem list * failure: TransferFailure

[<RequireQualifiedAccess>]
type ReceiveOutcome =
    | Completed of transferId: Guid * items: TransferredItem list
    | Cancelled of transferId: Guid * items: TransferredItem list
    | Failed of transferId: Guid * items: TransferredItem list * failure: TransferFailure

type WebShareFile = { Id: FileId; FileName: string; Size: int64; ContentType: string }
type WebShareRequest = { SessionId: SessionId; DeviceInfo: string; Address: IPAddress; Pending: bool }

[<RequireQualifiedAccess>]
type WebShareMode = Send | Receive

type WebShareOptions =
    { AutoAccept: bool; Pin: string option; UiCulture: string option }
    static member Default = { AutoAccept = false; Pin = None; UiCulture = None }

type WebShareState =
    { Active: bool
      Mode: WebShareMode
      Files: WebShareFile list
      Requests: WebShareRequest list
      Options: WebShareOptions }
    static member Inactive =
        { Active = false; Mode = WebShareMode.Send; Files = []; Requests = []; Options = WebShareOptions.Default }
