# LocalSendDotNet.Core.FSharp

An independent, experimental F# implementation of the LocalSend v2.2 Core,
included in `Tonarink.slnx`. It does not reference the C# Core. Production App,
CLI and other hosts continue to use their existing C# implementation.

## Design

- Discriminated unions model send sources, selections, decisions, lifecycle,
  negotiation and terminal outcomes. Outcomes retain completed items on failure
  or cancellation.
- Private single-case unions distinguish file IDs, sessions, upload tokens and
  fingerprints. `Option` represents optional domain values; null is confined to
  .NET interop boundaries.
- `Result` and FsToolkit.ErrorHandling's `result` / `taskResult` computation
  expressions compose validation and asynchronous preparation. Configuration
  validation accumulates independent errors.
- A `MailboxProcessor` owns the immutable device map and persistent-peer set.
  Receiving sessions use small synchronized state transitions; parallel body I/O
  never executes inside a metadata mailbox or lock.
- Explicit `System.Text.Json` readers/writers preserve protocol field names and
  avoid reflection serialization of F# unions.
- `task` integrates directly with ASP.NET Core, HttpClient and stream APIs.
  `use`/`use!` and `IAsyncDisposable` express resource ownership. This project
  deliberately does not introduce a generic effect interpreter or custom monad
  builder.

## Coverage and source map

| Responsibility | Implementation |
| --- | --- |
| Domain, opaque IDs, selected acceptance, structured outcomes | `Domain.fs` |
| Defaults, limits, accumulated configuration errors | `Configuration.fs` |
| File/text/repeatable stream sources, MIME lookup, directory enumeration | `Sources.fs` |
| LocalSend v2.2 registration, offers, tokens, download JSON | `Protocol.fs` |
| Persistent RSA identity, locked PEM creation, self-signed peer validation | `Identity.fs` |
| Safe relative paths, unique destinations, temporary writes, SHA-256, timestamps | `Storage.fs` |
| Bounded broadcast streams, device mailbox, serialized PIN rate limiting | `Infrastructure.fs` |
| HTTPS fingerprint pinning, probing, preparation, streaming uploads and cancellation | `HttpTransport.fs` |
| Pending decisions, partial acceptance, single-use tokens, parallel uploads, expiry | `ReceiveSessions.fs` |
| Multicast, network-interface recovery, wildcard interface filters, scan targets | `Discovery.fs` |
| Browser send/receive, host authorization, PIN, reset/expiry | `WebShare.fs` |
| Browser upload/download UI in English, Chinese, Japanese, Korean, French, Spanish | `WebSharePage.fs` |
| Embedded HTTP/HTTPS server and bounded request decoding | `Server.fs` |
| Startup/retry/shutdown, discovery fallback, trusted peers and public API | `Node.fs` |

Single text offers containing a preview complete after host acceptance without
silently writing a file. Hosts inspect `IncomingRequest.Items[*].Preview` to show
the text. Other selected items are streamed to safe destinations. The browser
upload path uses the same acceptance and storage implementation.

## Usage

```fsharp
open System.Threading
open LocalSendDotNet.FSharp

let options = NodeOptions.defaults "F# peer" identityDirectory downloadDirectory

let run cancellationToken = task {
    match Node.create options with
    | Error errors ->
        for error in errors do
            printfn "%s: %s" error.Field error.Message
    | Ok node ->
        use node = node
        do! node.StartAsync cancellationToken
        // Subscribe before expecting incoming requests. Dispose the enumerator
        // to release its bounded reliable queue when the consumer stops.
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
```

`AcceptAsync` above intentionally waits for completion before handling the next
offer. A concurrent host should dispatch and observe acceptance tasks separately.
Watch streams publish events after subscription; use the device, pending-offer
and WebShare snapshots for initial/current state. Progress queues drop old
updates under load; the incoming-offer stream applies backpressure instead.

Other entry points include `SendAsync`, `ProbeAsync`, `AddKnownDeviceAsync`,
`RefreshAsync`, `CancelTransferAsync`, `StartWebShare`, `StartWebReceive` and
`AcceptWebShareRequest`. Tokens/IDs received in events can be passed back without
converting them to strings. HTTP probe results are explicitly unverified; probing
does not persist trust. `AddKnownDeviceAsync` requires a caller-supplied trusted
fingerprint.

## Build and verification boundary

```powershell
dotnet build src/LocalSendDotNet.Core.FSharp/LocalSendDotNet.Core.FSharp.fsproj -c Release
dotnet build src/LocalSendDotNet.Core.FSharp/LocalSendDotNet.Core.FSharp.fsproj -c Release -p:CompileExamples=true
```

Targets .NET 11 and the ASP.NET Core shared framework, like the existing Core.
`CompileExamples=true` also compiles the example functions without invoking them
or starting a node; examples are excluded from ordinary library builds.
No Windows-specific framework or production project reference is introduced.
This initial implementation is compile-verified; no production App integration,
live peer/browser interoperability run, Native AOT publish or cross-OS runtime
verification is claimed. It is an experiment, not a drop-in replacement or a
published NuGet package. Those verification boundaries should remain explicit
when evaluating it against the C# implementation.
