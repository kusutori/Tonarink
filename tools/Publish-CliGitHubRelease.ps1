[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$AssetDirectory,
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,
    [switch]$Prerelease
)

$ErrorActionPreference = 'Stop'
$tag = "cli-v$Version"
$assets = @(Get-ChildItem -LiteralPath $AssetDirectory -File)
$project = Join-Path $PSScriptRoot '../src/Tonarink.Cli/Tonarink.Cli.csproj'
$rids = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.ToolPackageRuntimeIdentifiers.Split(';')
$requiredAssets = @("tonarink-cli.$Version.nupkg", 'SHA256SUMS.txt')
foreach ($rid in $rids) {
    $extension = if ($rid.StartsWith('win-')) { 'zip' } else { 'tar.gz' }
    $requiredAssets += "tonarink-cli.$rid.$Version.nupkg", "tonarink-cli-$Version-$rid.$extension"
}
foreach ($required in $requiredAssets) {
    if ($required -notin $assets.Name) { throw "Missing release asset: $required." }
}
$response = gh api "repos/$Repository/releases/tags/$tag" 2>&1
if ($LASTEXITCODE -eq 0) {
    $release = ($response -join "`n") | ConvertFrom-Json
    if (-not $release.draft) {
        # Never silently replace public assets referenced by immutable WinGet hashes.
        $download = Join-Path ([IO.Path]::GetTempPath()) "cli-release-check-$([guid]::NewGuid().ToString('N'))"
        gh release download $tag --repo $Repository --dir $download
        if ($LASTEXITCODE -ne 0) { throw 'Could not verify existing release assets.' }
        foreach ($asset in $assets) {
            $existing = Join-Path $download $asset.Name
            if (-not (Test-Path -LiteralPath $existing) -or
                (Get-FileHash -LiteralPath $existing).Hash -ne (Get-FileHash -LiteralPath $asset.FullName).Hash) {
                throw "Public asset $($asset.Name) differs; publish a new CLI version instead of overwriting it."
            }
        }
        Write-Host "Release $tag already published with matching assets."
        return
    }
}
else {
    if (($response -join "`n") -notmatch 'HTTP 404') { throw "Could not inspect release: $response" }
    $wingetNote = if ($Prerelease) {
        'This is a preview release and is not published to WinGet. Install the exact NuGet preview version or download a portable archive.'
    } else {
        'WinGet: `winget install --id kusutori.tonarink-cli --exact` after the community manifest is merged.'
    }
    $arguments = @('release', 'create', $tag, '--repo', $Repository, '--verify-tag', '--draft', '--latest=false', '--title', "tonarink-cli $Version (CLI only)", '--notes', @"
> **CLI only — no graphical user interface (GUI) is included.** This is a standalone command-line application, separate from the Tonarink Windows desktop app. For the GUI, download an ``app-v...`` release instead.

> **纯命令行版本，不包含图形界面（GUI）。** 此版本是独立 CLI，不是 Tonarink Windows 桌面应用；需要图形界面请下载 ``app-v...`` 版本。

- Native AOT / NuGet: ``dotnet tool install --global tonarink-cli --version $Version`` (.NET SDK 10+ selects the Windows, Linux or macOS x64/ARM64 package; no .NET 11 runtime required)
- Native AOT / portable: download the matching Windows ZIP or Linux/macOS tar.gz; no .NET or WinUI runtime required (Linux glibc 2.35+; macOS built/checked on 15, not Developer ID signed/notarized)
- $wingetNote

Quit any running standalone host with ``tonarink-cli app quit --yes`` before upgrading. See README.md inside each archive and https://github.com/$Repository/blob/main/docs/cli.md for commands.
"@)
    if ($Prerelease) { $arguments += '--prerelease' }
    gh @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the draft CLI release.' }
}

# A draft may be resumed safely. Public release assets are checked above, never clobbered.
gh release upload $tag --repo $Repository --clobber @($assets.FullName)
if ($LASTEXITCODE -ne 0) { throw 'Could not upload CLI assets; release remains a draft.' }
gh release edit $tag --repo $Repository --draft=false --latest=false
if ($LASTEXITCODE -ne 0) { throw 'Could not publish the CLI release.' }
