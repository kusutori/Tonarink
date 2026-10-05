[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier,
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$OutputDirectory
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
    dotnet publish $project --configuration Release --runtime $RuntimeIdentifier --self-contained true `
        -p:PublishAot=true -p:PackAsTool=false -p:ContinuousIntegrationBuild=true --output $publish
    if ($LASTEXITCODE -ne 0) { throw 'Native AOT publish failed.' }
    $executable = Join-Path $publish 'Tonarink.Cli.exe'
    # Check the PE architecture even when the ARM64 artifact cannot run on this runner.
    $bytes = [IO.File]::ReadAllBytes($executable)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    $expected = if ($RuntimeIdentifier -eq 'win-x64') { 0x8664 } else { 0xAA64 }
    if ($machine -ne $expected) { throw "Unexpected PE architecture: $machine." }
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
