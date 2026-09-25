param([switch]$SkipBuild, [ValidateRange(20, 1000)][int]$IterationsPerClient = 20)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
$previousLoadMode = $env:GAOAPP_RUN_LOCAL_LOAD
$previousIterations = $env:GAOAPP_LOAD_ITERATIONS
$previousProfileOnly = $env:GAOAPP_LOAD_PROFILE_ONLY
try {
    if (-not $SkipBuild) {
        dotnet build GaoApp.sln --configuration Release --artifacts-path Logs/security-phase3-release --ignore-failed-sources -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }
    $testAssembly = Join-Path $repositoryRoot 'Logs/security-phase3-release/bin/GaoApp.Tests/release/GaoApp.Tests.dll'
    if (-not (Test-Path -LiteralPath $testAssembly)) { throw 'Build the phase 3 test artifact first.' }
    $env:GAOAPP_RUN_LOCAL_LOAD = '1'
    $env:GAOAPP_LOAD_ITERATIONS = $IterationsPerClient.ToString()
    $env:GAOAPP_LOAD_PROFILE_ONLY = $null
    dotnet vstest $testAssembly '/TestCaseFilter:FullyQualifiedName~LocalReportLoadTests' '/logger:trx;LogFileName=phase3-local-load.trx' /ResultsDirectory:TestResults/security-phase3
    if ($LASTEXITCODE -ne 0) { throw 'Local load verification failed. Inspect the TRX and any partial JSON results.' }
    Write-Output 'Results: TestResults/security-phase3/local-load-results.json'
    Write-Output 'Scope: isolated loopback services + LocalDB. This is not a production-host benchmark.'
}
finally {
    $env:GAOAPP_RUN_LOCAL_LOAD = $previousLoadMode
    $env:GAOAPP_LOAD_ITERATIONS = $previousIterations
    $env:GAOAPP_LOAD_PROFILE_ONLY = $previousProfileOnly
    Pop-Location
}
