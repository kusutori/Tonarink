[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)

$ErrorActionPreference = 'Stop'
$version = '1.12.13.0'
$expectedHash = '24042BD37915805615E6CF969AC57C6439124C3FE85823327F5F3FB24BD9FFEA'
$null = New-Item -ItemType Directory -Path $Destination -Force
$executable = Join-Path $Destination 'wingetcreate.exe'
Invoke-WebRequest -Uri "https://github.com/microsoft/winget-create/releases/download/v$version/wingetcreate.exe" -OutFile $executable -MaximumRetryCount 2 -RetryIntervalSec 5 -ConnectionTimeoutSeconds 30
$actualHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) { throw "WingetCreate checksum mismatch: $actualHash." }
& $executable --version
if ($LASTEXITCODE -ne 0) { throw 'The downloaded WingetCreate failed to start.' }
if ($env:GITHUB_PATH) { $Destination >> $env:GITHUB_PATH }
