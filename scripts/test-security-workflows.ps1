param([switch]$SkipBuild, [switch]$IncludeRegression)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
try {
    if (-not $SkipBuild) {
        dotnet build GaoApp.sln --configuration Release --artifacts-path Logs/security-phase4-release --ignore-failed-sources -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }
    $testAssembly = Join-Path $repositoryRoot 'Logs/security-phase4-release/bin/GaoApp.Tests/release/GaoApp.Tests.dll'
    if (-not (Test-Path -LiteralPath $testAssembly)) { throw 'Build the complete solution artifact first.' }
    # These fixtures own random LocalDB databases and loopback Web processes. No app DB or bank endpoint is used.
    $filter = 'FullyQualifiedName~FullWorkflowSqlServerTests|FullyQualifiedName~PosRealtimeBatchSqlServerTests|FullyQualifiedName~SessionRevocationSqlServerTests|FullyQualifiedName~InventoryMovementSqlServerConcurrencyTests|FullyQualifiedName~SaleCostReversalSqlServerTests|FullyQualifiedName~PaymentCollectionSqlServerTests|FullyQualifiedName~InventoryPostingMigrationTests|FullyQualifiedName~Current_model_and_migration_snapshot_have_no_differences|FullyQualifiedName~PosPrimeResponsiveUiContractTests|FullyQualifiedName~PosCheckoutPaymentUiContractTests'
    if ($IncludeRegression) {
        $filter += '|FullyQualifiedName~Security|FullyQualifiedName~Tenant|FullyQualifiedName~GaoApp.Tests.Data.|FullyQualifiedName~Observability|FullyQualifiedName~Payments|FullyQualifiedName~GaoApp.Tests.Ui.Pos|FullyQualifiedName~StartupOrderingContractTests|FullyQualifiedName~POSShifts'
    }
    $resultName = if ($IncludeRegression) { 'phase5-script-regression.trx' } else { 'phase5-script-workflows.trx' }
    dotnet vstest $testAssembly "/TestCaseFilter:$filter" "/logger:trx;LogFileName=$resultName" /ResultsDirectory:TestResults/security-phase5
    if ($LASTEXITCODE -ne 0) { throw 'Security/workflow tests failed. Inspect TestResults/security-phase5 and the Web fixture logs.' }
    node --test GaoApp.Tests/Ui/pos-collection-idempotency.test.cjs GaoApp.Tests/Ui/pos-qr-installments.test.cjs GaoApp.Tests/Ui/pos-qr-history.test.cjs GaoApp.Tests/Ui/pos-acb-realtime.test.cjs GaoApp.Tests/Ui/acb-confirmation-lookup.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'POS JavaScript tests failed.' }
    Write-Output 'Passed. Scope: local Web/SQL correctness and security; host load and browser UAT need separate validation.'
}
finally { Pop-Location }
