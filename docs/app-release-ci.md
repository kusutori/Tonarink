# Application GitHub Release CI

The `Tonarink GitHub Release` workflow creates these Windows assets for x64 and ARM64:

- a self-contained Native AOT portable ZIP;
- a separate native-symbols ZIP;
- a signed managed MSIX sideload ZIP (`Tonarink-<version>-<platform>-msix.zip`);
- a signed managed Widgets MSIX sideload ZIP (`Tonarink-<version>-<platform>-widgets-msix.zip`);
- a signed Native AOT MSIX sideload ZIP (`Tonarink-<version>-<platform>-aot-msix.zip`);
- a signed Native AOT Widgets MSIX sideload ZIP (`Tonarink-<version>-<platform>-aot-widgets-msix.zip`).

Both managed and Native AOT MSIX ZIPs contain a signed `.msix`, public
certificate, and the repository's `Install-MsixSideload.ps1` as `Install.ps1`.
WinApp CLI 0.6.1 packs the prepared layouts; the Windows SDK's `signtool`
signs them without passing the PFX password on a command line.

Each generated GitHub Release begins with a bilingual Markdown table explaining
the application packages. Standard managed MSIX is recommended for most users;
the Widgets ZIPs are optional builds that register a Windows 11 widget
provider (Native AOT COM host, a few extra MB). They exist for both the
managed and Native AOT MSIX flavors. Portable ZIPs remain unpackaged and
do not include widgets.
Symbol ZIPs are identified as debugging-only downloads.

The workflow runs for tags in `app-vMAJOR.MINOR.PATCH` or
`app-vMAJOR.MINOR.PATCH.REVISION` form and can also be started manually. The `app-`
prefix keeps application releases separate from the `v*` tags used to publish the
Core NuGet package. GitHub Releases are marked as prereleases while the application
is experimental.

## Required repository secrets

Create the following Actions secrets in the GitHub repository:

| Secret | Value |
| --- | --- |
| `MSIX_CERTIFICATE_BASE64` | Base64 encoding of the PFX containing the self-signed certificate and private key |
| `MSIX_CERTIFICATE_PASSWORD` | Password used when the PFX was exported |

Encode the PFX and copy the result without printing it to the terminal:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\secure\LocalSendDotNet.pfx")) |
    Set-Clipboard
```

The certificate subject must exactly match the `Publisher` in
`src/Tonarink.App/Package.appxmanifest`. Never commit a PFX, its password, or
its Base64 representation. The workflow imports it into the temporary runner's
Current User certificate store, removes the temporary PFX immediately, and removes
the imported certificate after packaging.

## Publishing

Commit the feature work first so the working tree is clean, then from the
repository root:

```powershell
./tools/Publish-Release.ps1
```

That bumps the app patch version in `Tonarink.App.csproj` and
`Package.appxmanifest`, commits `Ship <version>`, and pushes `app-vMAJOR.MINOR.PATCH`.
Use `-Bump Minor` or `-Version 0.2.0` to choose the next number, `-DryRun` to
preview, and `-NoPush` to stop after the local commit and tag.

Manual equivalent after the version files already match:

```powershell
git tag -a app-v0.1.0 -m "Application release app-v0.1.0"
git push origin app-v0.1.0
```

The tag is converted to the four-part MSIX version (`app-v0.1.0` becomes
`0.1.0.0`). Prerelease suffixes such as `app-v0.1.0-preview.1` are intentionally
rejected because MSIX versions contain four numeric components.

The private key never becomes a release artifact.
