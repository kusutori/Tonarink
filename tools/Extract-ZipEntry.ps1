param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter(Mandatory = $true)]
    [string]$Entry,

    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$destinationDirectory = [System.IO.Path]::GetDirectoryName($destinationPath)

[System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null

$archive = [System.IO.Compression.ZipFile]::OpenRead($sourcePath)
try {
    $archiveEntry = $archive.GetEntry($Entry)
    if ($null -eq $archiveEntry) {
        throw "Entry '$Entry' was not found in '$sourcePath'."
    }

    $inputStream = $archiveEntry.Open()
    try {
        $outputStream = [System.IO.File]::Open(
            $destinationPath,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        try {
            $inputStream.CopyTo($outputStream)
        }
        finally {
            $outputStream.Dispose()
        }
    }
    finally {
        $inputStream.Dispose()
    }
}
finally {
    $archive.Dispose()
}
