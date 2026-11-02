#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$testRoot = Join-Path $repoRoot ('Logs/auto-invoice-credentials-20261010/sync-' + [Guid]::NewGuid().ToString('N'))
$workerRoot = Join-Path $testRoot 'worker'
$keysRoot = Join-Path $testRoot 'keys'
[IO.Directory]::CreateDirectory($workerRoot) | Out-Null
[IO.Directory]::CreateDirectory($keysRoot) | Out-Null
[IO.File]::WriteAllText((Join-Path $workerRoot 'GaoApp.AutoInvoiceWorker.exe'), 'synthetic-placeholder')
$pfxPath = Join-Path $testRoot 'test-only.pfx'
[IO.File]::WriteAllText($pfxPath, 'synthetic-placeholder')
$hostPath = Join-Path $testRoot 'test-only-host-environment.json'
$workerHostPath = Join-Path $testRoot 'private/worker.appsettings.Production.json'
@{
    DataProtection__KeysPath = $keysRoot
    DataProtection__RequirePortableKeys = 'true'
    DataProtection__CertificatePath = $pfxPath
    DataProtection__CertificatePassword = 'synthetic-pfx-password'
    ConnectionStrings__DefaultConnection = 'must-not-be-copied'
} | ConvertTo-Json | Set-Content -LiteralPath $hostPath
$original = '{"ConnectionStrings":{"DefaultConnection":"keep-worker-connection"},"DataProtection":{"KeysPath":"old"},"Other":{"Enabled":true}}'
foreach ($name in @('appsettings.json', 'appsettings.Development.json', 'appsettings.Production.json')) {
    [IO.File]::WriteAllText((Join-Path $workerRoot $name), $original)
}
# Stub the service inventory only. This test never queries/stops/starts a
# service and all writes are restricted to a synthetic Logs directory.
function Get-Service { param($Name, $ErrorAction) return $null }
$sync = Join-Path $PSScriptRoot 'Sync-GaoAppAutoInvoiceWorkerProtection.ps1'
& $sync -WorkerDirectory $workerRoot -HostEnvironmentPath $hostPath -WorkerHostSettingsPath $workerHostPath -WhatIf
if ((Get-Content -LiteralPath (Join-Path $workerRoot 'appsettings.json') -Raw) -ne $original) { throw 'WhatIf changed configuration.' }
if (Test-Path -LiteralPath (Split-Path $workerHostPath -Parent)) { throw 'WhatIf created a secret directory.' }
$output = (& $sync -WorkerDirectory $workerRoot -HostEnvironmentPath $hostPath -WorkerHostSettingsPath $workerHostPath | Out-String)
if ($output.Contains('synthetic-pfx-password')) { throw 'The synchronization output exposed a secret.' }
foreach ($name in @('appsettings.json', 'appsettings.Development.json', 'appsettings.Production.json')) {
    $path = Join-Path $workerRoot $name
    if ((Get-Content -LiteralPath $path -Raw) -ne $original) { throw 'Publish configuration was changed.' }
}
$updated = Get-Content -LiteralPath $workerHostPath -Raw | ConvertFrom-Json
if ($updated.DataProtection.KeysPath -ne $keysRoot -or $updated.DataProtection.CertificatePassword -ne 'synthetic-pfx-password') { throw 'External protection configuration was not synchronized.' }
if ($updated.ConnectionStrings.DefaultConnection -ne 'keep-worker-connection' -or !$updated.Other.Enabled) { throw 'Unrelated Worker configuration changed.' }
$privateAcl = Get-Acl -LiteralPath (Split-Path $workerHostPath -Parent)
if (!$privateAcl.AreAccessRulesProtected -or @($privateAcl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]) | Where-Object { $_.IdentityReference.Value -in @('S-1-1-0','S-1-5-11','S-1-5-32-545') }).Count) { throw 'New private directory inherited broad access.' }
$updated.ConnectionStrings.DefaultConnection = 'external-connection-survives-upgrade'
$updated | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $workerHostPath
$beforeSync = Get-Content -LiteralPath $workerHostPath -Raw
& $sync -WorkerDirectory $workerRoot -HostEnvironmentPath $hostPath -WorkerHostSettingsPath $workerHostPath | Out-Null
$backups = @(Get-ChildItem -LiteralPath (Split-Path $workerHostPath -Parent) -Filter 'worker.appsettings.Production.json.before-protection-sync-*.bak')
if ($backups.Count -ne 1 -or (Get-Content -LiteralPath $backups[0].FullName -Raw) -ne $beforeSync) { throw 'External configuration backup was not preserved.' }
if ((Get-Content -LiteralPath $workerHostPath -Raw | ConvertFrom-Json).ConnectionStrings.DefaultConnection -ne 'external-connection-survives-upgrade') { throw 'Upgrade replaced the private database connection.' }
function Get-Service { param($Name, $ErrorAction) return [pscustomobject]@{Status='Running'} }
try {
    & $sync -WorkerDirectory $workerRoot -HostEnvironmentPath $hostPath -WorkerHostSettingsPath $workerHostPath
    throw 'Running service was not rejected.'
} catch {
    if ($_.Exception.Message -notlike '*Stop the AutoInvoice Worker service*') { throw }
}
Write-Output 'PASS: preview, shared protection, secret redaction, unrelated settings, backups, running-service guard.'

# Web's PFX can be in appsettings/web.config rather than the pool. Verify
# effective configuration precedence before copying to a separate process.
. (Join-Path $PSScriptRoot 'WorkerProtection.Common.ps1')
$webRoot = Join-Path $testRoot 'web'
[IO.Directory]::CreateDirectory($webRoot) | Out-Null
@{DataProtection=@{KeysPath='relative-keys'; CertificatePath=$pfxPath; CertificatePassword='base-password'}} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $webRoot 'appsettings.json')
@{DataProtection=@{CertificatePassword='production-password'; RequirePortableKeys=$true}} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $webRoot 'appsettings.Production.json')
@{DataProtection=@{CertificatePassword='must-not-use-development'}} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $webRoot 'appsettings.Development.json')
$effective = Get-GaoWebProtectionSettings -WebDirectory $webRoot -PoolEnvironment @{
    DOTNET_ENVIRONMENT='Production'; ASPNETCORE_ENVIRONMENT='Development'
}
if ($effective.CertificatePassword -ne 'production-password' -or $effective.KeysPath -ne (Join-Path $webRoot 'relative-keys')) {
    throw 'Web JSON/environment/relative-key-path resolution failed.'
}
[IO.File]::WriteAllText((Join-Path $webRoot 'web.config'), '<configuration><location path="."><system.webServer><aspNetCore><environmentVariables><environmentVariable name="DataProtection__CertificatePassword" value="web-config-password&amp;value" /></environmentVariables></aspNetCore></system.webServer></location></configuration>')
$effective = Get-GaoWebProtectionSettings -WebDirectory $webRoot -PoolEnvironment @{
    DOTNET_ENVIRONMENT='Production'; DataProtection__CertificatePassword='pool-password'
} -MachineEnvironment @{DataProtection__CertificatePassword='machine-password'}
if ($effective.CertificatePassword -ne 'web-config-password&value' -or $effective.CertificatePath -ne $pfxPath) {
    throw 'web.config override or JSON PFX fallback failed.'
}
Write-Output 'PASS: effective Web JSON, environment precedence, relative keys, pool/machine/web.config overrides.'

# Permission repair targets the account and executable of the existing
# service, even when key files have inheritance disabled.
$keyFile = Join-Path $keysRoot 'key-synthetic.xml'
[IO.File]::WriteAllText($keyFile, 'synthetic-key-placeholder')
$keyAcl = Get-Acl -LiteralPath $keyFile
$keyAcl.SetAccessRuleProtection($true, $true)
Set-Acl -LiteralPath $keyFile -AclObject $keyAcl
function Get-Service { param($Name, $ErrorAction) return [pscustomobject]@{Status='Stopped'} }
$testAccount = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$testExecutable = Join-Path $workerRoot 'GaoApp.AutoInvoiceWorker.exe'
function Get-CimInstance {
    param($ClassName)
    return [pscustomobject]@{Name='GaoApp.AutoInvoiceWorker'; StartName=$testAccount; PathName=('"' + $testExecutable + '" --environment Production')}
}
$beforeAcl = (Get-Acl -LiteralPath $keysRoot).Sddl
& $sync -HostEnvironmentPath $hostPath -WorkerHostSettingsPath $workerHostPath -RepairFilePermissions -WhatIf
if ((Get-Acl -LiteralPath $keysRoot).Sddl -ne $beforeAcl) { throw 'WhatIf changed ACL.' }
$result = (& $sync -WorkerDirectory $workerRoot -HostEnvironmentPath $hostPath -WorkerHostSettingsPath $workerHostPath -RepairFilePermissions | Out-String)
if ($result.Contains('synthetic-pfx-password')) { throw 'Permission repair exposed a secret.' }
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach ($path in @($keysRoot, $keyFile, $pfxPath, $workerHostPath)) {
    $rules = (Get-Acl -LiteralPath $path).GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier])
    if (!@($rules | Where-Object { $_.IdentityReference.Value -eq $sid -and $_.AccessControlType -eq 'Allow' }).Count) { throw 'Worker account permission was not added.' }
}
if (@(Get-ChildItem -LiteralPath (Split-Path $workerHostPath -Parent) -Filter 'protection-acl*.json').Count -ne 1) { throw 'Original ACL snapshot was not preserved.' }
$testExecutable = Join-Path $testRoot 'different/GaoApp.AutoInvoiceWorker.exe'
try {
    & $sync -WorkerDirectory $workerRoot -HostEnvironmentPath $hostPath -WorkerHostSettingsPath $workerHostPath -RepairFilePermissions
    throw 'Wrong service executable was accepted.'
} catch {
    if ($_.Exception.Message -notlike '*does not match the installed service executable*') { throw }
}
Write-Output 'PASS: identity/executable validation, ACL preview, original ACL snapshot, service-only ACL additions, protected key files.'
