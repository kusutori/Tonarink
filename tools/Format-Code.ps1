#requires -Version 7.0
<#
.SYNOPSIS
Formats the same workspaces checked by CI, or verifies them without changes.
.EXAMPLE
./tools/Format-Code.ps1 -Scope Core,Web,Windows -Verify
#>
[CmdletBinding()]
param(
    [ValidateSet('Core', 'Web', 'Windows')]
    [string[]]$Scope = @('Core', 'Web'),
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
if ($Scope -contains 'Windows' -and -not $IsWindows) {
    throw 'The Windows formatting scope requires Windows and the WinUI build tools.'
}

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Push-Location -LiteralPath $repo
try {
    $sdkVersion = (dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not select the SDK from global.json.' }
    $sdkPattern = '^' + [regex]::Escape($sdkVersion) + ' \[(.+)\]$'
    $sdkLine = dotnet --list-sdks | Where-Object { $_ -match $sdkPattern } | Select-Object -First 1
    if (-not $sdkLine) { throw "Could not locate SDK $sdkVersion." }
    $sdkRoot = [regex]::Match($sdkLine, $sdkPattern).Groups[1].Value
    # Enter the selected SDK explicitly: preview CLI tool dispatch can otherwise
    # resolve dotnet-format relative to the host rather than the SDK directory.
    $sdkCli = Join-Path $sdkRoot "$sdkVersion/dotnet.dll"
}
finally { Pop-Location }
$workspaces = [ordered]@{
    Core = @('LocalSendDotNet.Core.slnx')
    Web = @('src/Tonarink.Web/Tonarink.Web.csproj')
    Windows = @(
        'src/Tonarink.App/Tonarink.App.csproj',
        'src/Tonarink.ExplorerCommand/Tonarink.ExplorerCommand.csproj',
        'src/Tonarink.WidgetProvider/Tonarink.WidgetProvider.csproj'
    )
}
$previousPortable = $env:PortableTransportOnly
$previousPlatform = $env:Platform
$failed = [Collections.Generic.List[string]]::new()
Push-Location -LiteralPath $repo
try {
    # Applies to restore and format's MSBuild workspace, without mobile workloads.
    $env:PortableTransportOnly = 'true'
    foreach ($name in ($Scope | Select-Object -Unique)) {
        $env:Platform = if ($name -eq 'Windows') { 'x64' } else { $null }
        foreach ($workspace in $workspaces[$name]) {
            Write-Host "Formatting scope ${name}: $workspace (verify: $Verify)"
            dotnet exec $sdkCli restore $workspace
            if ($LASTEXITCODE -ne 0) {
                $failed.Add("$workspace (restore)")
                continue
            }
            $arguments = @('format', $workspace, '--no-restore', '--verbosity', 'minimal')
            if ($Verify) { $arguments += '--verify-no-changes' }
            dotnet exec $sdkCli @arguments
            if ($LASTEXITCODE -ne 0) { $failed.Add($workspace) }
        }
    }
}
finally {
    $env:PortableTransportOnly = $previousPortable
    $env:Platform = $previousPlatform
    Pop-Location
}
if ($failed.Count -gt 0) {
    throw "Formatting failed: $($failed -join ', '). Run ./tools/Format-Code.ps1 -Scope $($Scope -join ',') to apply fixes."
}
