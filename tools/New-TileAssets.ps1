<#
.SYNOPSIS
Regenerates opaque Windows Start tile assets from the existing app icon.
.DESCRIPTION
Requires ImageMagick (magick) with SVG support. Centers the app mark over an
opaque, full-bleed teal background. Does not change desktop/taskbar icons.
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
        # Wide tiles add background space, not a stretched or enlarged mark.
        $logicalWidth = 384.0 * $tile.Width / $tile.Height
        $root.SetAttribute('viewBox', '0 0 ' + $logicalWidth.ToString('G17', $invariant) + ' 384')
        $root.SetAttribute('width', [string]($tile.Width * 4))
        $root.SetAttribute('height', [string]($tile.Height * 4))

        $glyph = $root.SelectSingleNode('svg:g', $ns)
        $defs = $root.SelectSingleNode('svg:defs', $ns)
        if ($null -eq $glyph -or $null -eq $defs) { throw 'AppIcon.svg must contain its mark and gradient definitions.' }
        $gradient = $svg.CreateElement('linearGradient', $root.NamespaceURI)
        $gradient.SetAttribute('id', 'tileBackground')
        $gradient.SetAttribute('x1', '0')
        $gradient.SetAttribute('y1', '0')
        $gradient.SetAttribute('x2', '1')
        $gradient.SetAttribute('y2', '1')
        foreach ($entry in @(@('0', '#14C4C2'), @('0.48', '#009BA3'), @('1', '#0B6570'))) {
            $stop = $svg.CreateElement('stop', $root.NamespaceURI)
            $stop.SetAttribute('offset', $entry[0])
            $stop.SetAttribute('stop-color', $entry[1])
            $gradient.AppendChild($stop) | Out-Null
        }
        $defs.AppendChild($gradient) | Out-Null
        $background = $svg.CreateElement('rect', $root.NamespaceURI)
        $background.SetAttribute('width', $logicalWidth.ToString('G17', $invariant))
        $background.SetAttribute('height', '384')
        $background.SetAttribute('fill', 'url(#tileBackground)')
        $root.InsertBefore($background, $glyph) | Out-Null
        $wrapper = $svg.CreateElement('g', $root.NamespaceURI)
        $offset = (($logicalWidth - 384 * 0.8) / 2).ToString('G17', $invariant)
        $wrapper.SetAttribute('transform', "translate($offset 38.4) scale(0.8)")
        $root.InsertBefore($wrapper, $glyph) | Out-Null
        $wrapper.AppendChild($glyph) | Out-Null
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
