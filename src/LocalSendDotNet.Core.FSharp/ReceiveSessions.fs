namespace LocalSendDotNet.FSharp

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Net
open System.Threading
open System.Threading.Tasks

type internal UploadTarget = { Item: IncomingItem; Path: string; VerifySha256: bool }

type private AcceptedSession =
    { Targets: Map<FileId, UploadTarget>
      Tokens: Map<FileId, UploadToken>
      Results: Map<FileId, TransferredItem>
      Progress: Map<FileId, int64> }

[<RequireQualifiedAccess>]
type private ReceiveState =
    | AwaitingDecision
    | Receiving of AcceptedSession
    | Finished of ReceiveOutcome

[<RequireQualifiedAccess>]
type internal PrepareResult =
    | Accepted of UploadAcceptance
    | NoContent
    | Declined
    | Busy
    | TimedOut
    | InvalidDestination of string

[<RequireQualifiedAccess>]
type internal UploadError = NotFound | Forbidden | Busy | Failed of string

/// Session metadata is synchronized separately from body I/O. File uploads may complete in any order.
type internal IncomingSession(request: IncomingRequest, address: IPAddress, canCancelSender: bool,
                              defaultDirectory: string, progress: BroadcastHub<TransferProgress>, lifetime: CancellationToken) =
    let gate = obj()
    let cancellation = CancellationTokenSource.CreateLinkedTokenSource lifetime
    let token = cancellation.Token
    let decision = TaskCompletionSource<ReceiveDecision>(TaskCreationOptions.RunContinuationsAsynchronously)
    let completion = TaskCompletionSource<ReceiveOutcome>(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable state = ReceiveState.AwaitingDecision
    let mutable cancellationRequested = false
    let mutable totalBytes = request.Items |> List.sumBy _.Size
    let mutable receivedBytes = 0L

    let items (accepted: AcceptedSession) =
        request.Items |> List.choose (fun item -> Map.tryFind item.Id accepted.Results)

    let report phase itemId =
        let transferred, total =
            match state with
            | ReceiveState.AwaitingDecision -> 0L, totalBytes
            | ReceiveState.Receiving accepted -> accepted.Progress |> Map.values |> Seq.sum, accepted.Targets |> Map.values |> Seq.sumBy _.Item.Size
            | ReceiveState.Finished outcome ->
                let completed = match outcome with
                                | ReceiveOutcome.Completed(_, items) | ReceiveOutcome.Cancelled(_, items)
                                | ReceiveOutcome.Failed(_, items, _) -> items
                max receivedBytes (completed |> List.sumBy _.BytesTransferred), totalBytes
        progress.Publish { TransferId = request.TransferId; ItemId = itemId; Direction = TransferDirection.Receive
                           Phase = phase; BytesTransferred = transferred; TotalBytes = total }

    let finish outcome =
        state <- ReceiveState.Finished outcome
        // Cancel bodies before publishing completion: the monitor may immediately dispose the CTS.
        match outcome with
        | ReceiveOutcome.Cancelled _ | ReceiveOutcome.Failed _ -> cancellation.Cancel()
        | ReceiveOutcome.Completed _ -> ()
        completion.TrySetResult outcome |> ignore
        let phase = match outcome with
                    | ReceiveOutcome.Completed _ -> TransferPhase.Completed
                    | ReceiveOutcome.Cancelled _ -> TransferPhase.Cancelled
                    | ReceiveOutcome.Failed _ -> TransferPhase.Failed
        report phase None

    member _.Request = request
    member _.Address = address
    member _.CanCancelSender = canCancelSender
    member _.CancellationToken = token
    member _.Completion = completion.Task
    member _.Decision = decision.Task
    member _.Decide(value) = decision.TrySetResult value
    member _.RequestCancellation() = lock gate (fun () -> cancellationRequested <- true)
    member _.CancellationRequested = lock gate (fun () -> cancellationRequested || token.IsCancellationRequested)
    member _.IsPending = lock gate (fun () -> match state with ReceiveState.AwaitingDecision -> not decision.Task.IsCompleted | ReceiveState.Receiving _ | ReceiveState.Finished _ -> false)

    member _.Configure(options: ReceiveOptions) = lock gate (fun () ->
        match state with
        | ReceiveState.Finished _ | ReceiveState.Receiving _ -> PrepareResult.Declined
        | ReceiveState.AwaitingDecision ->
            let selected = request.Items |> List.filter (fun item ->
                match options.Selection with ItemSelection.All -> true | ItemSelection.Only ids -> Set.contains item.Id ids)
            totalBytes <- selected |> List.sumBy _.Size
            match selected with
            | [] ->
                finish (ReceiveOutcome.Completed(request.TransferId, []))
                PrepareResult.NoContent
            | [item] when request.Items.Length = 1 && item.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && item.Preview.IsSome ->
                // The host presents the offered text; preview-only acceptance must not silently write a file.
                finish (ReceiveOutcome.Completed(request.TransferId,
                            [{ Id = item.Id; FileName = item.FileName; BytesTransferred = item.Size; SavedPath = None }]))
                PrepareResult.NoContent
            | selected ->
                try
                    let reserved = HashSet<string>(if OperatingSystem.IsWindows() then StringComparer.OrdinalIgnoreCase else StringComparer.Ordinal)
                    let root = options.DestinationDirectory |> Option.defaultValue defaultDirectory
                    let targets = selected |> List.map (fun item ->
                        let name = Map.tryFind item.Id options.TargetFileNames |> Option.defaultValue item.FileName
                        item.Id, { Item = item; Path = Storage.resolveUnique root name reserved; VerifySha256 = options.VerifySha256 }) |> Map.ofList
                    let tokens = targets |> Map.map (fun _ _ -> UploadToken.fresh())
                    state <- ReceiveState.Receiving { Targets = targets; Tokens = tokens; Results = Map.empty; Progress = Map.empty }
                    report TransferPhase.Transferring None
                    PrepareResult.Accepted { SessionId = request.SessionId; Tokens = tokens }
                with
                | StorageException(_, message) ->
                    finish (ReceiveOutcome.Failed(request.TransferId, [], { Code = FailureCode.InvalidDestination; Message = message; ItemId = None }))
                    PrepareResult.InvalidDestination message
                | :? IOException as error ->
                    finish (ReceiveOutcome.Failed(request.TransferId, [], { Code = FailureCode.InvalidDestination; Message = error.Message; ItemId = None }))
                    PrepareResult.InvalidDestination error.Message
                | :? UnauthorizedAccessException as error ->
                    finish (ReceiveOutcome.Failed(request.TransferId, [], { Code = FailureCode.InvalidDestination; Message = error.Message; ItemId = None }))
                    PrepareResult.InvalidDestination error.Message)

    member _.Claim(fileId, token, remoteAddress) = lock gate (fun () ->
        match state with
        | ReceiveState.Receiving accepted when address.Equals remoteAddress ->
            match Map.tryFind fileId accepted.Tokens with
            | Some expected when expected = token ->
                state <- ReceiveState.Receiving { accepted with Tokens = Map.remove fileId accepted.Tokens }
                Some accepted.Targets[fileId]
            | Some _ | None -> None
        | ReceiveState.AwaitingDecision | ReceiveState.Receiving _ | ReceiveState.Finished _ -> None)

    member _.Report(fileId, count) = lock gate (fun () ->
        match state with
        | ReceiveState.Receiving accepted ->
            let updated = { accepted with Progress = Map.add fileId count accepted.Progress }
            receivedBytes <- updated.Progress |> Map.values |> Seq.sum
            state <- ReceiveState.Receiving updated
            report TransferPhase.Transferring (Some fileId)
        | ReceiveState.AwaitingDecision | ReceiveState.Finished _ -> ())

    member _.CompleteItem(item: TransferredItem) = lock gate (fun () ->
        match state with
        | ReceiveState.Receiving accepted ->
            let updated = { accepted with Results = Map.add item.Id item accepted.Results; Progress = Map.add item.Id item.BytesTransferred accepted.Progress }
            state <- ReceiveState.Receiving updated
            if updated.Results.Count = updated.Targets.Count then finish (ReceiveOutcome.Completed(request.TransferId, items updated))
        | ReceiveState.AwaitingDecision | ReceiveState.Finished _ -> ())

    member _.Fail(failure: TransferFailure) =
        lock gate (fun () ->
            match state with
            | ReceiveState.Finished _ -> ()
            | ReceiveState.AwaitingDecision -> finish (ReceiveOutcome.Failed(request.TransferId, [], failure))
            | ReceiveState.Receiving accepted -> finish (ReceiveOutcome.Failed(request.TransferId, items accepted, failure)))

    member _.Cancel() =
        lock gate (fun () ->
            cancellationRequested <- true
            match state with
            | ReceiveState.Finished _ -> ()
            | ReceiveState.AwaitingDecision -> finish (ReceiveOutcome.Cancelled(request.TransferId, []))
            | ReceiveState.Receiving accepted -> finish (ReceiveOutcome.Cancelled(request.TransferId, items accepted)))
        decision.TrySetResult ReceiveDecision.Decline |> ignore

    interface IDisposable with member _.Dispose() = cancellation.Dispose()

type internal ReceiveSessions(options: NodeOptions, offers: BroadcastHub<IncomingRequest>,
                              progress: BroadcastHub<TransferProgress>, lifetime: CancellationToken) =
    let sessions = ConcurrentDictionary<SessionId, IncomingSession>()
    let requests = ConcurrentDictionary<Guid, IncomingSession>()
    let monitors = ConcurrentDictionary<SessionId, Task>()
    let capacity = new SemaphoreSlim(options.MaxConcurrentTransfers)
    let uploads = new SemaphoreSlim(options.MaxConcurrentFileUploads)

    let release (session: IncomingSession) =
        sessions.TryRemove session.Request.SessionId |> ignore
        requests.TryRemove session.Request.RequestId |> ignore
        capacity.Release() |> ignore

    let watch (session: IncomingSession) = task {
        try
            try
                let! _ = session.Completion.WaitAsync(options.IncomingTransferTimeout, lifetime)
                ()
            with
            | :? TimeoutException -> session.Fail { Code = FailureCode.TransferTimeout; Message = "Incoming transfer expired."; ItemId = None }
            | :? OperationCanceledException -> session.Cancel()
        finally
            release session
            (session :> IDisposable).Dispose()
    }

    member _.Pending = requests.Values |> Seq.filter _.IsPending |> Seq.map _.Request |> Seq.toList
    member _.Active = sessions.Values |> Seq.toList

    member _.Prepare(sender: Device, files: IncomingItem list, address: IPAddress, canCancelSender: bool,
                     automatic: bool, ct: CancellationToken) = task {
        if not (capacity.Wait 0) then return PrepareResult.Busy
        else
            let request = { RequestId = Guid.NewGuid(); TransferId = Guid.NewGuid(); SessionId = SessionId.fresh()
                            Sender = sender; Items = files; ReceivedAt = DateTimeOffset.UtcNow }
            let session = new IncomingSession(request, address, canCancelSender, options.DownloadDirectory, progress, lifetime)
            sessions[request.SessionId] <- session
            requests[request.RequestId] <- session
            let mutable accepted = false
            try
                try
                    use decisionLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime)
                    decisionLifetime.CancelAfter options.IncomingDecisionTimeout
                    if automatic then session.Decide(ReceiveDecision.Accept ReceiveOptions.Default) |> ignore
                    else do! offers.PublishAsync(request, decisionLifetime.Token)
                    let! decision = session.Decision.WaitAsync decisionLifetime.Token
                    match decision with
                    | ReceiveDecision.Decline -> session.Cancel(); return PrepareResult.Declined
                    | ReceiveDecision.Accept receiveOptions ->
                        let result = session.Configure receiveOptions
                        match result with
                        | PrepareResult.Accepted _ ->
                            accepted <- true
                            let monitor = watch session
                            monitors[request.SessionId] <- monitor
                            // Remove finished monitor entries without losing an in-flight shutdown snapshot.
                            monitor.ContinueWith((fun (_: Task) -> monitors.TryRemove request.SessionId |> ignore), TaskScheduler.Default) |> ignore
                        | PrepareResult.NoContent | PrepareResult.Declined | PrepareResult.Busy
                        | PrepareResult.TimedOut | PrepareResult.InvalidDestination _ -> ()
                        return result
                with :? OperationCanceledException ->
                    session.Cancel()
                    if ct.IsCancellationRequested || lifetime.IsCancellationRequested then return raise (OperationCanceledException ct)
                    else return PrepareResult.TimedOut
            finally
                if not accepted then
                    session.Cancel()
                    release session
                    (session :> IDisposable).Dispose()
    }

    member _.Accept(requestId, receiveOptions, ct: CancellationToken) = task {
        match requests.TryGetValue requestId with
        | false, _ -> return invalidArg (nameof requestId) "No pending request with this ID."
        | true, session ->
            ct.ThrowIfCancellationRequested()
            let known = session.Request.Items |> List.map _.Id |> Set.ofList
            match receiveOptions.Selection with
            | ItemSelection.Only selected when not (Set.isSubset selected known) -> invalidArg (nameof receiveOptions) "Selection contains an unknown file ID."
            | ItemSelection.All | ItemSelection.Only _ -> ()
            if receiveOptions.TargetFileNames |> Map.exists (fun id _ -> not (Set.contains id known)) then
                invalidArg (nameof receiveOptions) "Target filenames contain an unknown file ID."
            if not (session.Decide(ReceiveDecision.Accept receiveOptions)) then invalidOp "This request already has a decision."
            return! session.Completion.WaitAsync ct
    }

    member _.Decline(requestId) =
        match requests.TryGetValue requestId with
        | false, _ -> false
        | true, session -> session.Decide ReceiveDecision.Decline

    member _.CancelRemote(sessionId, address) =
        match sessions.TryGetValue sessionId with
        | true, session when session.Address.Equals address -> session.Cancel(); true
        | true, _ | false, _ -> false

    member private _.UploadCore(sessionId, fileId, token, address, body: Stream, contentLength: int64 option, ct: CancellationToken) = task {
        try
            match sessions.TryGetValue sessionId with
            | false, _ -> return Error UploadError.NotFound
            | true, session ->
                match session.Claim(fileId, token, address) with
                | None -> return Error UploadError.Forbidden
                | Some target ->
                    use linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime, session.CancellationToken)
                    try
                        if contentLength |> Option.exists ((<>) target.Item.Size) then
                            raise (StorageException(FailureCode.LengthMismatch, "Content-Length differs from the offer."))
                        let! item = Storage.receive body target.Path target.Item target.VerifySha256 (fun bytes -> session.Report(fileId, bytes)) linked.Token
                        session.CompleteItem item
                        return Ok ()
                    with
                    | :? OperationCanceledException -> session.Cancel(); return Error(UploadError.Failed "Transfer cancelled.")
                    | StorageException(code, message) ->
                        if session.CancellationRequested then session.Cancel()
                        else session.Fail { Code = code; Message = message; ItemId = Some fileId }
                        return Error(UploadError.Failed message)
                    | :? IOException as error ->
                        if session.CancellationRequested then session.Cancel()
                        else session.Fail { Code = FailureCode.ReceiveFailed; Message = error.Message; ItemId = Some fileId }
                        return Error(UploadError.Failed error.Message)
                    | :? UnauthorizedAccessException as error ->
                        session.Fail { Code = FailureCode.ReceiveFailed; Message = error.Message; ItemId = Some fileId }
                        return Error(UploadError.Failed error.Message)
        finally uploads.Release() |> ignore
    }

    member this.Upload(sessionId, fileId, token, address, body, contentLength, ct) =
        if uploads.Wait(0, ct) then this.UploadCore(sessionId, fileId, token, address, body, contentLength, ct)
        else Task.FromResult(Error UploadError.Busy)

    member _.StopAsync() = task {
        for session in sessions.Values do session.Cancel()
        do! Task.WhenAll(monitors.Values |> Seq.toArray)
    }

    interface IDisposable with
        member _.Dispose() =
            for session in sessions.Values do (session :> IDisposable).Dispose()
            capacity.Dispose()
            uploads.Dispose()
