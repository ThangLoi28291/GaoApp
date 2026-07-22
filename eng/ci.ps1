[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [int]$MinimumTestCount = 229,
    [string]$ArtifactsDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solutionPath = Join-Path $repoRoot "GaoApp.sln"
$testProjectPath = Join-Path $repoRoot "GaoApp.Tests\GaoApp.Tests.csproj"

if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory))
{
    $ArtifactsDirectory = Join-Path $repoRoot "TestResults\CI"
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactsDirectory))
{
    $ArtifactsDirectory = Join-Path $repoRoot $ArtifactsDirectory
}

if (Test-Path $ArtifactsDirectory)
{
    Remove-Item $ArtifactsDirectory -Recurse -Force
}

$testResultsDirectory = Join-Path $ArtifactsDirectory "TestResults"

New-Item -ItemType Directory -Force $ArtifactsDirectory | Out-Null
New-Item -ItemType Directory -Force $testResultsDirectory | Out-Null

function Invoke-DotNetCommand
{
    param(
        [Parameter(Mandatory)]
        [string]$StepName,

        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$LogFile
    )

    Write-Host ""
    Write-Host "=================================================="
    Write-Host $StepName
    Write-Host "dotnet $($Arguments -join ' ')"
    Write-Host "=================================================="

    Push-Location $repoRoot

    try
    {
        & dotnet @Arguments 2>&1 |
            Tee-Object -FilePath $LogFile

        $exitCode = $LASTEXITCODE
    }
    finally
    {
        Pop-Location
    }

    if ($exitCode -ne 0)
    {
        throw "$StepName failed with exit code $exitCode. See: $LogFile"
    }
}

Write-Host "Repository: $repoRoot"
Write-Host "Configuration: $Configuration"
Write-Host "Minimum test count: $MinimumTestCount"
Write-Host "Artifacts: $ArtifactsDirectory"

$sdkLog = Join-Path $ArtifactsDirectory "dotnet-info.txt"

Push-Location $repoRoot

try
{
    & dotnet --info 2>&1 |
        Tee-Object -FilePath $sdkLog

    $sdkExitCode = $LASTEXITCODE
}
finally
{
    Pop-Location
}

if ($sdkExitCode -ne 0)
{
    throw "dotnet --info failed with exit code $sdkExitCode."
}

Invoke-DotNetCommand `
    -StepName "Restore solution and audit direct/transitive dependencies" `
    -Arguments @(
        "restore",
        $solutionPath,
        "--nologo",
        "-p:NuGetAudit=true",
        "-p:NuGetAuditMode=all",
        "-p:NuGetAuditLevel=low",
        "-p:TreatWarningsAsErrors=true"
    ) `
    -LogFile (Join-Path $ArtifactsDirectory "restore-audit.txt")

Invoke-DotNetCommand `
    -StepName "Build solution with warnings treated as errors" `
    -Arguments @(
        "build",
        $solutionPath,
        "-c",
        $Configuration,
        "--no-restore",
        "-t:Rebuild",
        "--nologo",
        "-warnaserror",
        "-p:ContinuousIntegrationBuild=true",
        "-bl:$ArtifactsDirectory\build.binlog"
    ) `
    -LogFile (Join-Path $ArtifactsDirectory "build.txt")

Invoke-DotNetCommand `
    -StepName "Run full regression test suite" `
    -Arguments @(
        "test",
        $testProjectPath,
        "-c",
        $Configuration,
        "--no-build",
        "--nologo",
        "--results-directory",
        $testResultsDirectory,
        "--logger",
        "trx;LogFileName=ci-tests.trx",
        "--logger",
        "console;verbosity=normal"
    ) `
    -LogFile (Join-Path $ArtifactsDirectory "test.txt")

$trxFile = Get-ChildItem `
    -Path $testResultsDirectory `
    -Filter "ci-tests.trx" `
    -File `
    -Recurse |
    Select-Object -First 1

if ($null -eq $trxFile)
{
    throw "Test command completed without producing ci-tests.trx."
}

[xml]$trx = Get-Content $trxFile.FullName -Raw
$counters = $trx.SelectSingleNode("//*[local-name()='Counters']")

if ($null -eq $counters)
{
    throw "Could not read test counters from $($trxFile.FullName)."
}

$total = [int]$counters.GetAttribute("total")
$executed = [int]$counters.GetAttribute("executed")
$passed = [int]$counters.GetAttribute("passed")
$failed = [int]$counters.GetAttribute("failed")
$notExecuted = [int]$counters.GetAttribute("notExecuted")

$summary = @(
    "Total=$total"
    "Executed=$executed"
    "Passed=$passed"
    "Failed=$failed"
    "NotExecuted=$notExecuted"
    "MinimumExpected=$MinimumTestCount"
)

$summaryPath = Join-Path $ArtifactsDirectory "test-summary.txt"
$summary | Set-Content $summaryPath

Write-Host ""
Write-Host "===== TEST SUMMARY ====="
$summary | ForEach-Object { Write-Host $_ }

if ($total -lt $MinimumTestCount)
{
    throw "Test count $total is below required baseline $MinimumTestCount."
}

if ($failed -ne 0)
{
    throw "Test run contains $failed failed test(s)."
}

if ($notExecuted -ne 0)
{
    throw "Test run contains $notExecuted skipped/not-executed test(s)."
}

if ($executed -ne $total -or $passed -ne $total)
{
    throw "Test counters are inconsistent: total=$total, executed=$executed, passed=$passed."
}

Write-Host ""
Write-Host "CI gate PASS."
