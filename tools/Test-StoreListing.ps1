[CmdletBinding()]
param(
    [string]$ListingRoot = (Join-Path $PSScriptRoot '..\store-listing')
)

$ErrorActionPreference = 'Stop'
$requiredText = @('shortDescription', 'description', 'releaseNotes')
$requiredLists = @('features', 'searchTerms')
$sourcePath = Join-Path $ListingRoot 'en-US\listing.json'

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Missing source Store listing: $sourcePath"
}

$source = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
$expectedProperties = @($source.PSObject.Properties.Name | Sort-Object)
$listingFiles = Get-ChildItem -LiteralPath $ListingRoot -Filter listing.json -File -Recurse

if ($listingFiles.Count -eq 0) {
    throw "No Store listings were found under $ListingRoot."
}

foreach ($file in $listingFiles) {
    $listing = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $actualProperties = @($listing.PSObject.Properties.Name | Sort-Object)
    if (Compare-Object $expectedProperties $actualProperties) {
        throw "$($file.FullName) does not contain the same fields as the en-US source."
    }

    foreach ($name in $requiredText) {
        if ([string]::IsNullOrWhiteSpace([string]$listing.$name)) {
            throw "$($file.FullName): '$name' must not be empty."
        }
    }

    foreach ($name in $requiredLists) {
        $values = @($listing.$name)
        if ($values.Count -eq 0 -or $values.Where({ [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) {
            throw "$($file.FullName): '$name' must contain non-empty values."
        }
    }
}

Write-Host "Validated $($listingFiles.Count) Store listing locale(s)."
