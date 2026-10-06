#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [switch]$PackTool
)

$ErrorActionPreference = 'Stop'
$targetOS, $targetArchitecture = $RuntimeIdentifier.Split('-')
$hostOS = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
if ($hostOS -ne $targetOS) { throw 'Native AOT must be built on the target operating system.' }
$extension = if ($targetOS -eq 'win') { '.exe' } else { '' }
$entryPoint = "Tonarink.Cli$extension"
$portableName = "tonarink-cli$extension"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/Tonarink.Cli/Tonarink.Cli.csproj'
$projectVersion = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
if ($Version -ne $projectVersion) { throw "Version $Version does not match CLI project $projectVersion." }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $output -Force
$work = Join-Path ([IO.Path]::GetTempPath()) "tonarink-cli-build-$([guid]::NewGuid().ToString('N'))"
$publish = Join-Path $work 'publish'
$stage = Join-Path $work 'stage'
$null = New-Item -ItemType Directory -Path $stage -Force

Push-Location -LiteralPath $repoRoot
try {
    if ($PackTool) {
        # Publish once, then reuse the exact native binary for NuGet and the portable archive.
        dotnet pack $project --configuration Release --runtime $RuntimeIdentifier --output $output `
            -p:PublishAot=true -p:ContinuousIntegrationBuild=true "-p:PublishDir=$publish/" `
            -p:DebugType=none -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false
        if ($LASTEXITCODE -ne 0) { throw 'Native AOT tool packing failed.' }
        $package = Join-Path $output "tonarink-cli.$RuntimeIdentifier.$Version.nupkg"
        if (-not (Test-Path -LiteralPath $package)) { throw "Missing RID tool package: $package." }
    }
    else {
        dotnet publish $project --configuration Release --runtime $RuntimeIdentifier --self-contained true `
            -p:PublishAot=true -p:PackAsTool=false -p:ContinuousIntegrationBuild=true --output $publish
        if ($LASTEXITCODE -ne 0) { throw 'Native AOT publish failed.' }
    }
    $executable = Join-Path $publish $entryPoint
    # Validate the native format and architecture, including cross-compiled Windows ARM64.
    $bytes = [IO.File]::ReadAllBytes($executable)
    switch ($targetOS) {
        'win' {
            if ($bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) { throw 'Expected a PE executable.' }
            $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
            if ([BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550) { throw 'Invalid PE signature.' }
            $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
            $expected = if ($targetArchitecture -eq 'x64') { 0x8664 } else { 0xAA64 }
            $clrHeader = [BitConverter]::ToUInt32($bytes, $peOffset + 24 + 112 + 14 * 8)
            if ($clrHeader -ne 0) { throw 'Expected a Native AOT executable, not a managed PE.' }
        }
        'linux' {
            if ([BitConverter]::ToUInt32($bytes, 0) -ne 0x464c457f -or $bytes[4] -ne 2 -or $bytes[5] -ne 1) {
                throw 'Expected a little-endian 64-bit ELF executable.'
            }
            $machine = [BitConverter]::ToUInt16($bytes, 18)
            $expected = if ($targetArchitecture -eq 'x64') { 62 } else { 183 }
        }
        'osx' {
            if ([BitConverter]::ToUInt32($bytes, 0) -ne 4277009103) { throw 'Expected a 64-bit Mach-O executable.' }
            $machine = [BitConverter]::ToUInt32($bytes, 4)
            $expected = if ($targetArchitecture -eq 'x64') { 0x01000007 } else { 0x0100000c }
            if ([BitConverter]::ToUInt32($bytes, 12) -ne 2) { throw 'Expected a Mach-O executable, not a library.' }
        }
    }
    if ($machine -ne $expected) { throw "Unexpected $targetOS architecture: $machine." }
    if ($PackTool) {
        $zip = [IO.Compression.ZipFile]::OpenRead($package)
        try {
            $settingsPath = "tools/any/$RuntimeIdentifier/DotnetToolSettings.xml"
            $reader = [IO.StreamReader]::new($zip.GetEntry($settingsPath).Open())
            try { $settings = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
            $command = $settings.DotNetCliTool.Commands.Command
            if ($command.Name -ne 'tonarink-cli' -or $command.Runner -ne 'executable' -or $command.EntryPoint -ne $entryPoint) {
                throw 'RID tool package does not declare the expected native executable.'
            }
            $stream = $zip.GetEntry("tools/any/$RuntimeIdentifier/$entryPoint").Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash) {
                throw 'NuGet and portable packages must contain the same native executable.'
            }
            if ($zip.Entries.FullName | Where-Object { $_ -match '\.(pdb|dbg)$|\.dSYM(/|$)' }) {
                throw 'RID tool package unexpectedly includes debug symbols.'
            }
        }
        finally { $zip.Dispose() }
    }
    $portableExecutable = Join-Path $stage $portableName
    Copy-Item -LiteralPath $executable -Destination $portableExecutable
    if ($targetOS -ne 'win') {
        chmod 755 $portableExecutable
        if ($LASTEXITCODE -ne 0) { throw 'Could not set executable permissions.' }
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src/Tonarink.Cli/README.md') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'), (Join-Path $repoRoot 'NOTICE') -Destination $stage
    $hostArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    if ($targetArchitecture -eq $hostArchitecture) {
        & $portableExecutable --language zh-CN --help
        if ($LASTEXITCODE -ne 0) { throw 'The renamed standalone AOT executable failed to start.' }
    }
    $archiveExtension = if ($targetOS -eq 'win') { 'zip' } else { 'tar.gz' }
    $archive = Join-Path $output "tonarink-cli-$Version-$RuntimeIdentifier.$archiveExtension"
    if (Test-Path -LiteralPath $archive) { throw "Refusing to overwrite $archive." }
    if ($targetOS -eq 'win') {
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal
    }
    else {
        # tar preserves the executable bit, unlike PowerShell Compress-Archive.
        tar -czf $archive -C $stage $portableName README.md LICENSE NOTICE
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the portable tar archive.' }
    }
    Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
}
finally {
    Pop-Location
    # Leave the unique temporary build directory for diagnostics; no user files are deleted.
    Write-Host "Build directory: $work"
}
