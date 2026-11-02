#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'HostFile')]
param(
    [string]$WorkerDirectory,
    # Use the same private host-environment JSON used to configure the Web
    # pool. Only DataProtection values are read; no SQL/provider secret is copied.
    [Parameter(Mandatory, ParameterSetName = 'HostFile')][string]$HostEnvironmentPath,
    [Parameter(Mandatory, ParameterSetName = 'IIS')][string]$AppPoolName,
    [Parameter(ParameterSetName = 'IIS')][string]$WebDirectory,
    [string]$ServiceName = 'GaoApp.AutoInvoiceWorker',
    [string]$WorkerHostSettingsPath = 'C:\GaoAppData\DeployConfig\worker.appsettings.Production.json',
    [switch]$RepairFilePermissions
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WorkerProtection.Common.ps1')
if ([string]::IsNullOrWhiteSpace($WorkerDirectory)) {
    $registration = Get-GaoWorkerServiceRegistration -ServiceName $ServiceName
    $WorkerDirectory = [IO.Path]::GetDirectoryName($registration.Executable)
}
$workerRoot = (Resolve-Path -LiteralPath $WorkerDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $workerRoot 'GaoApp.AutoInvoiceWorker.exe') -PathType Leaf)) {
    throw 'WorkerDirectory must contain the published GaoApp.AutoInvoiceWorker.exe.'
}
if (Test-Path -LiteralPath (Join-Path $workerRoot 'wwwroot')) {
    throw 'Worker configuration containing secrets must remain outside the IIS Web directory.'
}
$protection = if ($PSCmdlet.ParameterSetName -eq 'IIS') {
    Add-Type -Path (Join-Path $env:windir 'System32/inetsrv/Microsoft.Web.Administration.dll')
    $manager = [Microsoft.Web.Administration.ServerManager]::new()
    try {
        $pools = $manager.GetApplicationHostConfiguration().GetSection('system.applicationHost/applicationPools').GetCollection()
        $pool = @($pools | Where-Object { $_.GetAttributeValue('name') -eq $AppPoolName })
        if ($pool.Count -ne 1) { throw 'The specified Web application pool was not found.' }
        $values = @{}
        foreach ($entry in $pool[0].GetCollection('environmentVariables')) {
            $name = [string]$entry.GetAttributeValue('name')
            if ($name -like 'DataProtection__*' -or $name -in @('ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT')) {
                $values[$name] = $entry.GetAttributeValue('value')
            }
        }
        $webRoots = @(foreach ($site in $manager.Sites) {
            foreach ($application in $site.Applications) {
                if ($application.ApplicationPoolName -eq $AppPoolName) {
                    [Environment]::ExpandEnvironmentVariables($application.VirtualDirectories['/'].PhysicalPath)
                }
            }
        })
        if ([string]::IsNullOrWhiteSpace($WebDirectory)) {
            if ($webRoots.Count -ne 1) { throw 'The pool has multiple/no Web applications. Specify WebDirectory explicitly.' }
            $WebDirectory = $webRoots[0]
        }
        $resolvedWeb = (Resolve-Path -LiteralPath $WebDirectory).Path
        if (!@($webRoots | Where-Object { [string]::Equals([IO.Path]::GetFullPath($_), $resolvedWeb, [StringComparison]::OrdinalIgnoreCase) }).Count) {
            throw 'WebDirectory does not belong to the specified application pool.'
        }
        $machineValues = @{}
        foreach ($name in @('DataProtection__KeysPath','DataProtection__RequirePortableKeys',
                'DataProtection__CertificatePath','DataProtection__CertificatePassword',
                'ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT')) {
            $value = [Environment]::GetEnvironmentVariable($name, 'Machine')
            if ($null -ne $value) { $machineValues[$name] = $value }
        }
        Get-GaoWebProtectionSettings -WebDirectory $resolvedWeb -PoolEnvironment $values -MachineEnvironment $machineValues
    } finally { $manager.Dispose() }
} else {
    $hostValues = Get-Content -LiteralPath $HostEnvironmentPath -Raw | ConvertFrom-Json
    $settings = [ordered]@{ KeysPath=''; RequirePortableKeys=$false; CertificatePath=''; CertificatePassword='' }
    foreach ($name in @($settings.Keys)) {
        $property = $hostValues.PSObject.Properties['DataProtection__' + $name]
        if ($null -ne $property) { $settings[$name] = $property.Value }
    }
    $settings
}
if ([string]::IsNullOrWhiteSpace($protection.KeysPath) -or
    ![IO.Path]::IsPathFullyQualified($protection.KeysPath) -or
    !(Test-Path -LiteralPath $protection.KeysPath -PathType Container)) {
    throw 'The Web host must specify an existing absolute DataProtection__KeysPath.'
}
if (![string]::IsNullOrWhiteSpace($protection.CertificatePath)) {
    if (![IO.Path]::IsPathFullyQualified($protection.CertificatePath) -or
        !(Test-Path -LiteralPath $protection.CertificatePath -PathType Leaf)) {
        throw 'The existing Web protection PFX must be accessible on this Worker machine.'
    }
} elseif ([string]$protection.RequirePortableKeys -eq 'true') {
    throw 'Portable mode requires the original Web PFX. Do not create a replacement certificate.'
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service -and $service.Status -ne 'Stopped' -and !$WhatIfPreference) {
    throw 'Stop the AutoInvoice Worker service before synchronizing its protection settings.'
}
$identity = if ($RepairFilePermissions) { Get-GaoWorkerServiceIdentity -ServiceName $ServiceName -WorkerRoot $workerRoot }
if (!$PSBoundParameters.ContainsKey('WorkerHostSettingsPath') -and $null -ne $service) {
    $registration = Get-GaoWorkerServiceRegistration -ServiceName $ServiceName
    if (![string]::IsNullOrWhiteSpace($registration.HostSettingsPath)) { $WorkerHostSettingsPath = $registration.HostSettingsPath }
}
if (![IO.Path]::IsPathFullyQualified($WorkerHostSettingsPath)) { throw 'WorkerHostSettingsPath must be absolute.' }
$targetPath = [IO.Path]::GetFullPath($WorkerHostSettingsPath)
if ($targetPath.StartsWith($workerRoot.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    (![string]::IsNullOrWhiteSpace($WebDirectory) -and $targetPath.StartsWith(
        [IO.Path]::GetFullPath($WebDirectory).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
    throw 'Worker host settings must remain outside both Worker and Web publish directories.'
}
Write-Output ('Nguồn khóa Web: ' + $protection.KeysPath)
Write-Output ('PFX Web: ' + $(if ([string]::IsNullOrWhiteSpace($protection.CertificatePath)) { 'không dùng' } else { $protection.CertificatePath }))
if ($RepairFilePermissions) { Write-Output ('Tài khoản Worker nhận quyền: ' + $identity.Account) }
$sourcePath = if (Test-Path -LiteralPath $targetPath -PathType Leaf) { $targetPath } else {
    # One-time migration from an older Worker release. Future upgrades always
    # reuse the external file and preserve its SQL account and other settings.
    @('appsettings.Production.json', 'appsettings.json') |
        ForEach-Object { Join-Path $workerRoot $_ } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($sourcePath)) { throw 'Create the private Worker host settings file first.' }
$document = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($document.ConnectionStrings.DefaultConnection)) {
    throw 'The Worker source settings have no database connection. Configure the private host file first; Web SQL credentials are not copied.'
}
$document | Add-Member -NotePropertyName DataProtection -NotePropertyValue $protection -Force
$preparedJson = $document | ConvertTo-Json -Depth 100
Write-Output ('Cấu hình Worker được giữ qua nâng cấp: ' + $targetPath)
if ($PSCmdlet.ShouldProcess($targetPath, 'Synchronize only DataProtection into the external Worker host settings')) {
    New-GaoPrivateConfigurationDirectory -Path ([IO.Path]::GetDirectoryName($targetPath))
    $backupSuffix = '.before-protection-sync-' + [Guid]::NewGuid().ToString('N') + '.bak'
    if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
        $info = [IO.FileInfo]::new($targetPath)
        $originalAcl = [IO.FileSystemAclExtensions]::GetAccessControl($info, [Security.AccessControl.AccessControlSections]::Access)
        Copy-Item -LiteralPath $targetPath -Destination ($targetPath + $backupSuffix)
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.FileInfo]::new($targetPath + $backupSuffix), $originalAcl)
    }
    [IO.File]::WriteAllText($targetPath, $preparedJson, [Text.UTF8Encoding]::new($false))
    if ($RepairFilePermissions) {
        Add-GaoWorkerProtectionAccess -Identity $identity -KeysPath $protection.KeysPath `
            -CertificatePath $protection.CertificatePath -HostSettingsPath $targetPath `
            -BackupPath (Join-Path ([IO.Path]::GetDirectoryName($targetPath)) ('protection-acl' + $backupSuffix + '.json'))
    }
    Write-Output 'Đã đồng bộ DataProtection từ Web vào cấu hình riêng của máy. Cấu hình trong thư mục publish được giữ nguyên.'
    Write-Output 'Kiểm tra quyền đọc key ring/PFX của tài khoản dịch vụ, rồi chạy Worker --check-credentials bằng đúng tài khoản đó trước khi khởi động lại dịch vụ.'
    Write-Output 'Nếu khóa dùng DPAPI, Web và Worker phải cùng máy. Script không chuyển đổi khóa DPAPI từ máy khác.'
}
