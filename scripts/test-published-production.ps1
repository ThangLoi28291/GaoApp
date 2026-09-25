[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [string]$ArtifactsPath = 'Logs/security-phase4-release',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$releaseRoot = (Resolve-Path -LiteralPath $ReleasePath).Path
& (Join-Path $PSScriptRoot 'test-release-package.ps1') -ReleasePath $releaseRoot
$previousRelease = $env:GAOAPP_TEST_RELEASE
Push-Location $repoRoot
try {
    if (!$SkipBuild) {
        & dotnet build GaoApp.sln --configuration Release --artifacts-path $ArtifactsPath --no-restore -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'Smoke test build failed. Restore the solution in ArtifactsPath first.' }
    }
    $env:GAOAPP_TEST_RELEASE = $releaseRoot
    $testAssembly = Join-Path $ArtifactsPath 'bin/GaoApp.Tests/release/GaoApp.Tests.dll'
    & dotnet vstest $testAssembly '--TestCaseFilter:FullyQualifiedName~PublishedProductionSmokeTests' '--Logger:trx;LogFileName=published-production.trx' '--ResultsDirectory:TestResults/security-phase6'
    if ($LASTEXITCODE -ne 0) { throw 'Published Production smoke tests failed.' }
    & (Join-Path $PSScriptRoot 'test-release-package.ps1') -ReleasePath $releaseRoot
}
finally {
    $env:GAOAPP_TEST_RELEASE = $previousRelease
    Pop-Location
}
