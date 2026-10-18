module internal LocalSendDotNet.FSharp.Examples

// Optional compile-only examples. Neither function is invoked by the library.

open System
open System.Net
open System.Threading
open System.Threading.Tasks
open LocalSendDotNet.FSharp

let receive alias identityDirectory downloadDirectory (cancellationToken: CancellationToken) = task {
    let options = NodeOptions.defaults alias identityDirectory downloadDirectory
    match Node.create options with
    | Error errors ->
        for error in errors do printfn "%s: %s" error.Field error.Message
    | Ok node ->
        use node = node
        do! node.StartAsync cancellationToken
        use requests = node.WatchIncomingTransfers().GetAsyncEnumerator(cancellationToken)
        let mutable reading = true
        while reading do
            let! hasNext = requests.MoveNextAsync()
            if hasNext then
                let request = requests.Current
                let! outcome = node.AcceptAsync(request.RequestId, ReceiveOptions.Default, cancellationToken)
                printfn "%A" outcome
            else reading <- false
}

let sendText (node: LocalSendNode) (address: string) port message (approve: Device -> Task<bool>) (cancellationToken: CancellationToken) = task {
    match DeviceEndpoint.create (IPAddress.Parse address) port Protocol.Https with
    | Error error -> return Error error
    | Ok endpoint ->
        let! probe = node.ProbeAsync(endpoint, cancellationToken)
        if not probe.IdentityVerified then return Error "The probe did not verify a TLS identity."
        else
            // The host shows/compares the fingerprint before approving a first-time peer.
            let! trusted = approve probe.Device
            if not trusted then return Error "The host did not trust this peer."
            else
                let! outcome = node.SendAsync(probe.Device, [SendItems.text message], SendOptions.Default, cancellationToken)
                return Ok outcome
}
