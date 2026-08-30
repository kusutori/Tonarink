# Tonarink

Tonarink is an independent app for fast, private file and text transfer over a
local network. The released Windows 11 client uses Reactor; Web, Android, and
iOS clients are being built on a shared Blazor UI. Tonarink is an unofficial
implementation of the LocalSend protocol and interoperates with
LocalSend-compatible devices.

Tonarink is not affiliated with, endorsed by, or distributed by the official
LocalSend project. The product name and user experience are independent; protocol
compatibility is provided so users can communicate with the existing LocalSend
ecosystem.

## Highlights

- Discover nearby LocalSend-compatible devices automatically
- Send and receive files, folders, text, and clipboard content
- Review incoming transfers and accept selected items
- Follow transfer progress in the app and on the Windows taskbar
- Use Windows Share to send files directly from File Explorer
- Run from the system tray and choose Chinese or English at runtime
- Use light, dark, or system appearance with Windows 11 materials
- Install with MSIX or run the Native AOT portable build
- Run a password-protected, installable Web control surface for a host node
- Build full Android and iOS nodes with native file pickers, share targets, and notifications

## Download

Prebuilt releases are available from [GitHub Releases](https://github.com/kusutori/Tonarink/releases).
The MSIX sideload package and portable build are described in
[the installation guide](docs/development-msix-install.md).

## LocalSendDotNet.Core

Tonarink is powered by
[LocalSendDotNet.Core](src/LocalSendDotNet.Core/README.md), the UI-independent
.NET 10 implementation of the LocalSend v2.2 protocol maintained in this
repository. It can also be consumed separately from NuGet by other .NET apps.

The protocol library retains the `LocalSendDotNet.Core` package name and public
API. Its usage guide, compatibility notes, interoperability matrix, and NuGet
publishing documentation live with the project under
[`src/LocalSendDotNet.Core`](src/LocalSendDotNet.Core).

## Build from source

```powershell
dotnet restore LocalSendDotNet.slnx
dotnet build LocalSendDotNet.slnx
dotnet test LocalSendDotNet.slnx
```

The released Windows app project is `src/Tonarink.App/Tonarink.App.csproj`.
The cross-platform shared Blazor UI is under `src/Tonarink.Blazor.Shared`, with
`src/Tonarink.Web` and `src/Tonarink.Hybrid` as its Web and .NET MAUI hosts.
Their architecture and current platform boundary are documented in
[docs/blazor-hybrid.md](docs/blazor-hybrid.md). Packaging and release details
for the Windows app are documented in [docs/packaging.md](docs/packaging.md) and
[docs/app-release-ci.md](docs/app-release-ci.md).

## Relationship to LocalSend

LocalSend established the open protocol and ecosystem that make Tonarink's
cross-client interoperability possible. We thank the LocalSend project and its
contributors for that work. Compatibility does not imply official status or
endorsement; see [NOTICE](NOTICE) for attribution and further details.

## License

Tonarink and LocalSendDotNet.Core are licensed under the
[Apache License 2.0](LICENSE).
