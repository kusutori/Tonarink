namespace LocalSendDotNet.FSharp

open LocalSendDotNet.Resources

open System
open System.Net
open System.Threading
open System.Threading.Tasks

[<RequireQualifiedAccess>]
type internal WebShareError = Inactive | PinRequired | PinRateLimited | Declined | TimedOut | Capacity

type private BrowserSession =
    { Request: WebShareRequest
      Decision: TaskCompletionSource<bool>
      CreatedAt: DateTimeOffset }

type internal WebShareService(options: NodeOptions, changes: BroadcastHub<WebShareState>) =
    let gate = obj()
    let pinGuard = PinGuard options.PinLockoutDuration
    let mutable state = WebShareState.Inactive
    let mutable offered: Map<FileId, WebShareFile * SendItem> = Map.empty
    let mutable sessions: Map<SessionId, BrowserSession> = Map.empty

    let normalize (shareOptions: WebShareOptions) =
        let clean (value: string option) = value |> Option.map _.Trim() |> Option.filter (String.IsNullOrEmpty >> not)
        { shareOptions with Pin = clean shareOptions.Pin; UiCulture = clean shareOptions.UiCulture }

    let snapshot () = { state with Requests = sessions |> Map.values |> Seq.sortBy _.CreatedAt |> Seq.map _.Request |> Seq.toList }
    let publish () = changes.Publish(snapshot ())
    let reset () =
        for session in Map.values sessions do session.Decision.TrySetResult false |> ignore
        sessions <- Map.empty
        pinGuard.Reset()

    let checkPin address pin =
        match pinGuard.Check(state.Options.Pin, pin, address) with
        | PinAuthorization.Allowed -> Ok ()
        | PinAuthorization.Required _ -> Error WebShareError.PinRequired
        | PinAuthorization.RateLimited -> Error WebShareError.PinRateLimited

    let expire () =
        let cutoff = DateTimeOffset.UtcNow - options.IncomingTransferTimeout
        let expired, retained = sessions |> Map.partition (fun _ session -> session.CreatedAt < cutoff)
        sessions <- retained
        for session in Map.values expired do session.Decision.TrySetResult false |> ignore

    member _.Snapshot = lock gate snapshot

    member _.Start(items: SendItem list, shareOptions: WebShareOptions) =
        if items.IsEmpty then invalidArg (nameof items) (CoreText.AtLeastOneItem)
        let entries = items |> List.map (fun item ->
            let file = { Id = FileId.fresh(); FileName = SendItems.fileName item; Size = SendItems.length item; ContentType = SendItems.contentType item }
            file.Id, (file, item))
        lock gate (fun () ->
            reset ()
            offered <- Map.ofList entries
            state <- { Active = true; Mode = WebShareMode.Send; Files = entries |> List.map (snd >> fst)
                       Requests = []; Options = normalize shareOptions }
            publish ())

    member _.StartReceive(shareOptions) = lock gate (fun () ->
        reset ()
        offered <- Map.empty
        state <- { Active = true; Mode = WebShareMode.Receive; Files = []; Requests = []; Options = normalize shareOptions }
        publish ())

    member _.Stop() = lock gate (fun () ->
        reset ()
        offered <- Map.empty
        state <- WebShareState.Inactive
        publish ())

    member _.SetAutoAccept(value) = lock gate (fun () ->
        state <- { state with Options = { state.Options with AutoAccept = value } }
        if value then
            sessions <- sessions |> Map.map (fun _ session ->
                session.Decision.TrySetResult true |> ignore
                { session with Request = { session.Request with Pending = false } })
        publish ())

    member _.SetPin(pin: string option) = lock gate (fun () ->
        state <- { state with Options = { state.Options with Pin = pin |> Option.map _.Trim() |> Option.filter (String.IsNullOrEmpty >> not) } }
        pinGuard.Reset()
        publish ())

    member _.Decide(sessionId, accept) = lock gate (fun () ->
        match Map.tryFind sessionId sessions with
        | Some session when session.Request.Pending ->
            if accept then sessions <- Map.add sessionId { session with Request = { session.Request with Pending = false } } sessions
            else sessions <- Map.remove sessionId sessions
            session.Decision.TrySetResult accept |> ignore
            publish ()
            true
        | Some _ | None -> false)

    member _.AuthorizeReceive(address, pin) = lock gate (fun () ->
        if not state.Active || state.Mode <> WebShareMode.Receive then Error WebShareError.Inactive
        else checkPin address pin |> Result.map (fun () -> state.Options.AutoAccept))

    member _.Prepare(address: IPAddress, userAgent: string, pin, ct: CancellationToken) = task {
        let pending = lock gate (fun () ->
            expire ()
            if not state.Active || state.Mode <> WebShareMode.Send then Error WebShareError.Inactive
            else
                checkPin address pin |> Result.bind (fun () ->
                    if sessions.Count >= 256 then Error WebShareError.Capacity
                    else
                        let id = SessionId.fresh()
                        let accepted = state.Options.AutoAccept
                        let browser =
                            { Request = { SessionId = id; DeviceInfo = userAgent; Address = address; Pending = not accepted }
                              Decision = TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                              CreatedAt = DateTimeOffset.UtcNow }
                        sessions <- Map.add id browser sessions
                        if accepted then browser.Decision.TrySetResult true |> ignore
                        publish ()
                        Ok(browser, state.Files)))
        match pending with
        | Error error -> return Error error
        | Ok(browser, files) ->
            try
                let! accepted = browser.Decision.Task.WaitAsync(options.IncomingDecisionTimeout, ct)
                return if accepted then Ok(browser.Request.SessionId, files) else Error WebShareError.Declined
            with
            | :? TimeoutException ->
                lock gate (fun () -> sessions <- Map.remove browser.Request.SessionId sessions; publish ())
                return Error WebShareError.TimedOut
            | :? OperationCanceledException ->
                lock gate (fun () -> sessions <- Map.remove browser.Request.SessionId sessions; publish ())
                return raise (OperationCanceledException ct)
    }

    member _.Download(sessionId, fileId, address: IPAddress, pin) = lock gate (fun () ->
        expire ()
        if not state.Active || state.Mode <> WebShareMode.Send then Error WebShareError.Inactive
        else checkPin address pin |> Result.bind (fun () ->
            match Map.tryFind sessionId sessions, Map.tryFind fileId offered with
            | Some session, Some file when not session.Request.Pending && session.Request.Address.Equals address -> Ok file
            | Some _, Some _ | Some _, None | None, Some _ | None, None -> Error WebShareError.Declined))
