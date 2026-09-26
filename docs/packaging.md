# Windows publishing and packaging

The Reactor app keeps its normal Debug and Release builds unpackaged. Native AOT
and MSIX are explicit release shapes so packaging does not slow down the inner
development loop.

## Native AOT

Publish the x64 build with the checked-in profile:

```powershell
dotnet publish src/Tonarink.App/Tonarink.App.csproj `
  -p:PublishProfile=win-x64-aot
```

For Windows on ARM64, use `win-arm64-aot` instead. Outputs are written under
`artifacts/publish/native-aot/<rid>/`.

The equivalent one-off command is:

```powershell
dotnet publish src/Tonarink.App/Tonarink.App.csproj -c Release `
  -r win-x64 -p:Platform=x64 -p:NativeAot=true `
  -o artifacts/publish/native-aot/win-x64
```

Native AOT keeps `InvariantGlobalization` off. `LocaleProvider` constructs a
`CultureInfo` for `zh-CN` / `en-US` when loading Resw strings; invariant mode
throws `CultureNotFoundException` as soon as the shell mounts. Language
*detection* still uses the WinRT `GlobalizationPreferences` API rather than
`CultureInfo.CurrentUICulture`. Reactor DevTools remains Debug-only and is
therefore absent from the trimmed retail binary.

## MSIX

The application uses single-project MSIX packaging, following ReactorGallery. The
default build still has `WindowsPackageType=None`; opt into packaging with:

```powershell
dotnet build src/Tonarink.App/Tonarink.App.csproj -c Release `
  -p:Platform=x64 `
  -p:TonarinkPackaged=true `
  -p:GenerateAppxPackageOnBuild=true
```

This produces an unsigned package under the app project's `AppPackages` directory.
It is suitable for validating the package layout, but Windows will not install it
until it is signed.

The default package does **not** include Windows 11 widgets. To build the larger
widgets flavor (same package identity, includes `Tonarink.WidgetProvider`):

```powershell
dotnet build src/Tonarink.App/Tonarink.App.csproj -c Release `
  -p:Platform=x64 `
  -p:TonarinkPackaged=true `
  -p:TonarinkWidgets=true `
  -p:GenerateAppxPackageOnBuild=true
```

For a sideloadable build, set `Package.appxmanifest`'s `Identity Publisher` to the
exact subject of the signing certificate and add:

```powershell
-p:AppxPackageSigningEnabled=true `
-p:PackageCertificateKeyFile=C:\path\LocalSendDotNet.pfx
```

Keep the PFX and its password outside the repository. The manifest declares both
`internetClientServer` and `privateNetworkClientServer`, which the LocalSend HTTP
server and local-network discovery require, plus `runFullTrust` for the desktop app.

For local sideload testing, generate a compatible code-signing certificate with:

```powershell
.\tools\New-MsixSigningCertificate.ps1
```

The generated certificate contains the Basic Constraints extension required by
Visual Studio's `Add-AppDevPackage.ps1`, with `CA=false`, plus the Code Signing EKU
and Digital Signature key usage. Copy the generated `.cer` beside the `.msix` only
when using the generated installation script. The package must be signed with the
matching private key from `Cert:\CurrentUser\My` (or an exported PFX).

## Native AOT inside MSIX

Native AOT and MSIX are independent choices. Do **not** pass
`TonarinkPackaged=true` together with `NativeAot=true` on the same
`dotnet publish`: the MSIX targets then package the managed apphost (~600 KB)
instead of the native executable (~26 MB), and the resulting app white-screens
and crashes.

Publish the unpackaged Native AOT layout first, copy the stamped
`Package.appxmanifest` in as `AppxManifest.xml`, set its processor architecture
and replace the generated `$targetentrypoint$` placeholder with
`Windows.FullTrustApplication`, copy PNG/ICO assets, then build `resources.pri`
with `tools/New-AotMsixResourcesPri.ps1` before packing. The unpackaged AOT
publish only emits `Tonarink.pri`; Windows Shell reads package logos from
`resources.pri`. Without it the taskbar falls back to a plated
`Square44x44Logo`. That PRI must be a full merged resource map (app + WinUI +
qualified assets), named after the package identity. A PNG-only
`resources.pri` becomes the package primary map and WinUI fail-fasts at
startup (`Microsoft.UI.Xaml.dll`, `0xc000027b`) before any window appears.
Windows 11 widgets are **not** in the default package. Opt in with
`-p:TonarinkWidgets=true` on a packaged or Native AOT publish; that injects
`Package.Widgets.extensions.xml` into the manifest and copies a Native AOT
`Tonarink.WidgetProvider.exe` (~3 MB) plus templates and icons. The host
shares the package's Windows App SDK runtime instead of bundling a second
copy.
`winapp package` 0.6.1 packs this prepared layout. A local x64 comparison found
that its payload and `resources.pri` matched `makeappx` byte for byte; the
generated manifest only gained a WinAppCli build marker. Use `--skip-pri` to
retain the merged PRI created above. The CLI does not replace the publish,
widget-payload, manifest, asset, or PRI preparation steps. GitHub Release CI
uses WinApp CLI for managed and Native AOT MSIX packages, then signs them with
the Windows SDK's `signtool`. Store CI publishes self-contained Native AOT x64
and ARM64 layouts, uses WinApp CLI for the unsigned MSIX packages, and combines
them into the submission bundle.

```powershell
dotnet publish src/Tonarink.App/Tonarink.App.csproj -c Release `
  -r win-x64 -p:Platform=x64 -p:NativeAot=true `
  -p:TonarinkPackaged=false -p:WindowsPackageType=None `
  -o artifacts/publish/native-aot/win-x64

Copy-Item src/Tonarink.App/Package.appxmanifest `
  artifacts/publish/native-aot/win-x64/AppxManifest.xml
Copy-Item src/Tonarink.App/Assets/*.png, src/Tonarink.App/Assets/*.ico `
  artifacts/publish/native-aot/win-x64/Assets
# Widgets flavor: add -p:TonarinkWidgets=true to publish, then
# ./tools/Add-WidgetManifestExtensions.ps1 -Source src/Tonarink.App/Package.appxmanifest `
#   -Destination artifacts/publish/native-aot/win-x64/AppxManifest.xml
./tools/New-AotMsixResourcesPri.ps1 `
  -LayoutPath artifacts/publish/native-aot/win-x64

winapp package artifacts/publish/native-aot/win-x64 --skip-pri `
  --output artifacts/Tonarink-win-x64-aot.msix
```

A missing `mspdbcmf.exe` only prevents generation of the optional symbol package; it
does not prevent the `.msix` application package from being created.

## GitHub Releases

Pushing a numeric `app-vMAJOR.MINOR.PATCH` tag builds x64 and ARM64 Native AOT
portable archives, signed managed MSIX sideload ZIPs, and signed Native AOT MSIX
sideload ZIPs, then attaches them to the GitHub Release for that tag. Managed
MSIX ZIPs include the repository's sideload installer. The separate tag prefix avoids
triggering Core NuGet publication. The private key is supplied only through
GitHub Actions Secrets. See [app-release-ci.md](app-release-ci.md) for setup and
release instructions.
