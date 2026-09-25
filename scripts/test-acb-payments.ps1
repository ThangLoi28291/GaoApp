[CmdletBinding()]
param([string]$SqlServer = '')

$ErrorActionPreference = 'Stop'
$acbRoot = Split-Path -Parent $PSScriptRoot
$acbPreviousServer = $env:GAOAPP_ACB_SQL_TEST_SERVER
$acbFilter = '(FullyQualifiedName~GaoApp.Tests.Payments|FullyQualifiedName~PosCheckoutPaymentUiContractTests|FullyQualifiedName~GlobalExceptionMiddlewareTests|FullyQualifiedName~TenantResolutionCancellationTests|FullyQualifiedName~ProxyTrustTests)'
if ([string]::IsNullOrWhiteSpace($SqlServer)) {
    $acbFilter += '&FullyQualifiedName!~AcbSqlLockTests'
    Write-Host 'Chay test ACB voi ngan hang gia lap. Them -SqlServer de kiem tra khoa dong thoi SQL Server.'
} else {
    $env:GAOAPP_ACB_SQL_TEST_SERVER = $SqlServer
    Write-Host 'Chay them test khoa SQL; chi dung khoa tam trong master qua Windows Authentication.'
}
Push-Location $acbRoot
try {
    dotnet test GaoApp.Tests/GaoApp.Tests.csproj --no-restore --filter $acbFilter --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=acb-tests.trx' --results-directory TestResults/acb
    if ($LASTEXITCODE -ne 0) { throw 'Kiem thu .NET ACB chua dat. Xem TestResults/acb/acb-tests.trx.' }
    node --test GaoApp.Tests/Ui/pos-acb-realtime.test.cjs GaoApp.Tests/Ui/pos-qr-history.test.cjs GaoApp.Tests/Ui/pos-qr-installments.test.cjs GaoApp.Tests/Ui/acb-confirmation-lookup.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Kiem thu POS realtime chua dat.' }
    Write-Host 'Tat ca kiem thu da chon deu dat. Bai test thao tac: docs/acb-acceptance-tests.md'
} finally {
    Pop-Location
    $env:GAOAPP_ACB_SQL_TEST_SERVER = $acbPreviousServer
}
