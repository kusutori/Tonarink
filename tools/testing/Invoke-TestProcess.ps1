#requires -Version 7.0

# Thin process orchestration only. Test selection and all behavior assertions
# belong to MSTest; this function also bounds uncooperative test hosts locally.
function Invoke-TestProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$ResultsDirectory,
        [Parameter(Mandatory)][ValidateRange(1, 86400)][int]$TimeoutSeconds
    )

    $ErrorActionPreference = 'Stop'
    $null = New-Item -ItemType Directory -Path $ResultsDirectory -Force
    $startInfo = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    foreach ($argument in $ArgumentList) { $startInfo.ArgumentList.Add($argument) }

    $child = [Diagnostics.Process]::new()
    $child.StartInfo = $startInfo
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $record = [ordered]@{
        startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        executable = $FilePath
        arguments = $ArgumentList
        workingDirectory = $WorkingDirectory
        timeoutSeconds = $TimeoutSeconds
        pid = $null
        outcome = 'launch-error'
        exitCode = 125
        nativeExitCode = $null
        streamsDrained = $false
        error = $null
    }
    $outputTask = $null
    $errorTask = $null
    try {
        if (-not $child.Start()) { throw "Cannot launch test process: $FilePath" }
        $record.pid = $child.Id
        $child.StandardInput.Close()
        $outputTask = $child.StandardOutput.ReadToEndAsync()
        $errorTask = $child.StandardError.ReadToEndAsync()
        $exitTask = $child.WaitForExitAsync()
        $all = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($exitTask, $outputTask, $errorTask))
        try {
            $all.WaitAsync([TimeSpan]::FromSeconds($TimeoutSeconds)).GetAwaiter().GetResult()
            $record.outcome = 'exited'
            $record.exitCode = $child.ExitCode
        }
        catch [TimeoutException] {
            $record.outcome = 'hard-deadline'
            $record.exitCode = 124
            $record.error = "Owned test process exceeded $TimeoutSeconds seconds."
        }
    }
    catch {
        $record.outcome = 'controller-error'
        $record.exitCode = 125
        $record.error = $_.Exception.ToString()
    }
    finally {
        if ($null -ne $record.pid) {
            try {
                # Never look up or kill an unrelated process by name or PID.
                # This is the exact Process object started above.
                if (-not $child.HasExited) { $child.Kill($true) }
                $child.WaitForExitAsync().WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
                $record.nativeExitCode = $child.ExitCode
                [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($outputTask, $errorTask)).WaitAsync(
                    [TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
                $record.streamsDrained = $true
            }
            catch {
                $record.outcome = 'cleanup-error'
                $record.exitCode = 125
                $record.error = "$($record.error)`n$($_.Exception)"
            }
        }
        $record.elapsedSeconds = $clock.Elapsed.TotalSeconds
        $record.finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        $output = if ($null -ne $outputTask -and $outputTask.IsCompletedSuccessfully) { $outputTask.Result } else { '<stdout unavailable or still open>' }
        $errorOutput = if ($null -ne $errorTask -and $errorTask.IsCompletedSuccessfully) { $errorTask.Result } else { '<stderr unavailable or still open>' }
        [IO.File]::WriteAllText((Join-Path $ResultsDirectory 'controller.stdout.txt'), $output)
        [IO.File]::WriteAllText((Join-Path $ResultsDirectory 'controller.stderr.txt'), $errorOutput)
        [IO.File]::WriteAllText((Join-Path $ResultsDirectory 'controller.json'), ($record | ConvertTo-Json -Depth 5))
        $child.Dispose()
    }
    [pscustomobject]$record
}
