namespace LocalSendDotNet.FSharp

open System
open System.Globalization
open System.IO
open System.Net
open System.Text.Json
open FsToolkit.ErrorHandling

type internal PeerInfo =
    { Alias: string; Version: string; Model: string option; DeviceType: DeviceType
      Fingerprint: string option; Port: int; Protocol: Protocol; Download: bool; Announce: bool }

type internal UploadOffer = { Info: PeerInfo; Files: IncomingItem list }
type internal UploadAcceptance = { SessionId: SessionId; Tokens: Map<FileId, UploadToken> }

/// Explicit codecs keep F# representation details off the v2 wire and need no reflection serializer.
[<RequireQualifiedAccess>]
module internal Wire =
    [<Literal>]
    let version = "2.2"
    [<Literal>]
    let basePath = "/api/localsend/v2"

    let private property name (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then raise (JsonException "Expected a JSON object.")
        element.EnumerateObject()
        |> Seq.tryFind (fun p -> String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        |> Option.map _.Value

    let private optionalString name element =
        match property name element with
        | None -> None
        | Some value when value.ValueKind = JsonValueKind.Null -> None
        | Some value when value.ValueKind = JsonValueKind.String -> Option.ofObj(value.GetString())
        | Some _ -> raise (JsonException $"'{name}' must be a string.")

    let private requiredString name element =
        match optionalString name element with
        | Some value when not (String.IsNullOrWhiteSpace value) -> value
        | Some _ | None -> raise (JsonException $"'{name}' is required.")

    let private integer name fallback element =
        match property name element with
        | None -> fallback
        | Some value ->
            match value.TryGetInt64() with
            | true, number -> number
            | false, _ -> raise (JsonException $"'{name}' must be an integer.")

    let private boolean name fallback element =
        match property name element with
        | None -> fallback
        | Some value when value.ValueKind = JsonValueKind.True -> true
        | Some value when value.ValueKind = JsonValueKind.False -> false
        | Some _ -> raise (JsonException $"'{name}' must be a boolean.")

    let private require = function Ok value -> value | Error message -> raise (JsonException message)

    let private parse decoder (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            Ok(decoder document.RootElement)
        with
        | :? JsonException as error -> Error error.Message
        | :? InvalidOperationException as error -> Error error.Message
        | :? OverflowException as error -> Error error.Message

    let private deviceType = function
        | Some "mobile" -> DeviceType.Mobile | Some "web" -> DeviceType.Web
        | Some "headless" -> DeviceType.Headless | Some "server" -> DeviceType.Server
        | Some "desktop" | Some _ | None -> DeviceType.Desktop

    let private readPeer element : PeerInfo =
        let port = integer "port" 53317L element
        if port < 1L || port > 65535L then raise (JsonException "Invalid peer port.")
        { Alias = requiredString "alias" element; Version = requiredString "version" element
          Model = optionalString "deviceModel" element
          DeviceType = optionalString "deviceType" element |> Option.map _.ToLowerInvariant() |> deviceType
          Fingerprint = optionalString "fingerprint" element
          Port = int port
          Protocol =
              match optionalString "protocol" element |> Option.map _.ToLowerInvariant() with
              | Some "http" -> Protocol.Http
              | Some "https" | None -> Protocol.Https
              | Some _ -> raise (JsonException "Unsupported peer protocol.")
          Download = boolean "download" false element; Announce = boolean "announce" false element }

    let private timestamp name element =
        optionalString name element
        |> Option.map (fun value ->
            match DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None) with
            | true, date -> date
            | false, _ -> raise (JsonException $"Invalid '{name}' timestamp."))

    let private readFile expectedId element : IncomingItem =
        let id = requiredString "id" element |> FileId.create |> require
        match expectedId with
        | Some key when FileId.value id <> key -> raise (JsonException "File key and ID differ.")
        | Some _ | None -> ()
        let size = integer "size" -1L element
        if size < 0L then raise (JsonException "File size cannot be negative.")
        let metadata = property "metadata" element |> Option.filter (fun e -> e.ValueKind <> JsonValueKind.Null)
        { Id = id; FileName = requiredString "fileName" element; Size = size
          ContentType = requiredString "fileType" element
          Sha256 = optionalString "sha256" element; Preview = optionalString "preview" element
          Modified = metadata |> Option.bind (timestamp "modified")
          Accessed = metadata |> Option.bind (timestamp "accessed") }

    let decodePeer bytes = parse readPeer bytes

    let decodeOffer bytes =
        let decode root =
            let info = property "info" root |> Option.defaultWith (fun () -> raise (JsonException "Missing info.")) |> readPeer
            let files = property "files" root |> Option.defaultWith (fun () -> raise (JsonException "Missing files."))
            if files.ValueKind <> JsonValueKind.Object then raise (JsonException "Files must be an object.")
            let items = files.EnumerateObject() |> Seq.map (fun p -> readFile (Some p.Name) p.Value) |> Seq.toList
            if items.Length <> (items |> List.map _.Id |> Set.ofList |> Set.count) then raise (JsonException "Duplicate file ID.")
            { Info = info; Files = items }
        parse decode bytes

    let decodeWebOffer bytes =
        let decode root =
            let files = property "files" root |> Option.defaultWith (fun () -> raise (JsonException "Missing files."))
            if files.ValueKind <> JsonValueKind.Array then raise (JsonException "Files must be an array.")
            let items = files.EnumerateArray() |> Seq.map (readFile None) |> Seq.toList
            if items.Length <> (items |> List.map _.Id |> Set.ofList |> Set.count) then raise (JsonException "Duplicate file ID.")
            items
        parse decode bytes

    let decodeAcceptance bytes =
        let decode root =
            let session = requiredString "sessionId" root |> SessionId.create |> require
            let files = property "files" root |> Option.defaultWith (fun () -> raise (JsonException "Missing files."))
            let pairs =
                files.EnumerateObject()
                |> Seq.map (fun p ->
                    let id = FileId.create p.Name |> require
                    let token = p.Value.GetString() |> Option.ofObj |> Option.defaultValue "" |> UploadToken.create |> require
                    id, token)
                |> Seq.toList
            if pairs.Length <> (pairs |> List.map fst |> Set.ofList |> Set.count) then raise (JsonException "Duplicate upload token key.")
            { SessionId = session; Tokens = Map.ofList pairs }
        parse decode bytes

    let decodeError bytes = parse (requiredString "message") bytes

    let private encode write =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        write writer
        writer.Flush()
        stream.ToArray()

    let private string (writer: Utf8JsonWriter) (name: string) (value: string) = writer.WriteString(name, value)
    let private optional writer name value = value |> Option.iter (string writer name)

    let ofIdentity (identity: LocalIdentity) : PeerInfo =
        { Alias = identity.Alias; Version = identity.Version; Model = identity.Model; DeviceType = identity.DeviceType
          Fingerprint = Some(Fingerprint.value identity.Fingerprint); Port = identity.Port
          Protocol = identity.Protocol; Download = false; Announce = false }

    let toDevice (address: IPAddress) (fingerprint: Fingerprint) (peer: PeerInfo) = result {
        let! endpoint = DeviceEndpoint.create address peer.Port peer.Protocol
        return { Alias = peer.Alias; Version = peer.Version; Model = peer.Model; DeviceType = peer.DeviceType
                 Fingerprint = fingerprint; SupportsDownload = peer.Download; Endpoints = [endpoint]; LastSeen = DateTimeOffset.UtcNow }
    }

    let private writePeer (writer: Utf8JsonWriter) (peer: PeerInfo) =
        writer.WriteStartObject()
        string writer "alias" peer.Alias
        string writer "version" peer.Version
        optional writer "deviceModel" peer.Model
        string writer "deviceType" (match peer.DeviceType with
                                   | DeviceType.Mobile -> "mobile" | DeviceType.Desktop -> "desktop"
                                   | DeviceType.Web -> "web" | DeviceType.Headless -> "headless" | DeviceType.Server -> "server")
        optional writer "fingerprint" peer.Fingerprint
        writer.WriteNumber("port", peer.Port)
        string writer "protocol" (match peer.Protocol with Protocol.Http -> "http" | Protocol.Https -> "https")
        writer.WriteBoolean("download", peer.Download)
        if peer.Announce then writer.WriteBoolean("announce", true)
        writer.WriteEndObject()

    let private writeFile (writer: Utf8JsonWriter) (file: IncomingItem) =
        writer.WriteStartObject()
        string writer "id" (FileId.value file.Id)
        string writer "fileName" file.FileName
        writer.WriteNumber("size", file.Size)
        string writer "fileType" file.ContentType
        optional writer "sha256" file.Sha256
        optional writer "preview" file.Preview
        if file.Modified.IsSome || file.Accessed.IsSome then
            writer.WriteStartObject("metadata")
            optional writer "modified" (file.Modified |> Option.map (fun t -> t.ToString("O")))
            optional writer "accessed" (file.Accessed |> Option.map (fun t -> t.ToString("O")))
            writer.WriteEndObject()
        writer.WriteEndObject()

    let encodePeer peer = encode (fun writer -> writePeer writer peer)
    let encodeOffer (offer: UploadOffer) = encode (fun writer ->
        writer.WriteStartObject()
        writer.WritePropertyName("info")
        writePeer writer offer.Info
        writer.WriteStartObject("files")
        for file in offer.Files do
            writer.WritePropertyName(FileId.value file.Id)
            writeFile writer file
        writer.WriteEndObject()
        writer.WriteEndObject())

    let encodeAcceptance (acceptance: UploadAcceptance) = encode (fun writer ->
        writer.WriteStartObject()
        string writer "sessionId" (SessionId.value acceptance.SessionId)
        writer.WriteStartObject("files")
        for KeyValue(id, token) in acceptance.Tokens do string writer (FileId.value id) (UploadToken.value token)
        writer.WriteEndObject()
        writer.WriteEndObject())

    let encodeError message = encode (fun writer ->
        writer.WriteStartObject(); string writer "message" message; writer.WriteEndObject())

    let encodeDownload session (files: WebShareFile list) = encode (fun writer ->
        writer.WriteStartObject()
        string writer "sessionId" (SessionId.value session)
        writer.WriteStartArray("files")
        for file in files do
            writer.WriteStartObject()
            string writer "id" (FileId.value file.Id)
            string writer "fileName" file.FileName
            writer.WriteNumber("size", file.Size)
            writer.WriteEndObject()
        writer.WriteEndArray()
        writer.WriteEndObject())
