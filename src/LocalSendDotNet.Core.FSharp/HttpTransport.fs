namespace LocalSendDotNet.FSharp

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling

[<RequireQualifiedAccess>]
type internal Negotiation =
    | Accepted of UploadAcceptance
    | NoUpload
    | PinRequired of invalid: bool
    | PinRateLimited
    | Busy
    | Declined

type internal OutgoingOffer = { Wire: UploadOffer; Sources: Map<FileId, SendItem> }

[<RequireQualifiedAccess>]
module internal OutgoingOffer =
    let prepare identity computeHash (items: SendItem list) (ct: CancellationToken) = taskResult {
        let mutable entries = []
        let mutable sources = Map.empty
        let mutable total = 0L
        for item in items do
            ct.ThrowIfCancellationRequested()
            let! metadata = task {
                try
                    let length = SendItems.length item
                    if length < 0L || length > Int64.MaxValue - total then
                        return Error { Code = FailureCode.SourceIo; Message = "Invalid or overflowing source size."; ItemId = None }
                    else
                        let! digest = task {
                            if computeHash then
                                use! stream = SendItems.openRead ct item
                                let! bytes = SHA256.HashDataAsync(stream, ct)
                                return Some(Convert.ToHexString bytes)
                            else return None
                        }
                        let modified, accessed =
                            match item with
                            | SendItem.File source -> Some(DateTimeOffset(File.GetLastWriteTimeUtc source.Path)), Some(DateTimeOffset(File.GetLastAccessTimeUtc source.Path))
                            | SendItem.Text _ | SendItem.Stream _ -> None, None
                        let preview = match items, item with
                                      | [_], SendItem.Text(text, _) -> Some text
                                      | _ -> None
                        return Ok { Id = FileId.fresh(); FileName = SendItems.fileName item; Size = length
                                    ContentType = SendItems.contentType item; Sha256 = digest; Preview = preview
                                    Modified = modified; Accessed = accessed }
                with
                | :? IOException as error -> return Error { Code = FailureCode.SourceIo; Message = error.Message; ItemId = None }
                | :? UnauthorizedAccessException as error -> return Error { Code = FailureCode.SourceIo; Message = error.Message; ItemId = None }
            }
            total <- total + metadata.Size
            entries <- metadata :: entries
            sources <- Map.add metadata.Id item sources
        do! if List.isEmpty entries then
                Error { Code = FailureCode.PrepareFailed; Message = "At least one item is required."; ItemId = None }
            else Ok ()
        return { Wire = { Info = Wire.ofIdentity identity; Files = List.rev entries }; Sources = sources }
    }

type internal HttpTransport(identity: DeviceIdentity, options: NodeOptions) =
    let createClient (endpoint: DeviceEndpoint) expected timeout capture =
        let handler = new HttpClientHandler()
        handler.AllowAutoRedirect <- false
        if endpoint.Protocol = Protocol.Https then
            handler.ClientCertificates.Add identity.Certificate |> ignore
            handler.ServerCertificateCustomValidationCallback <- fun _ certificate _ _ ->
                match certificate with
                | null -> raise (PeerIdentityException "Peer provided no TLS certificate.")
                | certificate ->
                    if not (IdentityStore.validatePeer expected certificate) then
                        raise (PeerIdentityException "Peer TLS identity failed validation.")
                    capture (IdentityStore.fingerprint certificate)
                    true
        let client = new HttpClient(handler, true)
        client.BaseAddress <- endpoint.BaseUri
        client.Timeout <- timeout
        client

    let decode decoder bytes =
        decoder bytes |> Result.defaultWith (fun error -> raise (InvalidDataException error))

    let jsonContent bytes =
        let content = new ByteArrayContent(bytes)
        content.Headers.ContentType <- MediaTypeHeaderValue "application/json"
        content

    let query name (value: string) = name + "=" + Uri.EscapeDataString value

    member _.Probe(endpoint: DeviceEndpoint, ct: CancellationToken, timeout: TimeSpan) = task {
        let mutable certificate = None
        use client = createClient endpoint None timeout (fun fingerprint -> certificate <- Some fingerprint)
        use! response = client.GetAsync(Wire.basePath + "/info", ct)
        response.EnsureSuccessStatusCode() |> ignore
        let! bytes = response.Content.ReadAsByteArrayAsync ct
        let peer = decode Wire.decodePeer bytes
        let fingerprint =
            match certificate, peer.Fingerprint with
            | Some actual, advertised ->
                if advertised |> Option.exists (fun advertised -> not (String.Equals(advertised, Fingerprint.value actual, StringComparison.OrdinalIgnoreCase))) then
                    raise (PeerIdentityException "Advertised fingerprint differs from TLS identity.")
                actual
            | None, Some advertised -> decode Fingerprint.create advertised
            | None, None -> raise (PeerIdentityException "Peer did not provide a fingerprint.")
        let peer = { peer with Port = endpoint.Port; Protocol = endpoint.Protocol }
        let device = decode (fun () -> Wire.toDevice endpoint.Address fingerprint peer) ()
        return { Device = device; IdentityVerified = certificate.IsSome }
    }

    member _.Register(endpoint: DeviceEndpoint, fingerprint: Fingerprint, local: LocalIdentity, ct: CancellationToken) = task {
        use client = createClient endpoint (Some fingerprint) options.DiscoveryTimeout ignore
        use body = Wire.ofIdentity local |> Wire.encodePeer |> jsonContent
        use! response = client.PostAsync(Wire.basePath + "/register", body, ct)
        response.EnsureSuccessStatusCode() |> ignore
        let! bytes = response.Content.ReadAsByteArrayAsync ct
        let peer = decode Wire.decodePeer bytes
        if peer.Fingerprint |> Option.exists (fun advertised -> not (String.Equals(advertised, Fingerprint.value fingerprint, StringComparison.OrdinalIgnoreCase))) then
            raise (PeerIdentityException "Register fingerprint differs from trusted identity.")
        return decode (fun () -> Wire.toDevice endpoint.Address fingerprint { peer with Port = endpoint.Port; Protocol = endpoint.Protocol }) ()
    }

    member _.Prepare(endpoint: DeviceEndpoint, fingerprint, offer, pin, ct: CancellationToken) = task {
        use client = createClient endpoint (Some fingerprint) options.RequestTimeout ignore
        use body = Wire.encodeOffer offer |> jsonContent
        let path = Wire.basePath + "/prepare-upload" + (pin |> Option.map (fun value -> "?" + query "pin" value) |> Option.defaultValue "")
        use! response = client.PostAsync(path, body, ct)
        match response.StatusCode with
        | HttpStatusCode.Unauthorized -> return Negotiation.PinRequired pin.IsSome
        | HttpStatusCode.Forbidden -> return Negotiation.Declined
        | HttpStatusCode.NoContent -> return Negotiation.NoUpload
        | HttpStatusCode.TooManyRequests ->
            let! bytes = response.Content.ReadAsByteArrayAsync ct
            let message = Wire.decodeError bytes |> Result.defaultValue ""
            return if message.Contains("maximum", StringComparison.OrdinalIgnoreCase) then Negotiation.Busy else Negotiation.PinRateLimited
        | _ ->
            response.EnsureSuccessStatusCode() |> ignore
            let! bytes = response.Content.ReadAsByteArrayAsync ct
            return Negotiation.Accepted(decode Wire.decodeAcceptance bytes)
    }

    member _.Upload(endpoint: DeviceEndpoint, fingerprint, acceptance: UploadAcceptance, item: IncomingItem,
                    source: SendItem, report: int64 -> unit, ct: CancellationToken) = task {
        if SendItems.length source <> item.Size then raise (IOException "Source size changed before upload.")
        use client = createClient endpoint (Some fingerprint) options.UploadTimeout ignore
        use! stream = SendItems.openRead ct source
        let mutable consumed = 0L
        use progress = new ProgressReadStream(stream, fun count -> consumed <- count; report count)
        use body = new StreamContent(progress)
        body.Headers.ContentLength <- Nullable item.Size
        body.Headers.ContentType <- MediaTypeHeaderValue item.ContentType
        let path = Wire.basePath + "/upload?" + String.concat "&"
                       [ query "sessionId" (SessionId.value acceptance.SessionId)
                         query "fileId" (FileId.value item.Id)
                         query "token" (UploadToken.value acceptance.Tokens[item.Id]) ]
        use! response = client.PostAsync(path, body, ct)
        response.EnsureSuccessStatusCode() |> ignore
        if consumed <> item.Size then raise (IOException "Source size changed during upload.")
        return { Id = item.Id; FileName = item.FileName; BytesTransferred = item.Size; SavedPath = None }
    }

    member _.Cancel(endpoint: DeviceEndpoint, fingerprint, session, ct: CancellationToken) = task {
        use client = createClient endpoint (Some fingerprint) options.CancelRequestTimeout ignore
        use! response = client.PostAsync(Wire.basePath + "/cancel?" + query "sessionId" (SessionId.value session), null, ct)
        if response.StatusCode <> HttpStatusCode.NotFound then response.EnsureSuccessStatusCode() |> ignore
    }
