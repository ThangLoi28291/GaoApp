param(
    [string]$ServiceName = "GaoApp.AutoInvoiceWorker",
    [string]$InstallDirectory = "C:\GaoApp\AutoInvoiceWorker"
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $InstallDirectory "GaoApp.AutoInvoiceWorker.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Không tìm thấy $exe. Hãy publish GaoApp.AutoInvoiceWorker vào $InstallDirectory trước."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

New-Service `
    -Name $ServiceName `
    -BinaryPathName ('"{0}"' -f $exe) `
    -DisplayName "GaoApp Auto Invoice Worker" `
    -Description "GaoApp worker phát hành hóa đơn tự động theo queue SQL." `
    -StartupType Automatic

Start-Service -Name $ServiceName
Get-Service -Name $ServiceName | Select-Object Name, Status, StartType
