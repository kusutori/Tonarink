[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier,
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [switch]$PackTool
)

$ErrorActionPreference = 'Stop'
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
        # The SDK publishes once, then wraps the native EXE in its RID-specific tool package.
        # Reuse that exact publish output for the portable ZIP, rather than compiling twice.
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
    $executable = Join-Path $publish 'Tonarink.Cli.exe'
    # Check the PE architecture even when the ARM64 artifact cannot run on this runner.
    $bytes = [IO.File]::ReadAllBytes($executable)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    $expected = if ($RuntimeIdentifier -eq 'win-x64') { 0x8664 } else { 0xAA64 }
    if ($machine -ne $expected) { throw "Unexpected PE architecture: $machine." }
    $clrHeader = [BitConverter]::ToUInt32($bytes, $peOffset + 24 + 112 + 14 * 8)
    if ($clrHeader -ne 0) { throw 'Expected a Native AOT executable, not a managed PE.' }
    if ($PackTool) {
        $zip = [IO.Compression.ZipFile]::OpenRead($package)
        try {
            $settingsPath = "tools/any/$RuntimeIdentifier/DotnetToolSettings.xml"
            $reader = [IO.StreamReader]::new($zip.GetEntry($settingsPath).Open())
            try { $settings = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
            $command = $settings.DotNetCliTool.Commands.Command
            if ($command.Name -ne 'tonarink-cli' -or $command.Runner -ne 'executable' -or $command.EntryPoint -ne 'Tonarink.Cli.exe') {
                throw 'RID tool package does not declare the expected native executable.'
            }
            $stream = $zip.GetEntry("tools/any/$RuntimeIdentifier/Tonarink.Cli.exe").Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash) {
                throw 'NuGet and portable packages must contain the same native executable.'
            }
            if ($zip.Entries.FullName | Where-Object { $_ -like '*.pdb' }) { throw 'RID tool package unexpectedly includes debug symbols.' }
        }
        finally { $zip.Dispose() }
    }
    Copy-Item -LiteralPath $executable -Destination (Join-Path $stage 'tonarink-cli.exe')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src/Tonarink.Cli/README.md') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'), (Join-Path $repoRoot 'NOTICE') -Destination $stage
    if ($RuntimeIdentifier -eq 'win-x64') {
        & (Join-Path $stage 'tonarink-cli.exe') --language zh-CN --help
        if ($LASTEXITCODE -ne 0) { throw 'The renamed standalone AOT executable failed to start.' }
    }
    $archive = Join-Path $output "tonarink-cli-$Version-$RuntimeIdentifier.zip"
    if (Test-Path -LiteralPath $archive) { throw "Refusing to overwrite $archive." }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal
    Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
}
finally {
    Pop-Location
    # Leave the unique temporary build directory for diagnostics; no user files are deleted.
    Write-Host "Build directory: $work"
}
