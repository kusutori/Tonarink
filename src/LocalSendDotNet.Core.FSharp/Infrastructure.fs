namespace LocalSendDotNet.FSharp

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Net
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

/// Each subscriber owns its queue. Disposal releases reliable publishers waiting on that queue.
type internal BroadcastHub<'T>(capacity: int, reliable: bool) =
    let gate = obj()
    let subscribers = Dictionary<int64, Channel<'T>>()
    let mutable nextId = 0L
    let mutable completed = false

    let snapshot () = lock gate (fun () -> subscribers.Values |> Seq.toArray)

    member _.Publish(value: 'T) =
        for channel in snapshot () do channel.Writer.TryWrite value |> ignore

    member _.PublishAsync(value: 'T, ct: CancellationToken) = task {
        for channel in snapshot () do
            try do! channel.Writer.WriteAsync(value, ct)
            with :? ChannelClosedException -> ()
    }

    member _.Subscribe() : IAsyncEnumerable<'T> =
        { new IAsyncEnumerable<'T> with
            member _.GetAsyncEnumerator(ct) =
                let id, channel = lock gate (fun () ->
                    nextId <- nextId + 1L
                    let settings = BoundedChannelOptions(capacity)
                    settings.SingleReader <- true
                    settings.FullMode <- if reliable then BoundedChannelFullMode.Wait else BoundedChannelFullMode.DropOldest
                    let channel = Channel.CreateBounded<'T>(settings)
                    if completed then channel.Writer.TryComplete() |> ignore
                    else subscribers.Add(nextId, channel)
                    nextId, channel)
                let reader = channel.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct)
                { new IAsyncEnumerator<'T> with
                    member _.Current = reader.Current
                    member _.MoveNextAsync() = reader.MoveNextAsync()
                    member _.DisposeAsync() =
                        lock gate (fun () -> subscribers.Remove id |> ignore)
                        channel.Writer.TryComplete() |> ignore
                        reader.DisposeAsync() } }

    member _.Complete() = lock gate (fun () ->
        completed <- true
        for channel in subscribers.Values do channel.Writer.TryComplete() |> ignore
        subscribers.Clear())

type private DeviceCommand =
    | Upsert of Device * persistent: bool * AsyncReplyChannel<Device>
    | Remove of Fingerprint * AsyncReplyChannel<bool>
    | Expire of DateTimeOffset * AsyncReplyChannel<int>
    | Snapshot of AsyncReplyChannel<Device list>
    | Finish of AsyncReplyChannel<unit>

/// The mailbox owns only metadata; no network or file I/O runs inside its loop.
type internal DeviceStore(changes: BroadcastHub<DeviceChange>) =
    let agent = MailboxProcessor.Start(fun inbox ->
        let rec loop devices persistent = async {
            let! command = inbox.Receive()
            match command with
            | Upsert(candidate, keep, reply) ->
                let updated, change =
                    match Map.tryFind candidate.Fingerprint devices with
                    | None -> candidate, DeviceChange.Added candidate
                    | Some previous ->
                        let merged = { candidate with Endpoints = List.distinct (previous.Endpoints @ candidate.Endpoints) }
                        merged, DeviceChange.Updated merged
                changes.Publish change
                reply.Reply updated
                return! loop (Map.add updated.Fingerprint updated devices)
                             (if keep then Set.add updated.Fingerprint persistent else persistent)
            | Remove(fingerprint, reply) ->
                match Map.tryFind fingerprint devices with
                | Some device -> changes.Publish(DeviceChange.Removed device); reply.Reply true
                | None -> reply.Reply false
                return! loop (Map.remove fingerprint devices) (Set.remove fingerprint persistent)
            | Expire(cutoff, reply) ->
                let expired, retained = devices |> Map.partition (fun id device -> not (Set.contains id persistent) && device.LastSeen < cutoff)
                for KeyValue(_, device) in expired do changes.Publish(DeviceChange.Removed device)
                reply.Reply expired.Count
                return! loop retained persistent
            | Snapshot reply ->
                devices |> Map.values |> Seq.sortWith (fun a b -> StringComparer.OrdinalIgnoreCase.Compare(a.Alias, b.Alias))
                |> Seq.toList |> reply.Reply
                return! loop devices persistent
            | Finish reply -> reply.Reply()
        }
        loop Map.empty Set.empty)

    member _.Upsert(device, persistent) = agent.PostAndAsyncReply(fun reply -> Upsert(device, persistent, reply)) |> Async.StartImmediateAsTask
    member _.Remove(id) = agent.PostAndAsyncReply(fun reply -> Remove(id, reply)) |> Async.StartImmediateAsTask
    member _.Expire(cutoff) = agent.PostAndAsyncReply(fun reply -> Expire(cutoff, reply)) |> Async.StartImmediateAsTask
    member _.Snapshot() = agent.PostAndAsyncReply Snapshot |> Async.StartImmediateAsTask
    member _.StopAsync() = agent.PostAndAsyncReply Finish |> Async.StartImmediateAsTask

[<RequireQualifiedAccess>]
type internal PinAuthorization = Allowed | Required of invalid: bool | RateLimited

type private PinAttempt = { Failures: int; LockedUntil: DateTimeOffset option }

/// Separate instances are used for native receiving and browser sharing.
type internal PinGuard(lockout: TimeSpan) =
    let gate = obj()
    let mutable attempts = Map.empty<string, PinAttempt>

    member _.Reset() = lock gate (fun () -> attempts <- Map.empty)

    member _.Check(expected: string option, supplied: string option, address: IPAddress) = lock gate (fun () ->
        match expected with
        | None -> PinAuthorization.Allowed
        | Some pin ->
            let key, now = string address, DateTimeOffset.UtcNow
            let previous =
                Map.tryFind key attempts
                |> Option.filter (fun attempt -> attempt.LockedUntil |> Option.forall (fun until -> until > now))
                |> Option.defaultValue { Failures = 0; LockedUntil = None }
            match previous.LockedUntil with
            | Some until when until > now -> PinAuthorization.RateLimited
            | Some _ | None ->
                match supplied with
                | Some value when value = pin ->
                    attempts <- Map.remove key attempts
                    PinAuthorization.Allowed
                | None | Some "" -> PinAuthorization.Required false
                | Some _ ->
                    let failures = previous.Failures + 1
                    let locked = if failures >= 3 then Some(now + lockout) else None
                    // Bound unauthenticated remote-address bookkeeping.
                    if attempts.Count >= 4096 && not (Map.containsKey key attempts) then
                        attempts <- attempts |> Map.filter (fun _ attempt -> attempt.LockedUntil |> Option.exists (fun until -> until > now))
                        if attempts.Count >= 4096 then
                            attempts <- Map.remove (attempts |> Map.keys |> Seq.head) attempts
                    attempts <- Map.add key { Failures = failures; LockedUntil = locked } attempts
                    PinAuthorization.Required true)
