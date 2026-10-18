namespace LocalSendDotNet.FSharp

open System
open System.Collections.Concurrent
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks

type private OutgoingOperation(endpoint: DeviceEndpoint, fingerprint: Fingerprint, caller: CancellationToken, lifetime: CancellationToken) =
    let gate = obj()
    let cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, lifetime)
    let token = cancellation.Token
    let finished = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable session = None
    let mutable cancelRequested = false
    member _.Endpoint = endpoint
    member _.Fingerprint = fingerprint
    member _.Token = token
    member _.Finished = finished.Task
    member _.Session with get () = lock gate (fun () -> session) and set value = lock gate (fun () -> session <- value)
    member _.RequestCancellation() = lock gate (fun () -> cancelRequested <- true)
    member _.CancellationRequested = lock gate (fun () -> cancelRequested || token.IsCancellationRequested)
    member _.Cancel() = lock gate (fun () ->
        cancelRequested <- true
        if not finished.Task.IsCompleted then cancellation.Cancel())
    member _.Finish() = lock gate (fun () ->
        finished.TrySetResult() |> ignore
        cancellation.Dispose())

/// Independent F# LocalSend node. Construct with validated options; it never delegates to the C# Core.
type LocalSendNode(configuration: ValidatedOptions) =
    let options = NodeOptions.value configuration
    let gate = obj()
    let lifecycle = new SemaphoreSlim(1, 1)
    let lifetime = new CancellationTokenSource()
    let deviceChanges = BroadcastHub<DeviceChange>(128, false)
    let incoming = BroadcastHub<IncomingRequest>(64, true)
    let progress = BroadcastHub<TransferProgress>(256, false)
    let stateChanges = BroadcastHub<NodeStateChange>(32, false)
    let webChanges = BroadcastHub<WebShareState>(32, false)
    let devices = DeviceStore deviceChanges
    let sessions = new ReceiveSessions(options, incoming, progress, lifetime.Token)
    let web = WebShareService(options, webChanges)
    let outgoing = ConcurrentDictionary<Guid, OutgoingOperation>()
    let networkOperations = ConcurrentDictionary<Guid, TaskCompletionSource<unit>>()
    let mutable state = NodeState.Created
    let mutable discoveryError = None
    let mutable identity: DeviceIdentity option = None
    let mutable client: HttpTransport option = None
    let mutable server: Server option = None
    let mutable discovery: MulticastDiscovery option = None
    let mutable maintenance = Task.CompletedTask
    let mutable disposed = false
    let mutable disposal: Task option = None

    let transition next = lock gate (fun () ->
        let previous = state
        state <- next
        stateChanges.Publish { Previous = previous; Current = next })

    let ensureRunning () = lock gate (fun () ->
        if disposed then raise (ObjectDisposedException "LocalSendNode")
        match state, client with
        | NodeState.Running, Some transport -> transport
        | _ -> invalidOp "Start the node before using network operations.")

    let rec isIdentityError (error: exn) =
        match error with
        | PeerIdentityException _ -> true
        | _ -> match error.InnerException with null -> false | inner -> isIdentityError inner

    let runNetwork (ct: CancellationToken) action = task {
        let id = Guid.NewGuid()
        let transport, finished = lock gate (fun () ->
            let transport = ensureRunning ()
            let finished = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
            networkOperations[id] <- finished
            transport, finished)
        use linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token)
        try return! action transport linked.Token
        finally
            finished.TrySetResult() |> ignore
            networkOperations.TryRemove id |> ignore
    }

    let localIdentity () = identity |> Option.map (fun certificate ->
        { Alias = options.Alias; Version = Wire.version; Model = options.DeviceModel; DeviceType = options.DeviceType
          Fingerprint = certificate.Fingerprint; Port = options.Port
          Protocol = if options.EnableHttps then Protocol.Https else Protocol.Http })

    let notify endpoint fingerprint session = task {
        match client with
        | None -> ()
        | Some transport ->
            try
                use timeout = new CancellationTokenSource(options.CancelRequestTimeout)
                do! transport.Cancel(endpoint, fingerprint, session, timeout.Token)
            with
            | :? OperationCanceledException -> ()
            | :? HttpRequestException as error -> Diagnostics.Debug.WriteLine error.Message
            | PeerIdentityException message -> Diagnostics.Debug.WriteLine message
    }

    let notifyOutgoing (operation: OutgoingOperation) = task {
        match operation.Session with
        | Some session -> do! notify operation.Endpoint operation.Fingerprint session
        | None -> ()
    }

    let notifyIncoming (session: IncomingSession) = task {
        session.RequestCancellation()
        if session.CanCancelSender then
            match session.Request.Sender.PreferredEndpoint with
            | Some endpoint -> do! notify endpoint session.Request.Sender.Fingerprint session.Request.SessionId
            | None -> ()
    }

    let onCancel session address = task {
        let received = sessions.CancelRemote(session, address)
        let mutable sent = false
        for operation in outgoing.Values do
            if operation.Session = Some session && operation.Endpoint.Address.Equals address then
                operation.Cancel()
                sent <- true
        return received || sent
    }

    let scan (transport: HttpTransport) (ct: CancellationToken) = task {
        use bound = new SemaphoreSlim(16, 16)
        let probe address = task {
            do! bound.WaitAsync ct
            try
                let preferred = if options.EnableHttps then [Protocol.Https; Protocol.Http] else [Protocol.Http; Protocol.Https]
                let mutable found = false
                for protocol in preferred do
                    if not found then
                        try
                            let endpoint = DeviceEndpoint.create address options.Port protocol |> Result.defaultWith invalidOp
                            let! result = transport.Probe(endpoint, ct, options.DiscoveryTimeout)
                            match localIdentity () with
                            | Some local when local.Fingerprint <> result.Device.Fingerprint ->
                                try
                                    let! _ = transport.Register(endpoint, result.Device.Fingerprint, local, ct)
                                    ()
                                with
                                | :? HttpRequestException as error -> Diagnostics.Debug.WriteLine error.Message
                                | :? OperationCanceledException when not ct.IsCancellationRequested -> ()
                                let! _ = devices.Upsert(result.Device, false)
                                ()
                            | Some _ | None -> ()
                            found <- true
                        with
                        | :? OperationCanceledException when not ct.IsCancellationRequested -> ()
                        | :? HttpRequestException -> ()
                        | :? InvalidDataException -> ()
                        | PeerIdentityException _ -> ()
            finally bound.Release() |> ignore
        }
        let! _ = LocalNetwork.scanTargets options |> Seq.map probe |> Seq.toArray |> Task.WhenAll
        return ()
    }

    let stopResources () = task {
        match discovery with
        | Some current ->
            do! current.StopAsync()
            (current :> IDisposable).Dispose()
            discovery <- None
        | None -> ()
        match server with
        | Some current ->
            do! current.StopAsync()
            server <- None
        | None -> ()
        web.Stop()
    }

    member _.State = lock gate (fun () -> state)
    member _.DiscoveryError = lock gate (fun () -> discoveryError)
    member _.Identity = localIdentity ()
    member _.WatchStateChanges() = stateChanges.Subscribe()
    member _.WatchDevices() = deviceChanges.Subscribe()
    member _.WatchIncomingTransfers() = incoming.Subscribe()
    member _.WatchProgress() = progress.Subscribe()
    member _.WatchWebShare() = webChanges.Subscribe()
    member _.GetDevicesAsync() = lock gate (fun () ->
        if disposed then raise (ObjectDisposedException "LocalSendNode")
        devices.Snapshot())
    member _.PendingIncomingTransfers = sessions.Pending
    member _.WebShare = web.Snapshot

    member _.StartAsync(ct: CancellationToken) = task {
        do! lifecycle.WaitAsync ct
        try
            if disposed then raise (ObjectDisposedException "LocalSendNode")
            match state with
            | NodeState.Running -> ()
            | NodeState.Stopping | NodeState.Stopped | NodeState.Disposed -> invalidOp "A stopped node cannot be restarted."
            | NodeState.Created | NodeState.Starting | NodeState.Faulted _ ->
                transition NodeState.Starting
                try
                    Directory.CreateDirectory options.DownloadDirectory |> ignore
                    let! certificate = IdentityStore.loadOrCreate options.DataDirectory ct
                    identity <- Some certificate
                    let transport = HttpTransport(certificate, options)
                    client <- Some transport
                    let local = localIdentity () |> Option.defaultWith (fun () -> invalidOp "Identity did not load.")
                    let registration device = task { let! _ = devices.Upsert(device, false) in return () }
                    let currentServer = Server(options, certificate, local, sessions, web, (fun device -> registration device :> Task), onCancel)
                    server <- Some currentServer
                    do! currentServer.StartAsync ct
                    let announce peer address cancellation = task {
                        match peer.Fingerprint |> Option.bind (Fingerprint.create >> Result.toOption) with
                        | None -> ()
                        | Some fingerprint when fingerprint = local.Fingerprint -> ()
                        | Some fingerprint ->
                            let endpoint = DeviceEndpoint.create address peer.Port peer.Protocol |> Result.defaultWith invalidOp
                            let! device = transport.Register(endpoint, fingerprint, local, cancellation)
                            let! _ = devices.Upsert(device, false)
                            ()
                    }
                    let currentDiscovery = new MulticastDiscovery(options, local, (fun peer address token -> announce peer address token :> Task),
                                               (fun error -> lock gate (fun () -> discoveryError <- error)), lifetime.Token)
                    discovery <- Some currentDiscovery
                    do! currentDiscovery.StartAsync ct
                    transition NodeState.Running
                    maintenance <- task {
                        try
                            if not currentDiscovery.Available then do! scan transport lifetime.Token
                            while not lifetime.IsCancellationRequested do
                                do! Task.Delay(options.AnnouncementInterval, lifetime.Token)
                                let! _ = devices.Expire(DateTimeOffset.UtcNow - options.DeviceExpiration)
                                if not currentDiscovery.Available then do! scan transport lifetime.Token
                        with :? OperationCanceledException when lifetime.IsCancellationRequested -> ()
                    }
                with error ->
                    do! stopResources ()
                    identity |> Option.iter (fun value -> (value :> IDisposable).Dispose())
                    identity <- None
                    client <- None
                    transition (if ct.IsCancellationRequested then NodeState.Created else NodeState.Faulted error)
                    return raise error
        finally lifecycle.Release() |> ignore
    }

    member _.StopAsync(ct: CancellationToken) = task {
        do! lifecycle.WaitAsync ct
        try
            match state with
            | NodeState.Stopped | NodeState.Disposed -> ()
            | NodeState.Created | NodeState.Starting | NodeState.Running | NodeState.Stopping | NodeState.Faulted _ ->
                transition NodeState.Stopping
                let active = outgoing.Values |> Seq.toArray
                let network = networkOperations.Values |> Seq.toArray
                for operation in active do operation.RequestCancellation()
                // Tell accepted peers before aborting bodies or releasing the TLS identity.
                let! _ = Task.WhenAll(Array.append (active |> Array.map notifyOutgoing) (sessions.Active |> List.map notifyIncoming |> List.toArray))
                lifetime.Cancel()
                do! sessions.StopAsync()
                do! maintenance
                let! _ = Task.WhenAll(network |> Array.map _.Task)
                do! stopResources ()
                let! _ = Task.WhenAll(active |> Array.map _.Finished)
                identity |> Option.iter (fun value -> (value :> IDisposable).Dispose())
                identity <- None
                client <- None
                transition NodeState.Stopped
                deviceChanges.Complete()
                incoming.Complete()
                progress.Complete()
                webChanges.Complete()
        finally lifecycle.Release() |> ignore
    }

    member _.RefreshAsync(ct: CancellationToken) = runNetwork ct (fun transport token -> task {
        match discovery with
        | Some current ->
            do! current.RefreshAsync token
            if not current.Available then do! scan transport token
        | None -> do! scan transport token
    })

    member _.ProbeAsync(endpoint, ct: CancellationToken) = runNetwork ct (fun transport token ->
        transport.Probe(endpoint, token, options.RequestTimeout))

    member _.AddKnownDeviceAsync(endpoint, fingerprint, ct: CancellationToken) = runNetwork ct (fun transport token -> task {
        let local = localIdentity () |> Option.defaultWith (fun () -> invalidOp "No local identity.")
        let! device = transport.Register(endpoint, fingerprint, local, token)
        return! devices.Upsert(device, true)
    })

    member _.RemoveDeviceAsync(fingerprint) = lock gate (fun () ->
        if disposed then raise (ObjectDisposedException "LocalSendNode")
        devices.Remove fingerprint)

    member _.AcceptAsync(requestId, receiveOptions, ct: CancellationToken) = runNetwork ct (fun _ token -> task {
        try return! sessions.Accept(requestId, receiveOptions, token)
        with :? OperationCanceledException ->
            match sessions.Active |> List.tryFind (fun session -> session.Request.RequestId = requestId) with
            | Some session ->
                do! notifyIncoming session
                session.Cancel()
                return! session.Completion
            | None -> return raise (OperationCanceledException token)
    })

    member _.Decline(requestId) = sessions.Decline requestId

    member _.CancelTransferAsync(transferId) = runNetwork CancellationToken.None (fun _ _ -> task {
        match outgoing.TryGetValue transferId with
        | true, operation ->
            operation.RequestCancellation()
            do! notifyOutgoing operation
            operation.Cancel()
            return true
        | false, _ ->
            match sessions.Active |> List.tryFind (fun session -> session.Request.TransferId = transferId) with
            | None -> return false
            | Some session ->
                do! notifyIncoming session
                session.Cancel()
                return true
    })

    member _.SendAsync(device: Device, items: SendItem list, sendOptions: SendOptions, ct: CancellationToken) = task {
        ct.ThrowIfCancellationRequested()
        let id = Guid.NewGuid()
        let transport, operation, local = lock gate (fun () ->
            let transport = ensureRunning ()
            let endpoint = device.PreferredEndpoint |> Option.defaultWith (fun () -> invalidArg (nameof device) "Peer has no endpoint.")
            let operation = OutgoingOperation(endpoint, device.Fingerprint, ct, lifetime.Token)
            outgoing[id] <- operation
            transport, operation, localIdentity () |> Option.defaultWith (fun () -> invalidOp "No local identity."))
        let mutable completed = []
        let mutable total, transferred = 0L, 0L
        let mutable failureCode = FailureCode.PrepareFailed
        let report phase item bytes =
            progress.Publish { TransferId = id; ItemId = item; Direction = TransferDirection.Send; Phase = phase
                               BytesTransferred = transferred + bytes; TotalBytes = total }
        try
            try
                report TransferPhase.Preparing None 0L
                let! prepared = OutgoingOffer.prepare local sendOptions.ComputeSha256 items operation.Token
                match prepared with
                | Error failure -> report TransferPhase.Failed None 0L; return SendOutcome.Failed(id, [], failure)
                | Ok offer ->
                    total <- offer.Wire.Files |> List.sumBy _.Size
                    report TransferPhase.WaitingForAcceptance None 0L
                    let! negotiation = transport.Prepare(operation.Endpoint, device.Fingerprint, offer.Wire, sendOptions.Pin, operation.Token)
                    match negotiation with
                    | Negotiation.PinRequired invalid -> return SendOutcome.PinRequired(id, invalid)
                    | Negotiation.PinRateLimited -> return SendOutcome.PinRateLimited id
                    | Negotiation.Busy -> return SendOutcome.PeerBusy id
                    | Negotiation.Declined -> return SendOutcome.Declined id
                    | Negotiation.NoUpload ->
                        completed <- offer.Wire.Files |> List.map (fun item ->
                            { Id = item.Id; FileName = item.FileName; BytesTransferred = item.Size; SavedPath = None })
                        transferred <- total
                        report TransferPhase.Completed None 0L
                        return SendOutcome.Completed(id, completed)
                    | Negotiation.Accepted acceptance ->
                        operation.Session <- Some acceptance.SessionId
                        if acceptance.Tokens |> Map.exists (fun file _ -> not (Map.containsKey file offer.Sources)) then
                            raise (InvalidDataException "Peer accepted an unknown file ID.")
                        let selected = offer.Wire.Files |> List.filter (fun item -> Map.containsKey item.Id acceptance.Tokens)
                        total <- selected |> List.sumBy _.Size
                        failureCode <- FailureCode.UploadFailed
                        for item in selected do
                            let! result = transport.Upload(operation.Endpoint, device.Fingerprint, acceptance, item, offer.Sources[item.Id],
                                                           (fun bytes -> report TransferPhase.Transferring (Some item.Id) bytes), operation.Token)
                            completed <- completed @ [result]
                            transferred <- transferred + result.BytesTransferred
                        report TransferPhase.Completed None 0L
                        return SendOutcome.Completed(id, completed)
            with
            | :? OperationCanceledException when operation.Token.IsCancellationRequested ->
                do! notifyOutgoing operation
                report TransferPhase.Cancelled None 0L
                return SendOutcome.Cancelled(id, completed)
            | :? OperationCanceledException as error ->
                do! notifyOutgoing operation
                report TransferPhase.Failed None 0L
                return SendOutcome.Failed(id, completed, { Code = FailureCode.TransferTimeout; Message = error.Message; ItemId = None })
            | PeerIdentityException message ->
                report TransferPhase.Failed None 0L
                return SendOutcome.Failed(id, completed, { Code = FailureCode.PeerIdentity; Message = message; ItemId = None })
            | :? HttpRequestException as error ->
                do! notifyOutgoing operation
                if operation.CancellationRequested then
                    report TransferPhase.Cancelled None 0L
                    return SendOutcome.Cancelled(id, completed)
                else
                    report TransferPhase.Failed None 0L
                    let code = if isIdentityError error then FailureCode.PeerIdentity else failureCode
                    return SendOutcome.Failed(id, completed, { Code = code; Message = error.Message; ItemId = None })
            | :? InvalidDataException as error ->
                do! notifyOutgoing operation
                report TransferPhase.Failed None 0L
                return SendOutcome.Failed(id, completed, { Code = failureCode; Message = error.Message; ItemId = None })
            | :? IOException as error ->
                do! notifyOutgoing operation
                if operation.CancellationRequested then
                    report TransferPhase.Cancelled None 0L
                    return SendOutcome.Cancelled(id, completed)
                else
                    report TransferPhase.Failed None 0L
                    return SendOutcome.Failed(id, completed, { Code = FailureCode.SourceIo; Message = error.Message; ItemId = None })
            | :? UnauthorizedAccessException as error ->
                do! notifyOutgoing operation
                report TransferPhase.Failed None 0L
                return SendOutcome.Failed(id, completed, { Code = FailureCode.SourceIo; Message = error.Message; ItemId = None })
        finally
            operation.Finish()
            outgoing.TryRemove id |> ignore
    }

    member _.StartWebShare(items, shareOptions) = ensureRunning () |> ignore; web.Start(items, shareOptions)
    member _.StartWebReceive(shareOptions) = ensureRunning () |> ignore; web.StartReceive shareOptions
    member _.StopWebShare() = web.Stop()
    member _.SetWebShareAutoAccept(value) = web.SetAutoAccept value
    member _.SetWebSharePin(pin) = web.SetPin pin
    member _.AcceptWebShareRequest(session) = web.Decide(session, true)
    member _.DeclineWebShareRequest(session) = web.Decide(session, false)
    member _.GetWebShareUrls() =
        ensureRunning () |> ignore
        let scheme = if options.EnableHttps then "https" else "http"
        LocalNetwork.addresses options |> List.map (fun address -> UriBuilder(scheme, string address, options.Port).Uri)

    interface IAsyncDisposable with
        member this.DisposeAsync() =
            let work = lock gate (fun () ->
                match disposal with
                | Some work -> work
                | None ->
                    let work = task {
                        do! this.StopAsync CancellationToken.None
                        lock gate (fun () -> disposed <- true)
                        transition NodeState.Disposed
                        stateChanges.Complete()
                        do! devices.StopAsync()
                        (sessions :> IDisposable).Dispose()
                        lifetime.Dispose()
                        // Retain the small lifecycle gate so racing Stop/Dispose calls observe the final state.
                    }
                    disposal <- Some(work :> Task)
                    work :> Task)
            ValueTask work

[<RequireQualifiedAccess>]
module Node =
    let create options = NodeOptions.validate options |> Result.map (fun validated -> new LocalSendNode(validated))
