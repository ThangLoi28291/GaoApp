#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ServiceName = 'GaoApp.AutoInvoiceWorker',
    [string]$InstallDirectory = 'C:\GaoApp\AutoInvoiceWorker',
    [string]$HostSettingsPath = 'C:\GaoAppData\DeployConfig\worker.appsettings.Production.json',
    # Starting can process an enabled queue. Installation/update alone leaves
    # the service stopped so configuration can be checked first.
    [switch]$Start
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ServiceName) -or $ServiceName.IndexOfAny([char[]]'"/\') -ge 0) {
    throw 'ServiceName must be a valid Windows service name.'
}
. (Join-Path $PSScriptRoot 'WorkerProtection.Common.ps1')
$workerRoot = (Resolve-Path -LiteralPath $InstallDirectory).Path
$exe = Join-Path $workerRoot 'GaoApp.AutoInvoiceWorker.exe'
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw 'Publish GaoApp.AutoInvoiceWorker into InstallDirectory first.'
}
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$registration = if ($null -ne $existing) { Get-GaoWorkerServiceRegistration -ServiceName $ServiceName }
if (!$PSBoundParameters.ContainsKey('HostSettingsPath') -and $null -ne $registration -and
    ![string]::IsNullOrWhiteSpace($registration.HostSettingsPath)) {
    $HostSettingsPath = $registration.HostSettingsPath
}
if (![IO.Path]::IsPathFullyQualified($HostSettingsPath) -or $HostSettingsPath.Contains('"')) {
    throw 'HostSettingsPath must be an absolute file path.'
}
$hostPath = [IO.Path]::GetFullPath($HostSettingsPath)
if ($hostPath.StartsWith($workerRoot.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'HostSettingsPath must remain outside the published Worker directory.'
}
if (!(Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw 'Create the private Worker host settings file before installing/updating the service.'
}
$settings = Get-Content -LiteralPath $hostPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($settings.ConnectionStrings.DefaultConnection) -or
    [string]::IsNullOrWhiteSpace($settings.DataProtection.KeysPath)) {
    throw 'Private host settings must contain ConnectionStrings:DefaultConnection and DataProtection:KeysPath.'
}
$binaryPath = '"{0}" --environment Production --host-settings "{1}" --Worker:ServiceName "{2}"' -f $exe, $hostPath, $ServiceName
Write-Output ('Worker release: ' + $exe)
Write-Output ('Cấu hình riêng của server: ' + $hostPath)
if ($null -ne $registration) { Write-Output ('Giữ tài khoản service: ' + $registration.Account) }

if ($PSCmdlet.ShouldProcess($ServiceName, 'Install or update Worker executable while preserving existing service identity and recovery')) {
    if ($null -ne $existing) {
        if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -ErrorAction Stop }
        # Change only PathName. Do not delete/recreate: credentials, delayed
        # startup, recovery and service ACL remain attached to the same service.
        $service = @(Get-CimInstance -ClassName Win32_Service | Where-Object { $_.Name -eq $ServiceName })
        if ($service.Count -ne 1) { throw 'Cannot identify the existing Worker service.' }
        $result = Invoke-CimMethod -InputObject $service[0] -MethodName Change -Arguments @{ PathName = $binaryPath }
        if ($result.ReturnValue -ne 0) { throw ('Failed to update Worker service path. Return code: ' + $result.ReturnValue) }
    } else {
        New-Service -Name $ServiceName -BinaryPathName $binaryPath -StartupType Manual `
            -DisplayName 'GaoApp Auto Invoice Worker' `
            -Description 'GaoApp worker phát hành hóa đơn tự động theo queue SQL.' | Out-Null
        & sc.exe config $ServiceName obj= ('NT SERVICE\' + $ServiceName) | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot configure the virtual service account. The new service remains stopped.' }
    }
    if ($Start) { Start-Service -Name $ServiceName }
    else { Write-Output 'Service đang dừng. Kiểm tra cấu hình/quyền dưới tài khoản service trước khi Start-Service.' }
    Get-Service -Name $ServiceName | Select-Object Name, Status, StartType
}
