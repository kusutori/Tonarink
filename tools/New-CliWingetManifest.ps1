[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$AssetDirectory,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'kusutori/Tonarink',
    [ValidateSet('x64', 'arm64')]
    [string[]]$Architectures = @('x64', 'arm64')
)

$ErrorActionPreference = 'Stop'
$identifier = 'kusutori.tonarink-cli'
$schema = '1.12.0'
$directory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) "manifests/k/kusutori/tonarink-cli/$Version"
$null = New-Item -ItemType Directory -Path $directory -Force
$url = "https://github.com/$Repository"
$header = "PackageIdentifier: $identifier`nPackageVersion: $Version"
$encoding = [Text.UTF8Encoding]::new($false)
$installers = foreach ($architecture in ($Architectures | Select-Object -Unique)) {
    $name = "tonarink-cli-$Version-win-$architecture.zip"
    $archive = Join-Path $AssetDirectory $name
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    @"
- Architecture: $architecture
  InstallerUrl: $url/releases/download/cli-v$Version/$name
  InstallerSha256: $hash
"@
}

$versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
$header
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@
$installerManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
$header
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
- RelativeFilePath: tonarink-cli.exe
  PortableCommandAlias: tonarink-cli
MinimumOSVersion: 10.0.19041.0
UpgradeBehavior: uninstallPrevious
Commands:
- tonarink-cli
Installers:
$($installers -join "`n")
ManifestType: installer
ManifestVersion: $schema
"@
$localeManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
$header
PackageLocale: en-US
Publisher: kusutori
PublisherUrl: https://github.com/kusutori
PublisherSupportUrl: $url/issues
PackageName: tonarink-cli
PackageUrl: $url
License: Apache-2.0
LicenseUrl: $url/blob/main/LICENSE
Copyright: Copyright (c) kusutori
ShortDescription: Standalone LocalSend-compatible command-line tool for file and text transfers
Moniker: tonarink-cli
Tags:
- cli
- file-transfer
- localsend
- tonarink
ReleaseNotesUrl: $url/releases/tag/cli-v$Version
Documentations:
- DocumentLabel: Command reference
  DocumentUrl: $url/blob/main/docs/cli.md
ManifestType: defaultLocale
ManifestVersion: $schema
"@
foreach ($entry in @{
    "$identifier.yaml" = $versionManifest
    "$identifier.installer.yaml" = $installerManifest
    "$identifier.locale.en-US.yaml" = $localeManifest
}.GetEnumerator()) {
    [IO.File]::WriteAllText((Join-Path $directory $entry.Key), $entry.Value + "`n", $encoding)
}
Write-Host "WinGet manifests: $directory"
