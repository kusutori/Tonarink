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
foreach ($required in @("tonarink-cli.$Version.nupkg", "tonarink-cli-$Version-win-x64.zip", "tonarink-cli-$Version-win-arm64.zip", 'SHA256SUMS.txt')) {
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
    $arguments = @('release', 'create', $tag, '--repo', $Repository, '--verify-tag', '--draft', '--latest=false', '--title', "tonarink-cli $Version", '--notes', @"
Standalone CLI, separate from the Tonarink desktop app.

- JIT / NuGet: ``dotnet tool install --global tonarink-cli --version $Version`` (requires .NET 11 SDK / shared runtimes)
- Native AOT / Windows: download the matching x64 or ARM64 portable ZIP; no .NET or WinUI runtime required
- WinGet: ``winget install --id kusutori.tonarink-cli --exact`` after the community manifest is merged (stable versions only)

Quit any running standalone host with ``tonarink-cli app quit --yes`` before upgrading. See README.md inside each ZIP and https://github.com/$Repository/blob/main/docs/cli.md for commands.
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
