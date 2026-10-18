namespace LocalSendDotNet.FSharp

open System
open System.IO
open System.Net
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.AspNetCore.Server.Kestrel.Https
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

type internal Server(options: NodeOptions, identity: DeviceIdentity, local: LocalIdentity, sessions: ReceiveSessions,
                     web: WebShareService, onRegister: Device -> Task,
                     onCancel: SessionId -> IPAddress -> Task<bool>) =
    let pinGuard = PinGuard options.PinLockoutDuration
    let mutable application: WebApplication option = None

    let remote (context: HttpContext) =
        match context.Connection.RemoteIpAddress with
        | null -> invalidOp "HTTP request has no source address."
        | address -> if address.IsIPv4MappedToIPv6 then address.MapToIPv4() else address

    let json (context: HttpContext) status (bytes: byte array) = task {
        context.Response.StatusCode <- status
        context.Response.ContentType <- "application/json"
        context.Response.ContentLength <- Nullable(int64 bytes.Length)
        do! context.Response.Body.WriteAsync(ReadOnlyMemory bytes, context.RequestAborted)
    }

    let error context status message = json context status (Wire.encodeError message)
    let query name (context: HttpContext) =
        let value = context.Request.Query[name].ToString()
        if String.IsNullOrEmpty value then None else Some value

    let body limit (context: HttpContext) = task {
        if context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > limit then
            return Error "Request body is too large."
        else
            use output = new MemoryStream()
            let buffer = Array.zeroCreate<byte> 16384
            let mutable finished, tooLarge = false, false
            while not finished && not tooLarge do
                let! read = context.Request.Body.ReadAsync(Memory buffer, context.RequestAborted)
                if read = 0 then finished <- true
                elif int64 read > limit - output.Length then tooLarge <- true
                else output.Write(buffer, 0, read)
            return if tooLarge then Error "Request body is too large." else Ok(output.ToArray())
    }

    let peerIdentity (context: HttpContext) (peer: PeerInfo) = task {
        match peer.Fingerprint |> Option.bind (Fingerprint.create >> Result.toOption) with
        | None -> return Error "Missing peer fingerprint."
        | Some fingerprint ->
            if options.EnableHttps then
                let! certificate = context.Connection.GetClientCertificateAsync context.RequestAborted
                match certificate with
                | null -> return Error "A client certificate is required."
                | certificate when IdentityStore.validatePeer (Some fingerprint) certificate -> return Ok fingerprint
                | _ -> return Error "Client certificate fingerprint mismatch."
            else return Ok fingerprint
    }

    let filesWithinLimits (files: IncomingItem list) =
        if files.IsEmpty || files.Length > options.MaxIncomingItemsPerTransfer then false
        else
            files |> List.fold (fun remaining file ->
                remaining |> Option.bind (fun bytes -> if file.Size < 0L || file.Size > bytes then None else Some(bytes - file.Size)))
                (Some options.MaxIncomingTransferBytes) |> Option.isSome

    let prepareResponse context = function
        | PrepareResult.Accepted acceptance -> json context 200 (Wire.encodeAcceptance acceptance)
        | PrepareResult.NoContent -> task { context.Response.StatusCode <- 204 }
        | PrepareResult.Declined -> error context 403 "Transfer declined."
        | PrepareResult.Busy -> error context 429 "Maximum concurrent transfers reached."
        | PrepareResult.TimedOut -> error context 408 "Receiver decision timed out."
        | PrepareResult.InvalidDestination message -> error context 400 message

    let webError context = function
        | WebShareError.Inactive -> error context 404 "No active share."
        | WebShareError.PinRequired -> error context 401 "PIN required or invalid."
        | WebShareError.PinRateLimited -> error context 429 "Too many PIN attempts."
        | WebShareError.Declined -> error context 403 "Transfer declined."
        | WebShareError.TimedOut -> error context 408 "Receiver decision timed out."
        | WebShareError.Capacity -> error context 429 "Maximum browser sessions reached."

    let register (context: HttpContext) = task {
        let! payload = body 65536L context
        match payload with
        | Error message -> do! error context 413 message
        | Ok bytes ->
            match Wire.decodePeer bytes with
            | Error message -> do! error context 400 message
            | Ok peer ->
                let! trusted = peerIdentity context peer
                match trusted with
                | Error message -> do! error context 403 message
                | Ok fingerprint ->
                    match Wire.toDevice (remote context) fingerprint peer with
                    | Error message -> do! error context 400 message
                    | Ok device ->
                        do! onRegister device
                        do! json context 200 (Wire.ofIdentity local |> Wire.encodePeer)
    }

    let prepare (context: HttpContext) = task {
        let! payload = body options.MaxPrepareRequestBytes context
        match payload with
        | Error message -> do! error context 413 message
        | Ok bytes ->
            match Wire.decodeOffer bytes with
            | Error message -> do! error context 400 message
            | Ok offer when not (filesWithinLimits offer.Files) -> do! error context 413 "Incoming transfer exceeds configured limits."
            | Ok offer ->
                let address = remote context
                let! trusted = peerIdentity context offer.Info
                match trusted with
                | Error message -> do! error context 403 message
                | Ok fingerprint ->
                    match pinGuard.Check(options.ReceivePin, query "pin" context, address) with
                    | PinAuthorization.Required _ -> do! error context 401 "PIN required or invalid."
                    | PinAuthorization.RateLimited -> do! error context 429 "Too many PIN attempts."
                    | PinAuthorization.Allowed ->
                        match Wire.toDevice address fingerprint offer.Info with
                        | Error message -> do! error context 400 message
                        | Ok device ->
                            do! onRegister device
                            let! result = sessions.Prepare(device, offer.Files, address, true, false, context.RequestAborted)
                            do! prepareResponse context result
    }

    let upload (context: HttpContext) = task {
        match query "sessionId" context |> Option.bind (SessionId.create >> Result.toOption),
              query "fileId" context |> Option.bind (FileId.create >> Result.toOption),
              query "token" context |> Option.bind (UploadToken.create >> Result.toOption) with
        | Some session, Some file, Some token ->
            let length = if context.Request.ContentLength.HasValue then Some context.Request.ContentLength.Value else None
            let! result = sessions.Upload(session, file, token, remote context, context.Request.Body, length, context.RequestAborted)
            match result with
            | Ok () -> context.Response.StatusCode <- 200
            | Error UploadError.NotFound -> do! error context 404 "Unknown or expired session."
            | Error UploadError.Forbidden -> do! error context 403 "Invalid upload credentials."
            | Error UploadError.Busy -> do! error context 429 "Maximum concurrent uploads reached."
            | Error(UploadError.Failed message) -> do! error context 400 message
        | _ -> do! error context 400 "Missing upload credentials."
    }

    let prepareWebUpload (context: HttpContext) = task {
        match web.AuthorizeReceive(remote context, query "pin" context) with
        | Error failure -> do! webError context failure
        | Ok automatic ->
            let! payload = body options.MaxPrepareRequestBytes context
            match payload with
            | Error message -> do! error context 413 message
            | Ok bytes ->
                match Wire.decodeWebOffer bytes with
                | Error message -> do! error context 400 message
                | Ok files when not (filesWithinLimits files) -> do! error context 413 "Incoming transfer exceeds configured limits."
                | Ok files ->
                    let address = remote context
                    let userAgent = context.Request.Headers.UserAgent.ToString()
                    let fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes($"{address}|{userAgent}")) |> Convert.ToHexString |> Fingerprint.ofCertificate
                    let endpoint = DeviceEndpoint.create address 1 Protocol.Http |> Result.defaultWith invalidOp
                    let device = { Alias = "Web browser"; Version = Wire.version; Model = Some userAgent; DeviceType = DeviceType.Web
                                   Fingerprint = fingerprint; SupportsDownload = false; Endpoints = [endpoint]; LastSeen = DateTimeOffset.UtcNow }
                    let! result = sessions.Prepare(device, files, address, false, automatic, context.RequestAborted)
                    do! prepareResponse context result
    }

    let download (context: HttpContext) = task {
        match query "sessionId" context |> Option.bind (SessionId.create >> Result.toOption),
              query "fileId" context |> Option.bind (FileId.create >> Result.toOption) with
        | Some session, Some file ->
            match web.Download(session, file, remote context, query "pin" context) with
            | Error failure -> do! webError context failure
            | Ok(metadata, source) ->
                use! stream = SendItems.openRead context.RequestAborted source
                context.Response.ContentType <- metadata.ContentType
                context.Response.ContentLength <- Nullable metadata.Size
                // RFC 5987 avoids raw filenames injecting response headers.
                context.Response.Headers.ContentDisposition <- $"attachment; filename*=UTF-8''{Uri.EscapeDataString metadata.FileName}"
                do! stream.CopyToAsync(context.Response.Body, context.RequestAborted)
        | _ -> do! error context 400 "Missing download credentials."
    }

    let dispatch (context: HttpContext) = task {
        let path = context.Request.Path.Value |> Option.ofObj |> Option.defaultValue ""
        match context.Request.Method, path with
        | "GET", "/api/localsend/v2/info" -> do! json context 200 (Wire.ofIdentity local |> Wire.encodePeer)
        | "POST", "/api/localsend/v2/register" -> do! register context
        | "POST", "/api/localsend/v2/prepare-upload" -> do! prepare context
        | "POST", "/api/localsend/v2/upload" -> do! upload context
        | "POST", "/api/localsend/v2/cancel" ->
            match query "sessionId" context |> Option.bind (SessionId.create >> Result.toOption) with
            | None -> do! error context 400 "Missing session ID."
            | Some session ->
                let! cancelled = onCancel session (remote context)
                context.Response.StatusCode <- if cancelled then 200 else 404
        | "GET", "/" ->
            let state = web.Snapshot
            if not state.Active then context.Response.StatusCode <- 404
            else
                context.Response.ContentType <- "text/html; charset=utf-8"
                do! context.Response.WriteAsync(WebSharePage.render local.Alias state, context.RequestAborted)
        | "POST", "/api/localsend/v2/prepare-download" ->
            let! result = web.Prepare(remote context, context.Request.Headers.UserAgent.ToString(), query "pin" context, context.RequestAborted)
            match result with
            | Ok(session, files) -> do! json context 200 (Wire.encodeDownload session files)
            | Error failure -> do! webError context failure
        | "GET", "/api/localsend/v2/download" -> do! download context
        | "POST", "/api/localsend/v2/prepare-web-upload" -> do! prepareWebUpload context
        | _ -> context.Response.StatusCode <- 404
    }

    member _.StartAsync(ct: CancellationToken) = task {
        let builder = WebApplication.CreateSlimBuilder(WebApplicationOptions(EnvironmentName = Environments.Production))
        builder.Logging.ClearProviders() |> ignore
        builder.WebHost.ConfigureKestrel(fun server ->
            server.Limits.MaxRequestBodySize <- Nullable()
            server.ListenAnyIP(options.Port, fun listen ->
                listen.Protocols <- HttpProtocols.Http1
                if options.EnableHttps then
                    listen.UseHttps(Action<HttpsConnectionAdapterOptions>(fun https ->
                        https.ServerCertificate <- identity.Certificate
                        https.ClientCertificateMode <- ClientCertificateMode.AllowCertificate
                        // Fingerprint binding is checked per native protocol request; browsers need no certificate.
                        https.ClientCertificateValidation <- fun _ _ _ -> true)) |> ignore)) |> ignore
        let app = builder.Build()
        application <- Some app
        app.Run(RequestDelegate(fun context -> dispatch context :> Task))
        do! app.StartAsync ct
    }

    member _.StopAsync() = task {
        match application with
        | None -> ()
        | Some app ->
            application <- None
            let! stopped = task {
                try
                    do! app.StopAsync()
                    return Ok ()
                with error -> return Error error
            }
            do! app.DisposeAsync()
            match stopped with Ok () -> () | Error error -> return raise error
    }
