# Changelog

## 0.3.0-preview.1

- Replace `TransferResult` with exhaustive `SendOutcome` and `ReceiveOutcome` unions.
- Return expected PIN, rate-limit, busy, and declined responses as send outcome cases instead of public exceptions.
- Preserve `OperationCanceledException` for caller-requested cancellation while representing transfer-initiated cancellation explicitly.

## 0.2.0-preview.5

- Add `NetworkWhitelist` and `NetworkBlacklist` on `LocalSendOptions` so discovery, announcements, and HTTP subnet scans can follow LocalSend-style IPv4 interface patterns.
- Add browser link sharing: `StartWebShareAsync` serves a download page at `/`, with PIN, auto-accept, and accept/decline for each browser session.

## 0.2.0-preview.4

- Update package and repository metadata for the Tonarink repository.
- Add a dedicated Core README and ship the Core API and compatibility documentation inside the NuGet package.
- Validate the packaged documentation before publishing through NuGet Trusted Publishing.

## 0.2.0-preview.3

- Keep the HTTP server running when multicast UDP cannot bind, for example on Windows excluded-port ranges.
- Expose `DiscoveryError` and `DiscoveryTimeout` so hosts can show a degraded-discovery warning and bound HTTP probes.
- Scan local `/24` subnets over HTTP when multicast is unavailable; `RefreshAsync` retries multicast and scans as needed.

## 0.2.0-preview.2

- Recover IPv4 multicast discovery after network-interface changes and socket failures.
- Make manual refresh force discovery-socket recovery for resume scenarios.
- Publish certificate identity through a cross-process lock and temporary files; reject incomplete or corrupt identities with an actionable exception.
- Report occupied TCP ports and unavailable discovery interfaces with dedicated public exceptions.
- Allow startup retry after a corrected startup failure.
- Require XML documentation for the complete public API and enforce an approved API-surface baseline.
- Add a package-only consumer sample and cross-platform CI smoke build.
- Use source-generated protocol JSON metadata so Native AOT applications can consume the core library.

## 0.2.0-preview.1

- Add lifecycle state and persistent local identity APIs for application hosts.
- Add stale-device removal, periodic announcements, trusted manual endpoint probing and removal.
- Add explicit transfer cancellation, bounded cancellation requests and recognizable failure codes.
- Add incoming decision, session, request-body, item, byte and concurrent-upload safeguards.
- Add SHA-256 generation/verification, aggregate receive progress and source-length change detection.
- Add stream-backed items, directory enumeration, relative paths and MIME type inference.
- Reject linked-directory traversal and normalize IPv4-mapped endpoints.
- Add concurrent upload, timeout, busy receiver, checksum and cancellation integration tests.
