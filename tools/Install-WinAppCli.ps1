[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$version = '0.6.1'
$expectedHash = '11C03BE2D356D6F910649CECCE912A9BC6A0814DC4F2E9AE14E379A8CF470F01'
$archive = Join-Path $env:RUNNER_TEMP "winappcli-$version-x64.zip"
$url = "https://github.com/microsoft/winappCli/releases/download/v$version/winappcli-x64.zip"

Invoke-WebRequest -Uri $url -OutFile $archive
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) {
    throw "WinApp CLI archive checksum mismatch: $actualHash"
}

Expand-Archive -LiteralPath $archive -DestinationPath $Destination -Force
$executable = Get-ChildItem -LiteralPath $Destination -Filter winapp.exe -File -Recurse |
    Select-Object -First 1
if ($null -eq $executable) {
    throw 'The WinApp CLI archive does not contain winapp.exe.'
}

$binDirectory = Split-Path -Parent $executable.FullName
$binDirectory >> $env:GITHUB_PATH
& $executable.FullName --version
if ($LASTEXITCODE -ne 0) {
    throw 'The downloaded WinApp CLI failed to start.'
}
