#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AssetDirectory,
    [Parameter(Mandatory)]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$InstallerSdkVersion,
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,
    [string]$ResultsDirectory = 'artifacts/test-results/cli-distribution',
    # Only for a caller that already built the current test controller. Avoid
    # touching its binaries when independently isolated suites run concurrently.
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) "tonarink-cli-verify-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $work
$toolDirectory = Join-Path $work 'tool'
$config = Join-Path $work 'NuGet.Config'
$assetRoot = (Resolve-Path -LiteralPath $AssetDirectory).Path
$feed = [Security.SecurityElement]::Escape($assetRoot)
# Only local artifacts can satisfy the CLI install, never a published version.
# SDK 10 still resolves a legacy apphost template for tools/any on macOS ARM64
# before creating the native executable launcher. Allow only that SDK support
# package from NuGet.org; the CLI and every RID package must come from this build.
Set-Content -LiteralPath $config -Value @"
<configuration>
  <packageSources>
    <clear />
    <add key="cli-build" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="cli-build">
      <package pattern="tonarink-cli" />
      <package pattern="tonarink-cli.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="Microsoft.NETCore.App.Host.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
$sdkCli = Join-Path $env:DOTNET_ROOT "sdk/$InstallerSdkVersion/dotnet.dll"
$dotnetHost = Join-Path $env:DOTNET_ROOT $(if ($IsWindows) { 'dotnet.exe' } else { 'dotnet' })
$hostOS, $architecture = $RuntimeIdentifier.Split('-')
# Use the selected SDK installation's host, rather than another dotnet on PATH.
& $dotnetHost exec $sdkCli tool install tonarink-cli --tool-path $toolDirectory --version $Version --configfile $config --no-cache
if ($LASTEXITCODE -ne 0) { throw 'AOT tool installation failed.' }
# Native tools use a .cmd launcher on Windows and an executable launcher on Unix.
$cli = Join-Path $toolDirectory 'tonarink-cli'
& $cli --language en-US --help
if ($LASTEXITCODE -ne 0) { throw 'Tool help failed.' }
# Check the distributed archive, not only the staging binary. On Unix this also
# catches a lost executable bit: deliberately do not chmod after extraction.
$archiveExtension = if ($IsWindows) { 'zip' } else { 'tar.gz' }
$archive = Join-Path $assetRoot "tonarink-cli-$Version-$hostOS-$architecture.$archiveExtension"
$portableDirectory = Join-Path $work 'portable'
$null = New-Item -ItemType Directory -Path $portableDirectory
if ($IsWindows) {
    Expand-Archive -LiteralPath $archive -DestinationPath $portableDirectory
    $portable = Join-Path $portableDirectory 'tonarink-cli.exe'
}
else {
    tar -xzf $archive -C $portableDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the portable archive.' }
    $portable = Join-Path $portableDirectory 'tonarink-cli'
}
# Locate the native payload installed by dotnet tool, not a global/cached tool.
# The .cmd/executable launcher above is checked independently; all functional
# contracts run against its installed payload and the extracted portable binary.
$entryPoint = if ($IsWindows) { 'Tonarink.Cli.exe' } else { 'Tonarink.Cli' }
# Unix tool payloads live under hidden .store; without -Force the scan reports
# no payload even when the installed native launcher above ran successfully.
$installed = @(Get-ChildItem -LiteralPath $toolDirectory -Recurse -Force -File -Filter $entryPoint)
if ($installed.Count -ne 1) { throw "Expected exactly one installed native tool payload, found $($installed.Count)." }
$package = Join-Path $assetRoot "tonarink-cli.$RuntimeIdentifier.$Version.nupkg"
$zip = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $stream = $zip.GetEntry("tools/any/$RuntimeIdentifier/$entryPoint").Open()
    try { $nativeHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
}
finally { $zip.Dispose() }
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$resultsRoot = if ([IO.Path]::IsPathRooted($ResultsDirectory)) { [IO.Path]::GetFullPath($ResultsDirectory) }
else { [IO.Path]::GetFullPath((Join-Path $repoRoot $ResultsDirectory)) }
$runTests = Join-Path $PSScriptRoot 'Invoke-Tests.ps1'
foreach ($artifact in @(@('tool', $installed[0].FullName), @('portable', $portable))) {
    $kind, $executable = $artifact
    & $runTests -Suite Cli -Configuration Release -CliExecutable $executable -NativeRid $RuntimeIdentifier `
        -CliVersion $Version -ExpectedSha256 $nativeHash `
        -ResultsDirectory (Join-Path $resultsRoot "$RuntimeIdentifier/$kind") -NoBuild:$NoBuild
}
Write-Host "Installed tool and portable tested from: $work"
