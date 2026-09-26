<p align="center">
  <img src="website/public/logo.svg" width="128" alt="Tonarink logo">
</p>

<h1 align="center">Tonarink</h1>

<p align="center">
  Fast, private file and text sharing for Windows 11.<br>
  Bring nearby devices naturally together—no cloud or internet connection required.
</p>

<p align="center">
  <a href="README.md">简体中文</a> ·
  <a href="README.en.md">English</a>
</p>

<p align="center">
  <a href="https://kusutori.github.io/Tonarink/">Website</a> ·
  <a href="https://github.com/kusutori/Tonarink/releases/latest">Download</a> ·
  <a href="https://github.com/kusutori/Tonarink/issues">Issues</a>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P29ZJGFWPJR"><img alt="Get it from Microsoft Store" src="https://img.shields.io/badge/Microsoft_Store-Get_the_app-0078D4?logo=microsoft&logoColor=white"></a>
  <a href="https://github.com/kusutori/Tonarink/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/kusutori/Tonarink?display_name=tag&sort=semver"></a>
  <a href="LICENSE"><img alt="Apache-2.0 license" src="https://img.shields.io/github/license/kusutori/Tonarink"></a>
  <img alt="Windows 11" src="https://img.shields.io/badge/Windows-11-0078D4?logo=windows11">
</p>

> [!IMPORTANT]
> Tonarink is an independent, unofficial implementation of the
> [LocalSend](https://localsend.org/) protocol. It interoperates with
> LocalSend-compatible devices but is not affiliated with, endorsed by, or
> distributed by the official LocalSend project.

## See it in action

| **Animated startup** | **Connected device transition** |
| :---: | :---: |
| ![Tonarink splash animation](docs/assets/readme/splash.gif) | ![Connected animation from a nearby device into the transfer overlay](docs/assets/readme/connected-animation.gif) |
| **Review an incoming transfer** | **Share directly from Windows** |
| ![Expanding an incoming transfer to review and select files](docs/assets/readme/receive-card-expand.gif) | ![Sending a file through the Windows Share interface](docs/assets/readme/windows-share.gif) |

[Watch the full Tonarink 1.0.5 demonstration](https://github.com/kusutori/Tonarink/releases/download/app-v1.0.5/Tonarink-1.0.5-demo.mp4).

## Highlights

- Discover nearby LocalSend-compatible devices automatically.
- Send and receive files, folders, clipboard content, and previewable text.
- Review incoming transfers, rename files, change the destination, or accept
  only selected items.
- Verify devices visually or with text, protect transfers with a PIN, and use
  HTTPS on the local network.
- Share through direct discovery, a saved address, favorites, or a browser link.
- Integrate with Windows Share, File Explorer context menus, notifications,
  taskbar progress, the system tray, and Windows 11 materials.
- Switch between light and dark themes and change language at runtime.
- Choose a signed MSIX package or a portable Native AOT build for x64 and ARM64.

## Download

For everyday use, install Tonarink directly from the
[Microsoft Store](https://apps.microsoft.com/detail/9P29ZJGFWPJR) to receive the appropriate
architecture and future updates automatically. The Store build is self-contained
Native AOT and does not require a separate .NET Desktop Runtime installation.

Standalone packages remain available from
[GitHub Releases](https://github.com/kusutori/Tonarink/releases/latest). Choose an MSIX build when
possible because Windows Share and File Explorer integration require packaged app identity.

| Build | Recommended for |
| --- | --- |
| Native AOT MSIX | The recommended installed build when it runs smoothly on your device. |
| Standard MSIX | The stable fallback if the Native AOT build feels less responsive. |
| Portable | Running without installation; packaged Windows integrations are unavailable. |
| Widgets | Experimental testing only; the widget remains under development. |

Choose `x64` for most PCs and `ARM64` for Windows on Arm. Extract the downloaded
MSIX ZIP and run `Install.ps1`; see the
[installation guide](docs/development-msix-install.md) for details.

## LocalSendDotNet.Core

Tonarink is powered by
[LocalSendDotNet.Core](src/LocalSendDotNet.Core/README.md), a UI-independent
.NET 11 implementation of the LocalSend v2.2 protocol maintained in this
repository and also available as a NuGet package.

The library keeps the `LocalSendDotNet.Core` package name and public API so it
can be consumed independently by other .NET applications. Its usage guide,
compatibility notes, interoperability matrix, and publishing documentation live
under [`src/LocalSendDotNet.Core`](src/LocalSendDotNet.Core).

## Build from source

The production Windows application is built with .NET 11, Windows App SDK, and
Microsoft UI Reactor.

```powershell
dotnet restore Tonarink.slnx
dotnet build Tonarink.slnx
dotnet test Tonarink.slnx
```

The Windows client is located in `src/Tonarink.App`. Experimental Blazor Web
and Hybrid projects remain in the repository, but they are not part of the
current production release. Packaging and release details are documented in
[docs/packaging.md](docs/packaging.md) and
[docs/app-release-ci.md](docs/app-release-ci.md).

## Contributing and localization

Bug reports, feature suggestions, code contributions, and translations are
welcome. Open an [issue](https://github.com/kusutori/Tonarink/issues) for product
feedback. English source resources and community translations are synchronized
through Crowdin by CI.

Before contributing, review the [security policy](SECURITY.md),
[privacy policy](PRIVACY.md), and [third-party notices](NOTICE).

## Relationship to LocalSend

LocalSend established the open protocol and ecosystem that make cross-client
interoperability possible. We thank the LocalSend project and its contributors
for that work. Compatibility does not imply official status or endorsement;
see [NOTICE](NOTICE) for attribution and further details.

## License

Tonarink and LocalSendDotNet.Core are released under the
[Apache License 2.0](LICENSE).
