# Blazor Web and Hybrid architecture

Tonarink keeps the released Windows 11 Reactor app while developing a shared
Blazor user interface for Web and .NET MAUI Blazor Hybrid hosts.

## Development status (paused 2026-08-30)

Blazor Web and MAUI Hybrid are an experimental next development phase. Their
source and normal CI build/test coverage remain in the repository, but current
`app-v*` GitHub Releases intentionally publish only the production Windows
Reactor application. The prepared Web and signed Android jobs in
`.github/workflows/release-app.yml` are statically disabled and are not release
dependencies.

Implemented so far:

- a shared responsive receive/send/incoming/history/settings UI with runtime
  Chinese/English and light/dark/system appearance;
- a real LocalSend node in the Blazor Server host, protected by loopback-only
  access unless an explicit Web password is configured;
- PWA metadata, browser file streams, clipboard access, notifications, PIN
  retry, partial acceptance, progress, cancellation, and persisted settings;
- a portable TLS/HTTP 1.1 Core server used by Android and iOS instead of
  Kestrel;
- Android multicast permission/lock, foreground receiving service, native
  share target, notifications, and MediaStore Downloads publishing;
- iOS local-network declarations, App Group integration, Files-visible
  downloads, notifications, and a native Share Extension for files/text/URLs.

Verified before pausing:

- 63 desktop Core tests, 4 portable-server tests, and 2 application-state tests;
- responsive Web rendering, language/theme switching, authentication boundary,
  and a Linux self-contained Web publish;
- Android Debug/Release AOT builds plus throwaway-key signed APK and AAB
  packaging;
- iOS simulator compilation including the Share Extension, and Mac Catalyst
  compilation.

Still required before treating these as supported products:

- real Android-device interoperability, system-share, background survival, and
  MediaStore verification;
- signed physical-iPhone interoperability and Share Extension verification with
  matching App Group profiles and Apple's multicast entitlement;
- production Android/Apple signing identities and distribution policy;
- a deliberate lifetime policy for temporary files imported from native share
  targets, so files are deleted only after the send queue releases them;
- re-enable and review the Web/Android release jobs, release notes, and version
  synchronization when this phase resumes.

## Projects

| Project | Responsibility |
| --- | --- |
| `Tonarink.Application` | Platform-neutral state, settings, transfer models, runtime and platform contracts |
| `Tonarink.Blazor.Shared` | Responsive Razor shell and receive, send, incoming, history, and settings pages |
| `Tonarink.LocalSend` | Desktop/Web adapter between the application contracts and `LocalSendDotNet.Core` |
| `LocalSendDotNet.Core.Mobile` | Mobile build of Core using the portable HTTP/1.1 server transport |
| `Tonarink.LocalSend.Mobile` | Android/iOS adapter compiled against the mobile Core build |
| `Tonarink.Web` | ASP.NET Core host that runs a LocalSend node and exposes the shared UI |
| `Tonarink.Hybrid` | .NET MAUI Blazor Hybrid host for Android, iOS, and Mac Catalyst |
| `Tonarink.Hybrid.ShareExtension` | iOS Share Extension that hands files and text to the main app through an App Group |

The existing `Tonarink.App` remains the production Windows application. The
shared UI does not force a rewrite of that app and can evolve independently.

## Web capability boundary

A browser sandbox cannot bind the LocalSend UDP multicast socket or accept
inbound LocalSend HTTPS connections. `Tonarink.Web` therefore runs the node in
the ASP.NET Core host process. The browser controls that host node through a
Blazor Server circuit. The Web host is restricted to loopback when no password
is configured. Set `Tonarink:WebPassword` or the `TONARINK_WEB_PASSWORD`
environment variable before binding it to a LAN address. The control surface
then uses HTTP Basic authentication; deploy behind HTTPS because Basic
credentials are encoded but not encrypted.

Browser file selection uses `InputFile` and streams selected content without
requiring a local filesystem path. Text items are retained as UTF-8 content in
the shared application state. Clipboard operations use the browser Clipboard
API and require a secure context. A Web App Manifest and service worker make
the UI installable, but transfers require a live Blazor Server connection and
are not available offline.

## Mobile transport boundary

The NuGet `LocalSendDotNet.Core` build continues to use its proven Kestrel
transport. Android and iOS do not provide an ASP.NET Core shared-framework
runtime pack, so directly referencing that build fails with `NETSDK1082`.

`LocalSendDotNet.Core.Mobile` links the protocol, discovery, security, transfer,
and storage implementation while replacing only Kestrel with a streaming
socket-based HTTP/1.1 and TLS server. It supports fixed-length and chunked
requests, optional browser client certificates, mutual certificate validation,
PIN handling, uploads, cancellation, and browser downloads. Desktop process
tests exercise mutual-TLS transfer and the certificate-free browser info route.

The Hybrid host starts this full node. Android acquires a Wi-Fi multicast lock,
requests Nearby Devices access on Android 13 or later, and uses a
`connectedDevice` foreground service for background discovery and receiving.
Received files are published to `Downloads/Tonarink` through MediaStore on
Android 10+ without broad storage access. Android 9 and earlier use their legacy
scoped write permission.

iOS includes a local-network usage description and the multicast entitlement;
physical-device distribution requires Apple to grant that restricted
entitlement to the app's provisioning profile. iOS suspends arbitrary listening
sockets after an app enters the background, so full discovery and receiving are
foreground-only by platform design. Received files are stored in Documents and
visible in the Files app. The Share Extension accepts files, images, movies,
URLs, and text through App Group `group.dev.tonarink.app`; both targets require
matching App Group provisioning profiles.

Both mobile targets support file selection, clipboard text, LocalSend PIN
retry, partial acceptance, cancellation, notifications, persisted settings,
runtime Chinese/English switching, and explicit light/dark/system appearance.

## Local build

Install the MAUI workload once:

```powershell
dotnet workload install maui
```

Build the Web host and Android Hybrid target:

```powershell
dotnet build src/Tonarink.Web/Tonarink.Web.csproj
dotnet build src/Tonarink.Hybrid/Tonarink.Hybrid.csproj -f net10.0-android
```

Run the Web host:

```powershell
dotnet run --project src/Tonarink.Web/Tonarink.Web.csproj
```

To expose the Web host to trusted devices on the LAN:

```powershell
$env:TONARINK_WEB_PASSWORD = "choose-a-long-password"
dotnet run --project src/Tonarink.Web/Tonarink.Web.csproj --urls https://0.0.0.0:7180
```

The development certificate is not suitable for other devices. Configure a
certificate trusted by those devices for a non-loopback HTTPS endpoint.

iOS and Mac Catalyst deployment still require a paired or local Mac with the
matching Xcode toolchain.

## Future release artifacts

The paused release recipe can produce self-contained Web hosts for Windows,
Linux, and macOS, plus a signed Android APK and AAB. It remains disabled until
the real-device gates above pass. iOS distribution must use project-owned Apple
certificates, provisioning profiles, App Group capability, and an approved
multicast entitlement; the repository does not publish a development-only IPA.
