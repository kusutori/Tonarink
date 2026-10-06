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
    [string]$RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) "tonarink-cli-verify-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $work
$toolDirectory = Join-Path $work 'tool'
$profile = Join-Path $work 'profile'
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
& $cli --language zh-CN --help
if ($LASTEXITCODE -ne 0) { throw 'Localized tool help failed.' }
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
& $portable --language en-US --help
if ($LASTEXITCODE -ne 0) { throw 'The extracted portable executable failed to start.' }
try {
    & $cli --profile $profile --timeout 60 settings set alias ci-cli --json
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the standalone host.' }
    $settings = & $cli --profile $profile settings get alias --json
    if ($LASTEXITCODE -ne 0) { throw 'Could not query the standalone host.' }
    $response = $settings | ConvertFrom-Json
    if (@($response.data | Where-Object { $_.key -eq 'alias' -and $_.value -eq 'ci-cli' }).Count -ne 1) {
        throw 'Standalone settings did not round-trip.'
    }
    # Use an ephemeral port rather than touching another local instance's default port.
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Any, 0)
    $probe.Start()
    try { $port = $probe.LocalEndpoint.Port } finally { $probe.Stop() }
    & $cli --profile $profile settings set port $port --json
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure the isolated server.' }
    & $cli --profile $profile --timeout 60 server start --json
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the HTTPS server.' }
    $status = & $cli --profile $profile status --json
    if ($LASTEXITCODE -ne 0) { throw 'Could not query the running server.' }
    $response = $status | ConvertFrom-Json
    if ($response.data.state -ne 'Running' -or -not $response.data.fingerprint) {
        throw 'The server did not load an identity and enter Running state.'
    }
    & $cli --profile $profile server stop --json
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop the isolated server.' }
}
finally {
    & $cli --profile $profile app quit --yes --json
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop the isolated standalone host.' }
    Write-Host "Verification profile: $profile"
}
