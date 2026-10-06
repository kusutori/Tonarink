[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$ManifestDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $env:WINGET_CREATE_GITHUB_TOKEN) { throw 'Configure WINGET_TOKEN (classic PAT with public_repo).' }
# Do not pass the PAT on the command line. WingetCreate and gh use environment variables.
$env:GH_TOKEN = $env:WINGET_CREATE_GITHUB_TOKEN
$path = "manifests/k/kusutori/tonarink-cli/$Version"
$merged = gh api "repos/microsoft/winget-pkgs/contents/$path" 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "Version $Version is already in winget-pkgs; skipping submission."
    return
}
if (($merged -join "`n") -notmatch 'HTTP 404') { throw "Could not check merged WinGet versions: $merged" }
$package = gh api 'repos/microsoft/winget-pkgs/contents/manifests/k/kusutori/tonarink-cli' 2>&1
if ($LASTEXITCODE -eq 0) { $prefix = 'New version' }
elseif (($package -join "`n") -match 'HTTP 404') { $prefix = 'New package' }
else { throw "Could not check WinGet package: $package" }
$title = "${prefix}: kusutori.tonarink-cli version $Version"
$existing = gh pr list --repo microsoft/winget-pkgs --author '@me' --state open --search "in:title kusutori.tonarink-cli $Version" --json title,url | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not check existing WinGet pull requests.' }
if ($matching = $existing | Where-Object title -EQ $title) {
    Write-Host "An open pull request already exists: $($matching.url)"
    return
}
wingetcreate submit $ManifestDirectory --prtitle $title --no-open
if ($LASTEXITCODE -ne 0) { throw 'WinGet submission failed; the public CLI release remains unchanged.' }
