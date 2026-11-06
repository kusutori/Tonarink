[CmdletBinding()]
param(
    [string]$AssetDirectory = (Join-Path $PSScriptRoot '../src/Tonarink.App/Assets/JumpList')
)

$ErrorActionPreference = 'Stop'
$magick = (Get-Command magick -ErrorAction Stop).Source

# Keep the existing packaged PNGs (including their light/dark variants) as the
# source of truth. Unpackaged IShellLink icons require an ICO instead of a PNG.
foreach ($source in Get-ChildItem -LiteralPath $AssetDirectory -Filter 'JumpList.*.png') {
    $destination = [IO.Path]::ChangeExtension($source.FullName, '.ico')
    & $magick $source.FullName '+repage' '-define' 'icon:auto-resize=48,32,24,20,16' $destination
    if ($LASTEXITCODE -ne 0) {
        throw "Could not generate $destination"
    }
}
