#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string] $InstallDirectory,
    [System.Management.Automation.PSCredential] $Credential
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($InstallDirectory) -and
        -not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        $InstallDirectory = Split-Path -Path $PSCommandPath -Parent
    }
}
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    throw 'Khong xac dinh duoc thu muc cai dat. Hay truyen -InstallDirectory voi duong dan day du den thu muc LabelPrintServer.'
}
$serviceName = 'GaoApp.LabelPrintServer'
$installRoot = (Resolve-Path -LiteralPath $InstallDirectory).Path
$executable = Join-Path $installRoot 'GaoApp.LabelPrintServer.exe'
$settingsFile = Join-Path $installRoot 'appsettings.json'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Khong thay GaoApp.LabelPrintServer.exe. Hay chay script trong thu muc publish tren server.'
}
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw 'Dich vu da ton tai. Kiem tra cau hinh va duong dan dich vu hien co; script khong ghi de.'
}
$settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
$configuredStoreId = 0
if ([string]::IsNullOrWhiteSpace($settings.ConnectionStrings.DefaultConnection) -or
    -not [int]::TryParse([string]$settings.LabelPrinting.StoreId, [ref]$configuredStoreId) -or
    $configuredStoreId -le 0) {
    throw 'Cau hinh appsettings.json: ConnectionStrings.DefaultConnection cung database voi Web va LabelPrinting.StoreId dung cua hang truoc khi cai.'
}
if ((Get-Service -Name Spooler).Status -ne 'Running') {
    throw 'Windows Print Spooler chua chay. Kiem tra dich vu Spooler tren server.'
}
if ($null -eq $Credential) {
    $Credential = Get-Credential -Message 'Tai khoan Windows chay dich vu: co quyen doc thu muc, Print va truy cap SQL cua GaoApp'
}
if ($null -eq $Credential) { throw 'Chua chon tai khoan chay dich vu.' }

New-Service -Name $serviceName -DisplayName 'GaoApp - In tem san pham' `
    -Description 'Nhan lenh in tem tu database GaoApp va gui den may in Windows tren server.' `
    -BinaryPathName ('"' + $executable + '"') -StartupType Automatic `
    -DependsOn Spooler -Credential $Credential | Out-Null

Write-Host 'Da cai dich vu tu khoi dong cung Windows. Chua bat dau xu ly hang doi.'
Write-Host ('StoreId da cau hinh: ' + $configuredStoreId)
Write-Host 'Sau khi kiem tra may in va cac lenh dang cho, chay:'
Write-Host "Start-Service -Name '$serviceName'"
Write-Host "Get-Service -Name '$serviceName'"
