param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [string]$OutputPath,

    [double]$WindowMilliseconds = 400
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$navigationStarts = [Collections.Generic.List[double]]::new()
$xamlEvents = [Collections.Generic.List[object]]::new()
$pidMarker = 'PID="' + $ProcessId + '"'
$currentWindow = -1

$interestingEvents = @(
    'MeasureElement',
    'ArrangeElement',
    'ApplyTemplate',
    'ProcessLayoutForTransition',
    'UIThreadCallback',
    'UIThreadFrame',
    'RenderThreadFrame',
    'HWCompNodeUpdate'
)

foreach ($line in [IO.File]::ReadLines((Resolve-Path $Path))) {
    if (-not $line.Contains($pidMarker) -or $line -notmatch 'MSec=\s*"([0-9.]+)"') {
        continue
    }

    $milliseconds = [double]::Parse($matches[1], $culture)
    if ($line.Contains('ProviderName="Microsoft-UI-Reactor"') -and
        $line.Contains('EventName="Reconcile/Start"')) {
        $navigationStarts.Add($milliseconds)
        $currentWindow = $navigationStarts.Count - 1
        continue
    }

    if (-not $line.Contains('ProviderName="Microsoft-Windows-XAML"') -or
        $line -notmatch 'EventName="([^"]+)"') {
        continue
    }

    $eventName = $matches[1]
    $baseName = $eventName -replace '/(Start|Stop)$', ''
    if ($baseName -notin $interestingEvents -or
        $currentWindow -lt 0 -or
        $milliseconds -ge ($navigationStarts[$currentWindow] + $WindowMilliseconds)) {
        continue
    }

    if ($line -notmatch 'TID=\s*"([0-9]+)"') {
        continue
    }

    $xamlEvents.Add([pscustomobject]@{
        Milliseconds = $milliseconds
        ThreadId = [int]$matches[1]
        Name = $eventName
        Window = $currentWindow
    })
}

function Get-Percentile([double[]]$Values, [double]$Percentile) {
    if ($Values.Count -eq 0) { return 0 }
    $sorted = $Values | Sort-Object
    $index = [Math]::Clamp([Math]::Ceiling($sorted.Count * $Percentile) - 1, 0, $sorted.Count - 1)
    return $sorted[$index]
}

function Get-Summary([double[]]$Values) {
    if ($Values.Count -eq 0) {
        return [ordered]@{ Count = 0; Mean = 0; P50 = 0; P95 = 0; Maximum = 0 }
    }

    return [ordered]@{
        Count = $Values.Count
        Mean = [Math]::Round(($Values | Measure-Object -Average).Average, 3)
        P50 = [Math]::Round((Get-Percentile $Values 0.50), 3)
        P95 = [Math]::Round((Get-Percentile $Values 0.95), 3)
        Maximum = [Math]::Round(($Values | Measure-Object -Maximum).Maximum, 3)
    }
}

$windows = foreach ($start in $navigationStarts) {
    $windowIndex = [array]::IndexOf($navigationStarts.ToArray(), $start)
    $events = $xamlEvents.Where({ $_.Window -eq $windowIndex })
    $counts = [ordered]@{}
    foreach ($baseName in $interestingEvents) {
        $counts[$baseName] = @($events.Where({ $_.Name -eq "$baseName/Start" -or $_.Name -eq $baseName })).Count
    }

    [pscustomobject]@{
        StartMilliseconds = $start
        Counts = $counts
    }
}

$eventCountSummaries = [ordered]@{}
foreach ($baseName in $interestingEvents) {
    $values = [double[]]@($windows | ForEach-Object { $_.Counts[$baseName] })
    $eventCountSummaries[$baseName] = Get-Summary $values
}

$durationSummaries = [ordered]@{}
foreach ($baseName in $interestingEvents) {
    $open = @{}
    $durations = [Collections.Generic.List[double]]::new()
    foreach ($event in $xamlEvents) {
        $key = "$($event.Window):$($event.ThreadId):$baseName"
        if ($event.Name -eq "$baseName/Start") {
            if (-not $open.ContainsKey($key)) {
                $open[$key] = [Collections.Generic.Stack[double]]::new()
            }
            $open[$key].Push($event.Milliseconds)
        }
        elseif ($event.Name -eq "$baseName/Stop" -and $open.ContainsKey($key) -and $open[$key].Count -gt 0) {
            $durations.Add($event.Milliseconds - $open[$key].Pop())
        }
    }
    $durationSummaries[$baseName] = Get-Summary ([double[]]$durations)
}

$report = [ordered]@{
    Trace = (Resolve-Path $Path).Path
    ProcessId = $ProcessId
    NavigationWindowMilliseconds = $WindowMilliseconds
    NavigationWindowCount = $navigationStarts.Count
    EventCountsPerNavigation = $eventCountSummaries
    EventDurationsMilliseconds = $durationSummaries
}

$json = $report | ConvertTo-Json -Depth 8
$json
if ($OutputPath) {
    Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8NoBOM
}
