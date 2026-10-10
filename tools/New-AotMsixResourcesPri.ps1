<#
.SYNOPSIS
    Build resources.pri for an unpackaged Native AOT layout so MSIX visual
    assets (taskbar unplated logos) resolve through the package resource map.

.DESCRIPTION
    Windows Shell looks up Square44x44Logo in resources.pri. The unpackaged
    AOT publish only emits Tonarink.pri, which the shell does not use for
    package logos. A PNG-only resources.pri becomes the package primary map
    and WinUI then fails at startup (Microsoft.UI.Xaml.dll, 0xc000027b).

    This mirrors the Visual Studio packaged PRI: merge the app PRI, sibling
    dependency PRIs, RESW strings, and Assets with filename qualifiers, using
    the AppxManifest identity as the resource map name.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$LayoutPath,

    [string]$MakepriPath
)

$ErrorActionPreference = "Stop"

$layout = [System.IO.Path]::GetFullPath($LayoutPath)
$manifest = Join-Path $layout "AppxManifest.xml"
$assets = Join-Path $layout "Assets"
if (-not (Test-Path -LiteralPath $manifest)) {
    throw "AppxManifest.xml was not found in '$layout'."
}
if (-not (Test-Path -LiteralPath $assets)) {
    throw "Assets was not found in '$layout'."
}

$appPri = Join-Path $layout "Tonarink.pri"
if (-not (Test-Path -LiteralPath $appPri)) {
    throw "Tonarink.pri was not found in '$layout'."
}

if ([string]::IsNullOrWhiteSpace($MakepriPath)) {
    $sdkBinRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $sdkTools = Get-ChildItem -LiteralPath $sdkBinRoot -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName "x64\makepri.exe") } |
        Sort-Object { [Version]$_.Name } -Descending |
        Select-Object -First 1
    if ($null -eq $sdkTools) {
        throw "makepri.exe was not found under Windows Kits\10\bin."
    }
    $MakepriPath = Join-Path $sdkTools.FullName "x64\makepri.exe"
}

$work = Join-Path $layout "_pri"
if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
}
New-Item -ItemType Directory -Path $work | Out-Null

$defaultQualifiers = @"
    <default>
      <qualifier name="Language" value="en-US" />
      <qualifier name="Contrast" value="standard" />
      <qualifier name="Scale" value="200" />
      <qualifier name="HomeRegion" value="001" />
      <qualifier name="TargetSize" value="256" />
      <qualifier name="LayoutDirection" value="LTR" />
      <qualifier name="DXFeatureLevel" value="DX9" />
      <qualifier name="Configuration" value="" />
      <qualifier name="AlternateForm" value="" />
      <qualifier name="Platform" value="UAP" />
    </default>
"@

$assetFiles = @(Get-ChildItem -LiteralPath $assets -Recurse -File |
    Where-Object { $_.Extension -in ".png", ".ico", ".json", ".svg" })
if ($assetFiles.Count -lt 1) {
    throw "No visual assets were found in '$assets'."
}
$layoutResfiles = Join-Path $work "layout.resfiles"
# Shell URIs include subfolders (Assets/JumpList/...). Do not flatten the paths.
$assetFiles | ForEach-Object { [System.IO.Path]::GetRelativePath($layout, $_.FullName) } |
    Set-Content -LiteralPath $layoutResfiles -Encoding utf8

$resourceResfiles = Join-Path $work "resources.resfiles"
$reswFiles = @()
$strings = Join-Path $layout "Strings"
if (Test-Path -LiteralPath $strings) {
    $reswFiles = @(Get-ChildItem -LiteralPath $strings -Recurse -Filter "*.resw" -File |
        ForEach-Object { $_.FullName.Substring($layout.Length).TrimStart("\", "/") })
}
Set-Content -LiteralPath $resourceResfiles -Value $reswFiles -Encoding utf8

$priResfiles = Join-Path $work "pri.resfiles"
# Tonarink.pri is already the merged app + WinUI map. Re-index it so the
# package primary map is named after the AppxManifest identity (Tonarink.App).
# Do not also list dependency PRIs here; that duplicates Microsoft.UI.Xaml
# and WinUI then fail-fasts the same way a PNG-only resources.pri does.
Set-Content -LiteralPath $priResfiles -Value "Tonarink.pri" -Encoding utf8

$config = Join-Path $work "priconfig.xml"
@"
<?xml version="1.0" encoding="utf-8"?>
<resources targetOsVersion="10.0.0" majorVersion="1">
  <index root="\" startIndexAt="_pri\layout.resfiles">
$defaultQualifiers
    <indexer-config type="RESFILES" qualifierDelimiter="." />
  </index>
  <index root="\" startIndexAt="_pri\resources.resfiles">
$defaultQualifiers
    <indexer-config type="RESW" convertDotsToSlashes="true" />
    <indexer-config type="RESJSON" />
    <indexer-config type="RESFILES" qualifierDelimiter="." />
  </index>
  <index root="\" startIndexAt="_pri\pri.resfiles">
$defaultQualifiers
    <indexer-config type="PRI" />
    <indexer-config type="RESFILES" qualifierDelimiter="." />
  </index>
</resources>
"@ | Set-Content -LiteralPath $config -Encoding utf8

$pri = Join-Path $layout "resources.pri"
& $MakepriPath new /pr $layout /cf $config /of $pri /mn $manifest /o
if ($LASTEXITCODE -ne 0) {
    throw "makepri failed with exit code $LASTEXITCODE."
}

Remove-Item -LiteralPath $work -Recurse -Force
Get-ChildItem -LiteralPath $layout -File |
    Where-Object { $_.Name -in @(
        "assets.resfiles", "priconfig.xml", "excluded.layout.resfiles",
        "filtered.layout.resfiles", "pri.resfiles", "resources.resfiles",
        "unfiltered.layout.resfiles") } |
    Remove-Item -Force

Get-Item -LiteralPath $pri
