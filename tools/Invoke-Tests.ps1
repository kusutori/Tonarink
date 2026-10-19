#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Core', 'Application', 'CommandLine', 'Cli', 'WinUI', 'WindowsInput', 'AppE2E', 'Blazor', 'Mobile')]
    [string[]]$Suite = @('Core', 'Application', 'CommandLine'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$CliExecutable,
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$NativeRid,
    [string]$CliVersion,
    [string]$ExpectedSha256,
    [string]$AppLayout,
    [string]$AppExpectedSha256,
    [string]$ResultsDirectory = 'artifacts/test-results/local',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'testing/Invoke-TestProcess.ps1')
$projects = @{
    Core = @('LocalSendDotNet.Core.Tests', 95)
    Application = @('Tonarink.Application.Tests', 26)
    CommandLine = @('Tonarink.CommandLine.Tests', 186)
    Cli = @('Tonarink.Cli.E2E.Tests', 55)
    WinUI = @('Tonarink.App.WinUI.Tests', 126)
    WindowsInput = @('Tonarink.App.WinUI.Tests', 1)
    AppE2E = @('Tonarink.App.E2E.Tests', 2)
    Blazor = @('Tonarink.Blazor.Shared.Tests', 8)
    Mobile = @('LocalSendDotNet.Core.Mobile.Tests', 5)
}
$previousExecutable = $env:TONARINK_CLI_TEST_EXECUTABLE
$previousRid = $env:TONARINK_CLI_TEST_RID
$previousVersion = $env:TONARINK_CLI_TEST_VERSION
$previousHash = $env:TONARINK_CLI_EXPECTED_SHA256
$previousAppLayout = $env:TONARINK_APP_TEST_LAYOUT
$previousAppHash = $env:TONARINK_APP_EXPECTED_SHA256
Push-Location -LiteralPath $repo
try {
    $results = [IO.Path]::GetFullPath($ResultsDirectory)
    if (($Suite -contains 'WinUI' -or $Suite -contains 'WindowsInput' -or $Suite -contains 'AppE2E') -and -not $IsWindows) { throw 'Windows UI suites require a Windows runner.' }
    if ($Suite -contains 'AppE2E') {
        if (-not $AppLayout -or -not $AppExpectedSha256) { throw 'AppE2E requires -AppLayout and -AppExpectedSha256 for the current Native AOT layout; it never falls back to an installed app.' }
        $env:TONARINK_APP_TEST_LAYOUT = (Resolve-Path -LiteralPath $AppLayout).Path
        $env:TONARINK_APP_EXPECTED_SHA256 = $AppExpectedSha256
    }
    if ($Suite -contains 'Cli') {
        if (-not $CliExecutable) { throw 'Pass -CliExecutable with the exact current-build artifact; no installed fallback is used.' }
        $env:TONARINK_CLI_TEST_EXECUTABLE = (Resolve-Path -LiteralPath $CliExecutable).Path
        if ($NativeRid) {
            if (-not $CliVersion -or -not $ExpectedSha256) { throw 'Native artifact runs require -CliVersion and -ExpectedSha256 from the current RID package.' }
            $env:TONARINK_CLI_TEST_RID = $NativeRid
            $env:TONARINK_CLI_TEST_VERSION = $CliVersion
            $env:TONARINK_CLI_EXPECTED_SHA256 = $ExpectedSha256
        }
    }
    $null = New-Item -ItemType Directory -Path $results -Force
    # Provenance, not business assertions: preserve the exact external executable
    # hash and runner details alongside MSTest's own result files.
    $metadata = [ordered]@{
        timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
        configuration = $Configuration
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        sdk = (dotnet --version)
        suites = $Suite
    }
    if ($Suite -contains 'Cli') {
        $metadata.cliExecutable = $env:TONARINK_CLI_TEST_EXECUTABLE
        $metadata.cliSha256 = (Get-FileHash -LiteralPath $env:TONARINK_CLI_TEST_EXECUTABLE -Algorithm SHA256).Hash
        $metadata.nativeRid = $NativeRid
        $metadata.packageVersion = $CliVersion
        $metadata.expectedPackageExecutableSha256 = $ExpectedSha256
    }
    if ($Suite -contains 'AppE2E') {
        $metadata.appLayout = $env:TONARINK_APP_TEST_LAYOUT
        $metadata.appExpectedSha256 = $AppExpectedSha256
        $metadata.appTarget = 'sandbox'
        $metadata.appDeployment = 'loose-development-package'
    }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'run.json') -Encoding utf8
    foreach ($name in $Suite) {
        $project, $minimum = $projects[$name]
        if ($name -eq 'Cli' -and $NativeRid) { $minimum += 3 }
        # The 52-case Windows JIT run reached 48 passing cases at the old
        # 8-minute deadline; individual cases remained below their deadlines.
        # Budget the expanded serial workload, preserving per-case limits and
        # the original aborted report rather than retrying unchanged runs.
        $suiteTimeout = if ($name -eq 'Cli') { '10m' } else { '5m' }
        $arguments = @('test', '--project', "tests/$project", '-c', $Configuration,
            '--report-trx', '--results-directory', (Join-Path $results $name.ToLowerInvariant()),
            '--minimum-expected-tests', "$minimum", '--zero-tests-policy', 'strict', '--timeout', $suiteTimeout)
        if ($NoBuild) { $arguments += '--no-build' }
        if ($name -in @('WinUI', 'WindowsInput')) { $arguments += '-p:Platform=x64' }
        if ($name -eq 'WinUI') { $arguments += @('--filter', 'TestCategory!=SystemInput') }
        if ($name -eq 'WindowsInput') { $arguments += @('--filter', 'TestCategory=SystemInput') }
        if ($name -eq 'Mobile') { $arguments += '-p:PortableTransportOnly=true' }
        if ($name -eq 'Cli' -and -not $NativeRid) { $arguments += @('--filter', 'TestCategory!=Distribution') }
        $controllerDirectory = Join-Path $results "$($name.ToLowerInvariant())/controller"
        # MTP gets its cooperative deadline first; the owning controller also
        # bounds startup/build and a test body that ignores cancellation.
        $hardLimit = if ($name -eq 'Cli') { 660 } else { 360 }
        # Hosted runners can expose dotnet from several PATH entries. Launch
        # the first application, matching shell resolution, not an array.
        $dotnetExecutable = Get-Command dotnet -CommandType Application | Select-Object -First 1 -ExpandProperty Source
        $run = Invoke-TestProcess -FilePath $dotnetExecutable `
            -ArgumentList $arguments -WorkingDirectory $repo -ResultsDirectory $controllerDirectory `
            -TimeoutSeconds $hardLimit
        Write-Host ([IO.File]::ReadAllText((Join-Path $controllerDirectory 'controller.stdout.txt')))
        $stderr = [IO.File]::ReadAllText((Join-Path $controllerDirectory 'controller.stderr.txt'))
        if ($stderr) { Write-Host $stderr }
        if ($run.exitCode -ne 0) { throw "$name test run failed: $($run.outcome), exit code $($run.exitCode). See $results." }
    }
}
finally {
    $env:TONARINK_CLI_TEST_EXECUTABLE = $previousExecutable
    $env:TONARINK_CLI_TEST_RID = $previousRid
    $env:TONARINK_CLI_TEST_VERSION = $previousVersion
    $env:TONARINK_CLI_EXPECTED_SHA256 = $previousHash
    $env:TONARINK_APP_TEST_LAYOUT = $previousAppLayout
    $env:TONARINK_APP_EXPECTED_SHA256 = $previousAppHash
    Pop-Location
}
