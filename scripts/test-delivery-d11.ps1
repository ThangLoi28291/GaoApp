param(
    [ValidateSet('Focused', 'FullWeb')][string]$Scope = 'Focused',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$ArtifactsPath = 'Logs/delivery-roadmap/D11/artifacts'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repo $ArtifactsPath))
$logsRoot = [IO.Path]::GetFullPath((Join-Path $repo 'Logs')) + [IO.Path]::DirectorySeparatorChar
if (-not $artifactRoot.StartsWith($logsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ArtifactsPath must be inside this repository Logs directory.'
}
$runId = 'run-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$output = Join-Path $repo ('Logs/delivery-roadmap/D11/' + $runId)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$summary = [ordered]@{ scope = $Scope; configuration = $Configuration; startedAt = (Get-Date).ToString('o'); outcome = 'INCOMPLETE';
    backend = $null; uiExitCode = $null; sourceUnchanged = $false;
    qualification = 'Local automated checks only. Physical UAT, sustained load, review and CI are separate gates.' }

function Get-CandidateFiles {
    # Include the dirty working tree, not just HEAD. Generated test output is outside these roots.
    $paths = @('GaoApp.Domain', 'GaoApp.Application', 'GaoApp.Infrastructure', 'GaoApp.Web',
        'GaoApp.Tests', 'GaoApp.Tests.Browser', 'GaoApp.Migrator', 'eng/deployment', 'scripts', 'docs/development',
        'docs/delivery-security-delta.sql', 'Directory.Build.props', 'Directory.Build.targets',
        'Directory.Packages.props', 'GaoApp.sln', 'global.json', 'NuGet.Config', 'nuget.config')
    $files = @(& git ls-files --cached --others --exclude-standard -- @paths | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Cannot identify candidate source files.' }
    @($files | Where-Object { Test-Path -LiteralPath (Join-Path $repo $_) -PathType Leaf } | ForEach-Object {
        [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash }
    })
}

Push-Location -LiteralPath $repo
try {
    $before = Get-CandidateFiles | ConvertTo-Json -Depth 4 -Compress
    [IO.File]::WriteAllText((Join-Path $output 'source-before.json'), $before)
    # Builds the Web paired with the tests. Fixtures own GUID-named local SQL databases
    # and random loopback ports; this script never starts/stops the user's port 7051 app.
    & dotnet build GaoApp.Tests/GaoApp.Tests.csproj -c $Configuration --artifacts-path $artifactRoot -m:1 -nr:false `
        -p:BuildInParallel=false -p:UseSharedCompilation=false --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'D11 build failed.' }
    $testArgs = @('test', 'GaoApp.Tests/GaoApp.Tests.csproj', '-c', $Configuration, '--artifacts-path', $artifactRoot,
        '--no-build', '--no-restore', '-m:1', '-nr:false', '--settings', 'GaoApp.Tests/test.local.runsettings',
        '--logger', 'trx;LogFileName=backend.trx', '--results-directory', $output, '--verbosity', 'minimal')
    if ($Scope -eq 'Focused') {
        $filter = 'Category=DeliveryD11|FullyQualifiedName~DeliveryD08HttpTests.H04_|FullyQualifiedName~DeliveryD08HttpTests.H06_|FullyQualifiedName~DeliveryD08HttpTests.H07_|FullyQualifiedName~DeliveryD08HttpTests.H08_|FullyQualifiedName~DeliveryD08SqlServerTests.S01_|FullyQualifiedName~DeliveryD10MonitorTests'
        $testArgs += @('--filter', $filter)
    }
    & dotnet @testArgs
    $backendExit = $LASTEXITCODE
    $trxPath = Join-Path $output 'backend.trx'
    if (-not (Test-Path -LiteralPath $trxPath)) { throw 'No backend TRX was produced.' }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counters) { throw 'TRX has no result counters.' }
    $summary.backend = [ordered]@{ total = [int]$counters.GetAttribute('total'); passed = [int]$counters.GetAttribute('passed');
        failed = [int]$counters.GetAttribute('failed'); notExecuted = [int]$counters.GetAttribute('notExecuted'); exitCode = $backendExit }
    if ($backendExit -ne 0 -or $summary.backend.total -eq 0 -or $summary.backend.failed -ne 0 -or
        $summary.backend.passed -ne $summary.backend.total) { throw 'Backend did not pass every selected test. Inspect backend.trx.' }
    if ($Scope -eq 'Focused' -and $summary.backend.total -lt 15) { throw 'Focused suite discovery is incomplete (expected at least 15 cases).' }
    $jsFiles = @('GaoApp.Tests/Ui/delivery-settlement.test.cjs', 'GaoApp.Tests/Ui/pos-delivery-unified-renderer.test.cjs',
        'GaoApp.Tests/Ui/store-monitor-delivery-retention.test.cjs', 'GaoApp.Tests/Ui/store-monitor-delivery-renderer.test.cjs',
        'GaoApp.Tests/Ui/store-monitor-fit-renderer.test.cjs')
    if ($Scope -eq 'FullWeb') {
        $jsFiles = @(Get-ChildItem -LiteralPath (Join-Path $repo 'GaoApp.Tests/Ui') -Filter '*.test.cjs' -File |
            Sort-Object Name | ForEach-Object { $_.FullName })
    }
    & node --test --test-reporter=tap @jsFiles 2>&1 | Tee-Object -FilePath (Join-Path $output 'ui.tap')
    $summary.uiExitCode = $LASTEXITCODE
    if ($summary.uiExitCode -ne 0) { throw 'UI checks failed. Inspect ui.tap.' }
    $after = Get-CandidateFiles | ConvertTo-Json -Depth 4 -Compress
    [IO.File]::WriteAllText((Join-Path $output 'source-after.json'), $after)
    $summary.sourceUnchanged = $before -ceq $after
    if (-not $summary.sourceUnchanged) { throw 'Source changed during this run; results cannot qualify the final candidate.' }
    $summary.outcome = 'AUTOMATED_PASS'
    Write-Output "Automated $Scope checks passed. Evidence: $output"
}
catch {
    $summary.outcome = 'FAIL'; $summary.error = $_.Exception.Message
    throw
}
finally {
    $summary.finishedAt = (Get-Date).ToString('o')
    [IO.File]::WriteAllText((Join-Path $output 'summary.json'), ($summary | ConvertTo-Json -Depth 6))
    Pop-Location
}
