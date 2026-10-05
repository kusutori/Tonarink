<#
.SYNOPSIS
Regenerates opaque Windows Start tile assets from the existing app icon.
.DESCRIPTION
Requires ImageMagick (magick) with SVG support. Keeps the original glyph and
its proportions; extends only the teal plate to fill each tile, without the
rounded app-icon corners or rim. Does not change desktop/taskbar icon assets.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/Tonarink.App/Assets')
)

$ErrorActionPreference = 'Stop'
$magick = (Get-Command magick -ErrorAction Stop).Source
$sourcePath = Join-Path $PSScriptRoot '../src/Tonarink.App/Assets/AppIcon.svg'
$source = Get-Content -LiteralPath $sourcePath -Raw
$outputPath = (Resolve-Path -LiteralPath $OutputDirectory).Path
$invariant = [Globalization.CultureInfo]::InvariantCulture
$tiles = @(
    @{ Name = 'Square71x71Logo'; Width = 71; Height = 71 },
    @{ Name = 'Square150x150Logo'; Width = 150; Height = 150 },
    @{ Name = 'Wide310x150Logo'; Width = 310; Height = 150 },
    @{ Name = 'Square310x310Logo'; Width = 310; Height = 310 }
)
$scales = @(100, 125, 150, 200, 400)
$temporaryPath = Join-Path ([IO.Path]::GetTempPath()) ('tonarink-tiles-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryPath | Out-Null
$svgPath = Join-Path $temporaryPath 'tile.svg'
$masterPath = Join-Path $temporaryPath 'tile.png'

try {
    foreach ($tile in $tiles) {
        [xml]$svg = $source
        $ns = [Xml.XmlNamespaceManager]::new($svg.NameTable)
        $ns.AddNamespace('svg', 'http://www.w3.org/2000/svg')
        $root = $svg.DocumentElement
        # All tile layouts use the original 256-unit height. Wide tiles add
        # background space on either side, not a stretched or enlarged glyph.
        $logicalWidth = 256.0 * $tile.Width / $tile.Height
        $root.SetAttribute('viewBox', '0 0 ' + $logicalWidth.ToString('G17', $invariant) + ' 256')
        $root.SetAttribute('width', [string]($tile.Width * 4))
        $root.SetAttribute('height', [string]($tile.Height * 4))

        $plate = $root.SelectSingleNode("svg:g[@clip-path='url(#plateClip)']", $ns)
        $glyph = $root.SelectSingleNode("svg:g[@filter='url(#glyphLift)']", $ns)
        if ($null -eq $plate -or $null -eq $glyph) {
            throw 'AppIcon.svg no longer has the expected plate/glyph groups.'
        }
        $plate.RemoveAttribute('clip-path')
        foreach ($rect in $plate.SelectNodes('svg:rect', $ns)) {
            $rect.SetAttribute('width', $logicalWidth.ToString('G17', $invariant))
            $rect.RemoveAttribute('rx')
        }
        $rim = $root.SelectSingleNode("svg:rect[@stroke='url(#rim)']", $ns)
        if ($null -ne $rim) { $root.RemoveChild($rim) | Out-Null }
        $clip = $root.SelectSingleNode("svg:defs/svg:clipPath[@id='plateClip']", $ns)
        if ($null -ne $clip) { $clip.ParentNode.RemoveChild($clip) | Out-Null }
        $offset = (($logicalWidth - 256) / 2).ToString('G17', $invariant)
        $glyph.SetAttribute('transform', "translate($offset 0)")
        $svg.Save($svgPath)

        # Render a vector master at twice the largest output size, then
        # downsample for clean edges even at the smallest tile scale.
        & $magick '-background' 'none' '-density' '192' $svgPath '-strip' "PNG24:$masterPath"
        if ($LASTEXITCODE -ne 0) { throw "SVG rendering failed for $($tile.Name)." }
        foreach ($scale in $scales) {
            $width = [int][Math]::Ceiling($tile.Width * $scale / 100.0)
            $height = [int][Math]::Ceiling($tile.Height * $scale / 100.0)
            $suffix = if ($scale -eq 100) { '' } else { ".scale-$scale" }
            $destination = Join-Path $outputPath ($tile.Name + $suffix + '.png')
            & $magick $masterPath '-filter' 'Lanczos' '-resize' "${width}x${height}!" '-strip' '-define' 'png:exclude-chunk=date,time' "PNG24:$destination"
            if ($LASTEXITCODE -ne 0) { throw "PNG generation failed for $destination." }
            Write-Host "Generated $($tile.Name)$suffix.png (${width}x${height})"
        }
    }
}
finally {
    # Delete only our two generated temporary files and the now-empty folder.
    foreach ($temporaryFile in @($svgPath, $masterPath)) {
        if (Test-Path -LiteralPath $temporaryFile) {
            Remove-Item -LiteralPath $temporaryFile
        }
    }
    Remove-Item -LiteralPath $temporaryPath
}
