# Core API guide

`LocalSendNode` is the version-independent application boundary. Protocol v2 DTOs and routes remain internal so a future v3 adapter can preserve this calling model.

## Lifecycle and identity

Create one node for the lifetime of the host application. `StartAsync` loads or creates the persistent certificate identity and starts the HTTP server. Multicast discovery is started when the UDP port can be bound; a multicast failure leaves the node `Running` so transfers still work, and `DiscoveryError` explains why announcements are unavailable. Nearby devices are then found with an HTTP `/24` subnet scan. A stopped node is intentionally not restartable; dispose it and create a new instance. `State`, `Identity`, and `WatchStateChangesAsync` are suitable for application status indicators.

Startup failures enter `Faulted` and may be retried on the same instance after correcting the cause. `PortUnavailableException` and `IdentityLoadException` remain fatal. `DiscoveryUnavailableException` is no longer fatal during `StartAsync`. A caller-cancelled startup returns to `Created`.

The library never writes to the console and does not switch synchronization contexts. Supply an `ILoggerFactory` for diagnostics. Callers decide how `IProgress<TransferProgress>` callbacks are marshalled to their UI thread.

## Devices

Use `GetDevices()` for an initial snapshot and `WatchDeviceChangesAsync()` for additions, updates, and removals. Devices expire after `DeviceExpiration` when no new announcement or registration is seen. HTTPS is preferred by `LocalSendDevice.PreferredEndpoint`.

Discovery listens for operating-system address changes and rebinds its IPv4 multicast receiver after a short debounce. Periodic maintenance retries interfaces that could not join. `RefreshAsync` retries multicast, announces when it is available, and always scans local `/24` subnets over HTTP using `DiscoveryTimeout`. `NetworkWhitelist` and `NetworkBlacklist` on `LocalSendOptions` filter those discovery addresses with dotted-quad patterns (`*` matches one octet). The HTTP server still listens on every interface. Restart the node after changing the lists.

`StartWebShareAsync` serves a browser download page at the node root (`/`). Browsers call `POST /api/localsend/v2/prepare-download` (optional `pin` query) and then `GET /api/localsend/v2/download`. Pending browser sessions appear in `WatchWebShareAsync` until `AcceptWebShareRequest` or `DeclineWebShareRequest`. `WebShareOptions.AutoAccept` skips that confirmation.

`StartWebReceiveAsync` serves the reverse browser flow. The page posts file metadata to `POST /api/localsend/v2/prepare-web-upload`, which produces the same `IncomingTransferRequest` used by LocalSend peers, and streams accepted files through the regular upload route. PIN, partial acceptance, automatic acceptance, destination safety, progress, and cancellation therefore follow the normal receive pipeline.

For a manually entered address, call `ProbeDeviceAsync(endpoint)` first. HTTPS probes validate that the certificate is current, self-signed consistently, and agrees with the fingerprint returned by `/info`; the returned fingerprint is still trust-on-first-use and should be shown for user confirmation. After confirmation, call `AddKnownDeviceAsync(endpoint, fingerprint)`. Manually trusted devices remain in the in-memory list until `RemoveDevice` or node disposal instead of expiring with multicast peers. HTTP probes cannot cryptographically verify identity and return `IdentityVerified == false`.

## Sending

Use `SendFileItem`, `SendTextItem`, or `SendStreamItem`. The stream factory makes sandboxed file pickers and virtual content usable without an intermediate file. `LocalSendItems.FromDirectory` builds items with protocol-safe relative names. `SendOptions.ComputeSha256` performs a complete pre-read and includes the digest in prepare-upload; enable it when integrity is more important than avoiding a second read.

`SendAsync` reports a transfer ID in its first progress callback. Retain that ID to call `CancelTransferAsync`. Cancellation also attempts the remote `/cancel` route with a short bounded timeout. PIN-required and PIN-rate-limited responses use dedicated exceptions; normal transport failures are returned as `TransferResult` with a code from `TransferFailureCodes`.

## Receiving

`WatchIncomingTransfersAsync` is a reliable bounded stream: when a subscriber is slow, producers wait instead of silently dropping a request requiring a decision. Call `AcceptAsync` with item IDs for partial acceptance or `DeclineAsync`. Unknown IDs are rejected before a decision is sent.

Accepted files are streamed to `.part-*`, length-checked, optionally SHA-256 checked, and atomically renamed. Absolute paths, traversal, linked subdirectories, and platform-invalid names are rejected. Abandoned accepted sessions fail after `IncomingTransferTimeout`, release their concurrency slot, and remove temporary files. When all slots are occupied, new sessions receive an immediate busy response instead of waiting indefinitely.

## Limits

Important host-controlled limits include `MaxConcurrentTransfers`, `MaxConcurrentFileUploads`, `MaxIncomingItemsPerTransfer`, `MaxIncomingTransferBytes`, `MaxPrepareRequestBytes`, `IncomingDecisionTimeout`, `IncomingTransferTimeout`, `UploadTimeout`, and `CancelRequestTimeout`. Defaults favor normal LAN transfers; applications accepting files from less trusted networks should choose tighter byte and item limits.

## API compatibility

Every public member must have XML documentation. `tests/LocalSendDotNet.Core.Tests/PublicApiBaseline.txt` stores the approved SHA-256 of a canonical public surface generated by `tools/LocalSendDotNet.ApiSurface`. Run the tool without arguments to review the full surface and with `--hash` after intentionally approving a preview API change. Unintentional additions, removals, parameter changes, return-type changes, enum changes, or default-value changes fail the test suite.
