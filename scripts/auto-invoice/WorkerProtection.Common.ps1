function Get-GaoWebProtectionSettings {
    param(
        [Parameter(Mandatory)][string]$WebDirectory,
        [hashtable]$PoolEnvironment = @{},
        [hashtable]$MachineEnvironment = @{}
    )
    $webRoot = (Resolve-Path -LiteralPath $WebDirectory).Path
    $environmentValues = @{}
    foreach ($source in @($MachineEnvironment, $PoolEnvironment)) {
        foreach ($key in $source.Keys) { $environmentValues[$key] = $source[$key] }
    }
    $webConfig = Join-Path $webRoot 'web.config'
    if (Test-Path -LiteralPath $webConfig -PathType Leaf) {
        [xml]$webXml = Get-Content -LiteralPath $webConfig -Raw
        foreach ($entry in $webXml.SelectNodes('//system.webServer/aspNetCore/environmentVariables/environmentVariable')) {
            $environmentValues[[string]$entry.name] = [string]$entry.value
        }
    }
    # .NET 8 WebApplication gives DOTNET_ENVIRONMENT precedence over
    # ASPNETCORE_ENVIRONMENT. These values identify the Web JSON override.
    $environmentName = $environmentValues['DOTNET_ENVIRONMENT']
    if ([string]::IsNullOrWhiteSpace($environmentName)) { $environmentName = $environmentValues['ASPNETCORE_ENVIRONMENT'] }
    if ([string]::IsNullOrWhiteSpace($environmentName)) { $environmentName = 'Production' }
    if ($environmentName -notmatch '^[A-Za-z0-9._-]+$') { throw 'Invalid Web environment name.' }

    $protection = [ordered]@{ KeysPath = ''; RequirePortableKeys = $false; CertificatePath = ''; CertificatePassword = '' }
    foreach ($name in @('appsettings.json', ('appsettings.' + $environmentName + '.json'))) {
        $path = Join-Path $webRoot $name
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $jsonValues = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        foreach ($key in @($protection.Keys)) {
            if ($null -ne $jsonValues.DataProtection -and $null -ne $jsonValues.DataProtection.PSObject.Properties[$key]) {
                $protection[$key] = $jsonValues.DataProtection.$key
            }
        }
    }
    foreach ($key in @($protection.Keys)) {
        $name = 'DataProtection__' + $key
        if ($environmentValues.ContainsKey($name)) { $protection[$key] = $environmentValues[$name] }
    }
    # Web resolves relative key paths against its own content root. Copy the
    # resolved location, never a relative path that Worker would reinterpret.
    foreach ($key in @('KeysPath', 'CertificatePath')) {
        $value = [string]$protection[$key]
        if (![string]::IsNullOrWhiteSpace($value) -and ![IO.Path]::IsPathFullyQualified($value)) {
            $protection[$key] = [IO.Path]::GetFullPath((Join-Path $webRoot $value))
        }
    }
    return $protection
}

function Get-GaoWorkerServiceRegistration {
    param([Parameter(Mandatory)][string]$ServiceName)
    $service = @(Get-CimInstance -ClassName Win32_Service | Where-Object { $_.Name -eq $ServiceName })
    if ($service.Count -ne 1) { throw 'Cannot identify the Worker service account. Verify ServiceName before repairing permissions.' }
    $command = [string]$service[0].PathName
    if ($command -match '^\s*"([^"]+\.exe)"(?:\s|$)') { $executable = $Matches[1] }
    elseif ($command -match '^\s*(\S+\.exe)(?:\s|$)') { $executable = $Matches[1] }
    else { throw 'Worker service executable path must be unambiguous.' }
    if (![IO.Path]::IsPathFullyQualified($executable) -or
        [IO.Path]::GetFileName($executable) -ne 'GaoApp.AutoInvoiceWorker.exe') {
        throw 'Service executable must be the published GaoApp.AutoInvoiceWorker.exe.'
    }
    $hostSettingsPath = $null
    if ($command -match '(?:^|\s)--(?:host-settings|Worker:HostSettingsPath)(?:\s+|=)"([^"]+)"') { $hostSettingsPath = $Matches[1] }
    elseif ($command -match '(?:^|\s)--(?:host-settings|Worker:HostSettingsPath)(?:\s+|=)(\S+)') { $hostSettingsPath = $Matches[1] }
    return [pscustomobject]@{Executable=[IO.Path]::GetFullPath($executable); Account=[string]$service[0].StartName; HostSettingsPath=$hostSettingsPath}
}

function New-GaoPrivateConfigurationDirectory {
    param([Parameter(Mandatory)][string]$Path)
    if (Test-Path -LiteralPath $Path -PathType Container) { return }
    # New secret directories do not inherit broad Users permissions. Existing
    # directories are left intact; retain their established ACL.
    $directory = [IO.Directory]::CreateDirectory($Path)
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $sids = @('S-1-5-18', 'S-1-5-32-544', [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) | Select-Object -Unique
    foreach ($sid in $sids) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    [IO.FileSystemAclExtensions]::SetAccessControl($directory, $acl)
}

function Get-GaoWorkerServiceIdentity {
    param([Parameter(Mandatory)][string]$ServiceName, [Parameter(Mandatory)][string]$WorkerRoot)
    $service = Get-GaoWorkerServiceRegistration -ServiceName $ServiceName
    $executable = $service.Executable
    $expected = [IO.Path]::GetFullPath((Join-Path $WorkerRoot 'GaoApp.AutoInvoiceWorker.exe'))
    if (![IO.Path]::IsPathFullyQualified($executable) -or
        ![string]::Equals([IO.Path]::GetFullPath($executable), $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'WorkerDirectory does not match the installed service executable. No permissions were changed.'
    }
    $account = $service.Account
    $sid = switch -Regex ($account) {
        '^LocalSystem$' { [Security.Principal.SecurityIdentifier]::new('S-1-5-18'); break }
        '^NT AUTHORITY\\(NETWORK ?SERVICE)$' { [Security.Principal.SecurityIdentifier]::new('S-1-5-20'); break }
        '^NT AUTHORITY\\(LOCAL ?SERVICE)$' { [Security.Principal.SecurityIdentifier]::new('S-1-5-19'); break }
        default { [Security.Principal.NTAccount]::new($account).Translate([Security.Principal.SecurityIdentifier]) }
    }
    if ($sid.Value -in @('S-1-1-0', 'S-1-5-11', 'S-1-5-32-545')) { throw 'A broad group cannot be the Worker service account.' }
    return [pscustomobject]@{ Account = $account; Sid = $sid }
}

function Add-GaoWorkerProtectionAccess {
    param(
        [Parameter(Mandatory)]$Identity,
        [Parameter(Mandatory)][string]$KeysPath,
        [string]$CertificatePath,
        [string]$HostSettingsPath,
        [Parameter(Mandatory)][string]$BackupPath
    )
    $targets = @([pscustomobject]@{Path=$KeysPath; Rights='Modify'; Directory=$true})
    # Explicit entries cover existing keys whose inheritance is disabled.
    $targets += @(Get-ChildItem -LiteralPath $KeysPath -File | Where-Object {
        $_.Name -like 'key-*.xml' -or $_.Name -like 'revocation-*.xml'
    } | ForEach-Object { [pscustomobject]@{Path=$_.FullName; Rights='Read'; Directory=$false} })
    if (![string]::IsNullOrWhiteSpace($CertificatePath)) {
        $targets += [pscustomobject]@{Path=$CertificatePath; Rights='Read'; Directory=$false}
    }
    if (![string]::IsNullOrWhiteSpace($HostSettingsPath)) {
        $targets += [pscustomobject]@{Path=$HostSettingsPath; Rights='Read'; Directory=$false}
        $targets += [pscustomobject]@{Path=[IO.Path]::GetDirectoryName($HostSettingsPath); Rights='ReadAndExecute'; Directory=$true; NoInheritance=$true}
    }
    $snapshots = @($targets | ForEach-Object {
        $acl = Get-Acl -LiteralPath $_.Path
        [pscustomobject]@{Path=$_.Path; Sddl=$acl.Sddl}
    })
    $snapshots | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $BackupPath -Encoding utf8
    foreach ($target in $targets) {
        # Persist only the DACL. Reapplying owner/SACL from Get-Acl is
        # unrelated to file access and may require SeSecurityPrivilege.
        $info = if ($target.Directory) { [IO.DirectoryInfo]::new($target.Path) } else { [IO.FileInfo]::new($target.Path) }
        $acl = [IO.FileSystemAclExtensions]::GetAccessControl($info, [Security.AccessControl.AccessControlSections]::Access)
        $inheritance = if ($target.Directory -and !$target.NoInheritance) {
            [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
        } else { [Security.AccessControl.InheritanceFlags]::None }
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($Identity.Sid,
            [Security.AccessControl.FileSystemRights]$target.Rights, $inheritance,
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
        [IO.FileSystemAclExtensions]::SetAccessControl($info, $acl)
    }
}
