# LocalSendDotNet.Core

`LocalSendDotNet.Core` is a UI-independent .NET 11 implementation of the
LocalSend v2.2 protocol. It provides discovery, secure transfer, and receiving
APIs for desktop apps, services, command-line tools, and other .NET hosts.

The library is maintained as part of the
[Tonarink repository](https://github.com/kusutori/Tonarink). It is an unofficial
implementation and is not affiliated with or endorsed by the official LocalSend
project.

## Install

```powershell
dotnet add package LocalSendDotNet.Core --prerelease
```

The library targets .NET 11 and uses the ASP.NET Core shared framework for its
embedded HTTP server.

## Features

- IPv4 multicast discovery and two-way registration
- HTTP or HTTPS with a persisted RSA identity, mutual TLS, and certificate
  fingerprint pinning
- Streaming file and text send/receive without buffering complete files
- Partial acceptance, progress reporting, cancellation, and optional receiver PIN
- Safe writes with path traversal protection, temporary files, and atomic publish
- Bounded transfer concurrency, session timeouts, and SHA-256 verification
- Device lifecycle events, trusted manual endpoints, and stream-backed send items
- Folder enumeration with relative paths and common MIME type inference

## Quick start

```csharp
using LocalSendDotNet;

await using var node = new LocalSendNode(new LocalSendOptions
{
    Alias = "My app",
    DataDirectory = appDataDirectory,
    DownloadDirectory = downloadsDirectory
});

await node.StartAsync(cancellationToken);

await foreach (var request in node.WatchIncomingTransfersAsync(cancellationToken))
{
    _ = node.AcceptAsync(
        request.RequestId,
        progress: progress,
        cancellationToken: cancellationToken);
}
```

The sibling `LocalSendDotNet.Cli` project provides discovery, send, receive, and
diagnostic commands for development and interoperability testing.

## Documentation

- [Core API guide](https://github.com/kusutori/Tonarink/blob/main/src/LocalSendDotNet.Core/docs/core-api.md)
- [Protocol compatibility](https://github.com/kusutori/Tonarink/blob/main/src/LocalSendDotNet.Core/docs/compatibility.md)
- [Interoperability matrix](https://github.com/kusutori/Tonarink/blob/main/src/LocalSendDotNet.Core/docs/interop-matrix.md)
- [NuGet publishing guide](https://github.com/kusutori/Tonarink/blob/main/src/LocalSendDotNet.Core/docs/nuget-publishing.md)

The public API is intentionally version-independent. Protocol-specific DTOs,
routes, and serialization remain internal so a future protocol adapter can be
added without changing the `LocalSendNode` calling model.

## License and attribution

`LocalSendDotNet.Core` is licensed under the
[Apache License 2.0](https://github.com/kusutori/Tonarink/blob/main/LICENSE).
Attribution and the relationship to the official LocalSend project are recorded
in [NOTICE](https://github.com/kusutori/Tonarink/blob/main/NOTICE).
