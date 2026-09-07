param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [int]$WarmupRounds = 3,

    [int]$MeasuredRounds = 20,

    [int]$SettleMilliseconds = 500,

    [int]$PostRunSettleMilliseconds = 1000,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient

$process = Get-Process -Id $ProcessId
$process.WaitForInputIdle(10000) | Out-Null
$process.Refresh()

if ($process.MainWindowHandle -eq 0) {
    throw "Process $ProcessId does not have a main window."
}

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$selectionPattern = [System.Windows.Automation.SelectionItemPattern]::Pattern
$trueCondition = [System.Windows.Automation.Condition]::TrueCondition

function Get-NavigationItems {
    $all = $root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $trueCondition)

    $items = for ($index = 0; $index -lt $all.Count; $index++) {
        $element = $all.Item($index)
        if ($element.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) {
            continue
        }

        $pattern = $null
        if ($element.TryGetCurrentPattern($selectionPattern, [ref]$pattern)) {
            [pscustomobject]@{
                Name = $element.Current.Name
                Element = $element
                Pattern = [System.Windows.Automation.SelectionItemPattern]$pattern
            }
        }
    }

    return @($items | Select-Object -First 3)
}

function Select-And-Wait([object]$item) {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $item.Pattern.Select()
    $stopwatch.Stop()
    Start-Sleep -Milliseconds $SettleMilliseconds
    return $stopwatch.Elapsed.TotalMilliseconds
}

$navigationItems = Get-NavigationItems
if ($navigationItems.Count -ne 3) {
    throw "Expected three navigation items, found $($navigationItems.Count)."
}

for ($round = 0; $round -lt $WarmupRounds; $round++) {
    for ($index = 0; $index -lt $navigationItems.Count; $index++) {
        Select-And-Wait $navigationItems[$index] | Out-Null
    }
}

$process.Refresh()
$workingSetBefore = $process.WorkingSet64
$privateMemoryBefore = $process.PrivateMemorySize64
$cpuBefore = $process.TotalProcessorTime
$samples = [System.Collections.Generic.List[object]]::new()

for ($round = 0; $round -lt $MeasuredRounds; $round++) {
    for ($index = 0; $index -lt $navigationItems.Count; $index++) {
        $elapsed = Select-And-Wait $navigationItems[$index]
        $samples.Add([pscustomobject]@{
            Round = $round + 1
            Page = $navigationItems[$index].Name
            ElapsedMilliseconds = [Math]::Round($elapsed, 3)
        })
    }
}

$process.Refresh()
$workingSetImmediatelyAfter = $process.WorkingSet64
$privateMemoryImmediatelyAfter = $process.PrivateMemorySize64
$cpuAfter = $process.TotalProcessorTime
if ($PostRunSettleMilliseconds -gt 0) {
    Start-Sleep -Milliseconds $PostRunSettleMilliseconds
    $process.Refresh()
}
$sorted = @($samples.ElapsedMilliseconds | Sort-Object)
$p95Index = [Math]::Min(
    $sorted.Count - 1,
    [Math]::Ceiling($sorted.Count * 0.95) - 1)

$summary = [pscustomobject]@{
    ProcessId = $ProcessId
    Samples = $samples.Count
    MeanMilliseconds = [Math]::Round(($samples.ElapsedMilliseconds | Measure-Object -Average).Average, 3)
    P50Milliseconds = [Math]::Round($sorted[[Math]::Floor(($sorted.Count - 1) * 0.50)], 3)
    P95Milliseconds = [Math]::Round($sorted[$p95Index], 3)
    MaximumMilliseconds = [Math]::Round(($samples.ElapsedMilliseconds | Measure-Object -Maximum).Maximum, 3)
    WorkingSetBeforeMiB = [Math]::Round($workingSetBefore / 1MB, 2)
    WorkingSetImmediatelyAfterMiB = [Math]::Round($workingSetImmediatelyAfter / 1MB, 2)
    WorkingSetAfterMiB = [Math]::Round($process.WorkingSet64 / 1MB, 2)
    PrivateMemoryBeforeMiB = [Math]::Round($privateMemoryBefore / 1MB, 2)
    PrivateMemoryImmediatelyAfterMiB = [Math]::Round($privateMemoryImmediatelyAfter / 1MB, 2)
    PrivateMemoryAfterMiB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 2)
    CpuMilliseconds = [Math]::Round(($cpuAfter - $cpuBefore).TotalMilliseconds, 2)
}

if ($OutputPath) {
    $outputDirectory = Split-Path -Parent $OutputPath
    if ($outputDirectory) {
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    }

    [pscustomobject]@{
        Summary = $summary
        Samples = $samples
    } | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputPath -Encoding utf8
}

$summary | Format-List
