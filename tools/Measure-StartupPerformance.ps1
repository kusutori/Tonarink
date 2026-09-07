param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [int]$Iterations = 7,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient

$executable = (Resolve-Path $ExecutablePath).Path
$workingDirectory = Split-Path -Parent $executable
$selectionPattern = [System.Windows.Automation.SelectionItemPattern]::Pattern
$trueCondition = [System.Windows.Automation.Condition]::TrueCondition
$samples = [Collections.Generic.List[object]]::new()

function Test-AppReady([Diagnostics.Process]$Process) {
    $Process.Refresh()
    if ($Process.HasExited -or $Process.MainWindowHandle -eq 0) {
        return $false
    }

    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $trueCondition)
        $navigationItems = 0
        for ($index = 0; $index -lt $all.Count; $index++) {
            $pattern = $null
            if ($all.Item($index).TryGetCurrentPattern($selectionPattern, [ref]$pattern)) {
                $navigationItems++
            }
        }
        return $navigationItems -ge 3
    }
    catch {
        return $false
    }
}

for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $executable -WorkingDirectory $workingDirectory -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        while ([DateTime]::UtcNow -lt $deadline -and -not (Test-AppReady $process)) {
            Start-Sleep -Milliseconds 20
        }

        if (-not (Test-AppReady $process)) {
            throw "Tonarink did not expose its navigation UI within 20 seconds."
        }

        $stopwatch.Stop()
        $process.Refresh()
        $samples.Add([pscustomobject]@{
            Iteration = $iteration
            ReadyMilliseconds = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
            WorkingSetMiB = [Math]::Round($process.WorkingSet64 / 1MB, 2)
            PrivateMemoryMiB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        })
    }
    finally {
        if (-not $process.HasExited) {
            $process.CloseMainWindow() | Out-Null
            if (-not $process.WaitForExit(3000)) {
                Stop-Process -Id $process.Id -Force
            }
        }
        $process.Dispose()
    }

    Start-Sleep -Milliseconds 300
}

$measured = @($samples | Select-Object -Skip 1)
if ($measured.Count -eq 0) {
    $measured = @($samples)
}
$sorted = @($measured.ReadyMilliseconds | Sort-Object)
$p95Index = [Math]::Clamp([Math]::Ceiling($sorted.Count * 0.95) - 1, 0, $sorted.Count - 1)
$summary = [pscustomobject]@{
    Executable = $executable
    Samples = $measured.Count
    FirstLaunchMilliseconds = $samples[0].ReadyMilliseconds
    MeanMilliseconds = [Math]::Round(($measured.ReadyMilliseconds | Measure-Object -Average).Average, 3)
    P50Milliseconds = [Math]::Round($sorted[[Math]::Floor(($sorted.Count - 1) * 0.50)], 3)
    P95Milliseconds = [Math]::Round($sorted[$p95Index], 3)
    WorkingSetMeanMiB = [Math]::Round(($measured.WorkingSetMiB | Measure-Object -Average).Average, 2)
    PrivateMemoryMeanMiB = [Math]::Round(($measured.PrivateMemoryMiB | Measure-Object -Average).Average, 2)
}

if ($OutputPath) {
    [pscustomobject]@{ Summary = $summary; Samples = $samples } |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
}

$summary | Format-List
