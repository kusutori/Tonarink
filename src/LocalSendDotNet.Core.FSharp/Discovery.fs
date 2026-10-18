namespace LocalSendDotNet.FSharp

open System
open System.Collections.Concurrent
open System.Net
open System.Net.NetworkInformation
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks

[<RequireQualifiedAccess>]
module NetworkAddressPatterns =
    let matches (pattern: string) (address: string) =
        let patternParts, addressParts = pattern.Split('.'), address.Split('.')
        patternParts.Length = 4 && addressParts.Length = 4
        && Array.forall2 (fun expected actual -> expected = "*" || expected = actual) patternParts addressParts

    let isInterfaceIgnored addresses whitelist blacklist =
        let any patterns = addresses |> Seq.exists (fun address -> patterns |> List.exists (fun pattern -> matches pattern address))
        match whitelist, blacklist with
        | Some allow, _ -> not (any allow)
        | None, Some deny -> any deny
        | None, None -> false

[<RequireQualifiedAccess>]
module internal LocalNetwork =
    let private collectAddresses (options: NodeOptions) =
        NetworkInterface.GetAllNetworkInterfaces()
        |> Seq.filter (fun nic -> nic.OperationalStatus = OperationalStatus.Up && nic.NetworkInterfaceType <> NetworkInterfaceType.Loopback)
        |> Seq.collect (fun nic ->
            let ips = nic.GetIPProperties().UnicastAddresses |> Seq.map _.Address
                      |> Seq.filter (fun ip -> ip.AddressFamily = AddressFamily.InterNetwork && not (IPAddress.IsLoopback ip))
                      |> Seq.distinct |> Seq.toArray
            if NetworkAddressPatterns.isInterfaceIgnored (ips |> Seq.map string) options.NetworkWhitelist options.NetworkBlacklist then [||]
            else ips)
        |> Seq.distinct |> Seq.sortBy string |> Seq.toList

    let addresses options =
        try collectAddresses options
        with :? NetworkInformationException as error ->
            Diagnostics.Debug.WriteLine error.Message
            []

    let scanTargets options =
        let local = addresses options
        let localSet = local |> List.map string |> Set.ofList
        local
        |> Seq.filter (fun ip -> let bytes = ip.GetAddressBytes() in not (bytes[0] = 169uy && bytes[1] = 254uy))
        |> Seq.map (fun ip -> let bytes = ip.GetAddressBytes() in bytes[0], bytes[1], bytes[2])
        |> Seq.distinct
        |> Seq.collect (fun (a, b, c) -> seq { for d in 1..254 -> IPAddress([| a; b; c; byte d |]) })
        |> Seq.filter (fun ip -> not (Set.contains (string ip) localSet))

/// Interface recovery and announcement work are owned by one worker; callbacks are tracked until shutdown.
type internal MulticastDiscovery(options: NodeOptions, local: LocalIdentity,
                                 onAnnouncement: PeerInfo -> IPAddress -> CancellationToken -> Task,
                                 onStatus: string option -> unit, lifetime: CancellationToken) =
    let stop = CancellationTokenSource.CreateLinkedTokenSource lifetime
    let refreshGate = new SemaphoreSlim(1, 1)
    let registrations = ConcurrentDictionary<IPAddress, Task>()
    let mutable receiver: Socket option = None
    let mutable receiveStop: CancellationTokenSource option = None
    let mutable receiveTask = Task.CompletedTask
    let mutable worker = Task.CompletedTask
    let mutable joined = []
    let mutable changed = 0
    let mutable started = false
    let mutable available = false
    let reportError (error: exn) = onStatus(Some error.Message)

    let observe peer address =
        if registrations.Count < 64 then
            // Placeholder insertion is atomic, so duplicate announcements cannot launch duplicate registrations.
            if registrations.TryAdd(address, Task.CompletedTask) then
                let work = task {
                    try
                        try do! onAnnouncement peer address stop.Token
                        with
                        | :? OperationCanceledException when stop.IsCancellationRequested -> ()
                        | :? Http.HttpRequestException as error -> Diagnostics.Debug.WriteLine error.Message
                        | :? IO.InvalidDataException as error -> Diagnostics.Debug.WriteLine error.Message
                        | PeerIdentityException message -> Diagnostics.Debug.WriteLine message
                    finally registrations.TryRemove address |> ignore
                }
                registrations[address] <- work
                work.ContinueWith((fun (_: Task) -> registrations.TryRemove address |> ignore), TaskScheduler.Default) |> ignore

    let receive (socket: Socket) (ct: CancellationToken) = task {
        let buffer = Array.zeroCreate<byte> 65536
        try
            while not ct.IsCancellationRequested do
                let! received = socket.ReceiveFromAsync(Memory buffer, SocketFlags.None, IPEndPoint(IPAddress.Any, 0), ct)
                match Wire.decodePeer buffer[..received.ReceivedBytes - 1], received.RemoteEndPoint with
                | Ok peer, (:? IPEndPoint as remote) ->
                    match peer.Fingerprint with
                    | Some fingerprint when not (String.Equals(fingerprint, Fingerprint.value local.Fingerprint, StringComparison.OrdinalIgnoreCase)) ->
                        observe peer remote.Address
                    | Some _ | None -> ()
                | Error _, _ | Ok _, _ -> ()
        with
        | :? OperationCanceledException when ct.IsCancellationRequested -> ()
        | :? ObjectDisposedException when ct.IsCancellationRequested -> ()
        | :? SocketException as error ->
            available <- false
            Interlocked.Exchange(&changed, 1) |> ignore
            reportError error
    }

    let refresh (ct: CancellationToken) = task {
        do! refreshGate.WaitAsync ct
        try
            try
                let addresses = LocalNetwork.addresses options
                if addresses.IsEmpty then raise (NetworkInformationException())
                if not available || addresses <> joined then
                    let next = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                    try
                        next.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true)
                        next.ExclusiveAddressUse <- false
                        next.Bind(IPEndPoint(IPAddress.Any, options.Port))
                        let memberships = addresses |> List.filter (fun address ->
                            try
                                next.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, MulticastOption(options.MulticastAddress, address))
                                true
                            with :? SocketException -> false)
                        if memberships.IsEmpty then raise (NetworkInformationException())
                        receiveStop |> Option.iter _.Cancel()
                        receiver |> Option.iter _.Dispose()
                        do! receiveTask
                        receiveStop |> Option.iter _.Dispose()
                        let nextStop = CancellationTokenSource.CreateLinkedTokenSource stop.Token
                        receiver <- Some next
                        receiveStop <- Some nextStop
                        joined <- memberships
                        available <- true
                        receiveTask <- receive next nextStop.Token
                        onStatus None
                    with error ->
                        next.Dispose()
                        raise error
            with
            | :? SocketException as error -> available <- false; reportError error
            | :? NetworkInformationException as error -> available <- false; reportError error
        finally refreshGate.Release() |> ignore
    }

    let announce (ct: CancellationToken) = task {
        let payload = Wire.encodePeer { Wire.ofIdentity local with Announce = true }
        for address in LocalNetwork.addresses options do
            try
                use sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes())
                sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1)
                sender.Bind(IPEndPoint(address, 0))
                let! _ = sender.SendToAsync(ReadOnlyMemory payload, SocketFlags.None, IPEndPoint(options.MulticastAddress, options.Port), ct)
                ()
            with :? SocketException as error -> Diagnostics.Debug.WriteLine error.Message
    }

    let addressChanged = NetworkAddressChangedEventHandler(fun _ _ -> Interlocked.Exchange(&changed, 1) |> ignore)

    member _.Available = available

    member _.StartAsync(ct: CancellationToken) = task {
        if not started then
            do! refresh ct
            NetworkChange.NetworkAddressChanged.AddHandler addressChanged
            started <- true
            worker <- task {
                try
                    let mutable nextAnnouncement = DateTimeOffset.MinValue
                    while not stop.IsCancellationRequested do
                        if Interlocked.Exchange(&changed, 0) <> 0 || not available then do! refresh stop.Token
                        if DateTimeOffset.UtcNow >= nextAnnouncement then
                            do! announce stop.Token
                            nextAnnouncement <- DateTimeOffset.UtcNow + options.AnnouncementInterval
                        do! Task.Delay(500, stop.Token)
                with :? OperationCanceledException when stop.IsCancellationRequested -> ()
            }
    }

    member _.RefreshAsync(ct: CancellationToken) = task {
        do! refresh ct
        do! announce ct
    }

    member _.StopAsync() = task {
        if started then NetworkChange.NetworkAddressChanged.RemoveHandler addressChanged
        stop.Cancel()
        do! worker
        do! refreshGate.WaitAsync()
        try
            receiveStop |> Option.iter _.Cancel()
            receiver |> Option.iter _.Dispose()
            do! receiveTask
            do! Task.WhenAll(registrations.Values |> Seq.toArray)
            receiveStop |> Option.iter _.Dispose()
            receiver <- None
        finally refreshGate.Release() |> ignore
    }

    interface IDisposable with
        member _.Dispose() =
            stop.Dispose()
            refreshGate.Dispose()
