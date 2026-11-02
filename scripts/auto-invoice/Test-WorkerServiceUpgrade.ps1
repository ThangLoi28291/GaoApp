#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$testRoot = Join-Path $repoRoot ('Logs/auto-invoice-credentials-20261010/service-' + [Guid]::NewGuid().ToString('N'))
$release = Join-Path $testRoot 'new-release'
[IO.Directory]::CreateDirectory($release) | Out-Null
[IO.File]::WriteAllText((Join-Path $release 'GaoApp.AutoInvoiceWorker.exe'), 'synthetic-placeholder')
$privatePath = Join-Path $testRoot 'private-custom.json'
[IO.File]::WriteAllText($privatePath, '{"ConnectionStrings":{"DefaultConnection":"synthetic-connection"},"DataProtection":{"KeysPath":"synthetic-keys"}}')
$installer = Join-Path $PSScriptRoot 'Install-GaoAppAutoInvoiceWorker.ps1'
$global:gaoWorkerUpgradeTestService = [pscustomobject]@{
    Name='GaoApp.AutoInvoiceWorker'; Status='Running'; StartName='DOMAIN\ExistingWorker'
    PathName=('"' + (Join-Path $testRoot 'old/GaoApp.AutoInvoiceWorker.exe') + '" --environment Production --host-settings "' + $privatePath + '"')
    StartType='Automatic'; Recovery='retain-existing-recovery'
}
$global:gaoWorkerUpgradeTestMutations = @()
# All service commands are stubs. No real Windows service is queried or modified.
function Get-Service { param($Name, $ErrorAction) return $global:gaoWorkerUpgradeTestService }
function Get-CimInstance { param($ClassName) return $global:gaoWorkerUpgradeTestService }
function Stop-Service { param($Name, $ErrorAction) $global:gaoWorkerUpgradeTestMutations += 'stop'; $global:gaoWorkerUpgradeTestService.Status = 'Stopped' }
function Start-Service { param($Name) $global:gaoWorkerUpgradeTestMutations += 'start'; $global:gaoWorkerUpgradeTestService.Status = 'Running' }
function Invoke-CimMethod {
    param($InputObject, $MethodName, $Arguments)
    if ($MethodName -ne 'Change' -or $Arguments.Count -ne 1 -or !$Arguments.ContainsKey('PathName')) { throw 'Upgrade changed settings other than PathName.' }
    $global:gaoWorkerUpgradeTestMutations += 'change-path'
    $InputObject.PathName = $Arguments.PathName
    return @{ReturnValue=0}
}
function New-Service {
    param($Name, $BinaryPathName, $StartupType, $DisplayName, $Description)
    $global:gaoWorkerUpgradeTestMutations += 'create'
    $global:gaoWorkerUpgradeTestService = [pscustomobject]@{Name=$Name; Status='Stopped'; PathName=$BinaryPathName; StartType=$StartupType; StartName='LocalSystem'}
}
function sc.exe {
    $global:gaoWorkerUpgradeTestMutations += ('sc ' + ($args -join ' '))
    $global:gaoWorkerUpgradeTestService.StartName = $args[-1]
    $global:LASTEXITCODE = 0
}

$before = $global:gaoWorkerUpgradeTestService.PathName
& $installer -InstallDirectory $release -WhatIf | Out-Null
if ($global:gaoWorkerUpgradeTestMutations.Count -or $global:gaoWorkerUpgradeTestService.PathName -ne $before) { throw 'WhatIf modified the service.' }
$output = (& $installer -InstallDirectory $release | Out-String)
if ($output.Contains('synthetic-connection')) { throw 'Installer exposed a database secret.' }
if (($global:gaoWorkerUpgradeTestMutations -join ',') -ne 'stop,change-path') { throw 'Upgrade recreated or started the service unexpectedly.' }
if ($global:gaoWorkerUpgradeTestService.StartName -ne 'DOMAIN\ExistingWorker' -or $global:gaoWorkerUpgradeTestService.Recovery -ne 'retain-existing-recovery' -or $global:gaoWorkerUpgradeTestService.StartType -ne 'Automatic') { throw 'Existing service identity/recovery/startup changed.' }
if (!$global:gaoWorkerUpgradeTestService.PathName.Contains('--environment Production') -or !$global:gaoWorkerUpgradeTestService.PathName.Contains('--host-settings "' + $privatePath + '"') -or !$global:gaoWorkerUpgradeTestService.PathName.Contains($release)) { throw 'Upgrade did not retain Production/custom host config.' }
Write-Output 'PASS: upgrade preview, existing service identity/startup/recovery, custom external settings, no implicit queue start.'

$global:gaoWorkerUpgradeTestMutations = @()
try {
    & $installer -InstallDirectory $release -HostSettingsPath (Join-Path $testRoot 'missing.json')
    throw 'Missing host config was accepted.'
} catch { if ($_.Exception.Message -notlike '*Create the private Worker host settings file*') { throw } }
if ($global:gaoWorkerUpgradeTestMutations.Count) { throw 'Validation failure changed a service.' }
$global:gaoWorkerUpgradeTestService = $null
& $installer -InstallDirectory $release -HostSettingsPath $privatePath -WhatIf | Out-Null
if ($global:gaoWorkerUpgradeTestMutations.Count) { throw 'First-install preview changed a service.' }
& $installer -InstallDirectory $release -HostSettingsPath $privatePath | Out-Null
if ($global:gaoWorkerUpgradeTestService.Status -ne 'Stopped' -or $global:gaoWorkerUpgradeTestService.StartType -ne 'Manual' -or $global:gaoWorkerUpgradeTestService.StartName -ne 'NT SERVICE\GaoApp.AutoInvoiceWorker') { throw 'New service identity/startup is incorrect.' }
if (@($global:gaoWorkerUpgradeTestMutations | Where-Object { $_ -like '*delete*' -or $_ -eq 'start' }).Count) { throw 'Installer deleted or implicitly started a service.' }
Write-Output 'PASS: validation before mutation, first-install preview, virtual account, stopped/manual first install.'
