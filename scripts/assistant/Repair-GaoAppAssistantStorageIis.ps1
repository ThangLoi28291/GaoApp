#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$WebPath = 'C:\GaoAppDeploy\ServerMoi\Web',
    [long]$SiteId = 3,
    [string]$AppPoolName = 'GaoAppPool',
    [string]$SourceSettingsDirectory = 'C:\ProgramData\GaoApp\assistant\data',
    [string]$RecoveryRoot = 'C:\GaoAppData',
    [string]$ResumeFromRecovery = '',
    [switch]$WritersQuiesced
)

# This one-time recovery is for a legacy AI settings directory WITHOUT a CSKH database.
# The maintenance CLI runs before the Web host, SQL Server, or channel workers start.
# Existing settings, Data Protection keys, web.config and appsettings are never changed.

function Get-GaoRecoveryPath([string]$Path) {
    if ($Path -notmatch '^[A-Za-z]:[\\/]') { throw 'An absolute local path is required.' }
    $result = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $cursor = $result
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'A storage path traverses a link or reparse point.'
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }; $cursor = $parent
    }
    return $result
}

function Test-GaoRecoveryOverlap([string]$First, [string]$Second) {
    $a = (Get-GaoRecoveryPath $First); $b = (Get-GaoRecoveryPath $Second)
    return $a.Equals($b, [StringComparison]::OrdinalIgnoreCase) -or
        $a.StartsWith($b + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $b.StartsWith($a + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Open-GaoRecoveryIis {
    if (!('Microsoft.Web.Administration.ServerManager' -as [type])) {
        Add-Type -Path (Join-Path $env:windir 'System32\inetsrv\Microsoft.Web.Administration.dll')
    }
    return New-Object Microsoft.Web.Administration.ServerManager
}

function Get-GaoRecoveryEnvironment($Pool) {
    $map = @{}
    foreach ($entry in $Pool.GetCollection('environmentVariables')) {
        $name = ([string]$entry['name']).Replace(':', '__')
        if ($map.ContainsKey($name)) { throw 'Duplicate IIS environment configuration.' }
        $map[$name] = [string]$entry['value']
    }
    return $map
}

function Get-GaoRecoveryMachineValue([string]$Key) {
    $first = [Environment]::GetEnvironmentVariable($Key, 'Machine')
    if (!$Key.Contains('__')) { return $first }
    $second = [Environment]::GetEnvironmentVariable($Key.Replace('__', ':'), 'Machine')
    if ($null -ne $first -and $null -ne $second) { throw 'Duplicate machine environment configuration.' }
    if ($null -ne $first) { return $first }; return $second
}

function Get-GaoRecoveryEffectiveConfiguration([string]$Web, [hashtable]$PoolValues, $Xml) {
    # Return only the storage/path contract; never print provider credentials.
    $keys = @('Assistant__StoreSettingsDirectory','Assistant__StorageId','CustomerService__DatabasePath',
        'Storage__UploadRoot','DataProtection__KeysPath','DataProtection__CertificatePath','Assistant__PrivateKeyFile')
    $webValues = @{}
    foreach ($entry in $Xml.SelectNodes('//aspNetCore/environmentVariables/environmentVariable')) {
        $key = ([string]$entry.name).Replace(':', '__')
        if ($webValues.ContainsKey($key)) { throw 'Duplicate Web environment configuration.' }
        $webValues[$key] = [string]$entry.value
    }
    foreach ($key in @('DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT')) {
        $value = Get-GaoRecoveryMachineValue $key
        if ($PoolValues.ContainsKey($key)) { $value = $PoolValues[$key] }
        if ($webValues.ContainsKey($key)) { $value = $webValues[$key] }
        if ($value -and $value -ine 'Production') { throw 'This recovery was diagnosed for the Production environment.' }
    }
    $effective = @{}
    foreach ($name in @('appsettings.json', 'appsettings.Production.json')) {
        $path = Join-Path $Web $name
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        try { $json = [IO.File]::ReadAllText($path) | ConvertFrom-Json }
        catch { throw 'Unable to read the published JSON configuration safely.' }
        foreach ($key in $keys) {
            $cursor = $json; $found = $true
            foreach ($segment in ($key -split '__')) {
                if ($null -eq $cursor -or !$cursor.PSObject.Properties[$segment]) { $found = $false; break }
                $cursor = $cursor.PSObject.Properties[$segment].Value
            }
            $flat = $json.PSObject.Properties[$key.Replace('__', ':')]
            if ($flat) {
                if ($found) { throw 'Duplicate JSON storage/path configuration.' }
                $cursor = $flat.Value; $found = $true
            }
            if ($found) { $effective[$key] = [string]$cursor }
        }
        $json = $null
    }
    foreach ($key in $keys) {
        $machine = Get-GaoRecoveryMachineValue $key
        if ($null -ne $machine) { $effective[$key] = [string]$machine }
        if ($PoolValues.ContainsKey($key)) { $effective[$key] = $PoolValues[$key] }
        if ($webValues.ContainsKey($key)) { $effective[$key] = $webValues[$key] }
    }
    return $effective
}

function Assert-GaoRecoveryContext($Manager, [string]$Web, [long]$Id, [string]$PoolName, [string]$Source, [string]$Root) {
    $sites = @($Manager.Sites | Where-Object { $_.Id -eq $Id })
    if ($sites.Count -ne 1) { throw 'The expected IIS site was not found.' }
    $application = $sites[0].Applications['/']
    if (!$application -or $application.ApplicationPoolName -cne $PoolName -or
        (Get-GaoRecoveryPath ([Environment]::ExpandEnvironmentVariables($application.VirtualDirectories['/'].PhysicalPath))) -ine $Web) {
        throw 'The IIS site, pool and physical Web path do not match.'
    }
    $pool = $Manager.ApplicationPools[$PoolName]
    if (!$pool -or [string]$pool.ProcessModel.IdentityType -ne 'ApplicationPoolIdentity') {
        throw 'This recovery requires the existing ApplicationPoolIdentity; custom identities need separate ACL configuration.'
    }
    foreach ($site in $Manager.Sites) {
        foreach ($app in $site.Applications) {
            if ($app.ApplicationPoolName -eq $PoolName -and ($site.Id -ne $Id -or $app.Path -ne '/')) {
                throw 'The IIS pool is shared with another application.'
            }
            foreach ($virtual in $app.VirtualDirectories) {
                $public = [Environment]::ExpandEnvironmentVariables($virtual.PhysicalPath)
                if (Test-GaoRecoveryOverlap $Root $public) { throw 'Recovery storage overlaps an IIS public directory.' }
            }
        }
    }
    if (Test-GaoRecoveryOverlap $Root $Source) { throw 'Recovery storage overlaps the original AI settings.' }
    $current = Get-GaoRecoveryEnvironment $pool
    if ($current['Assistant__StoreSettingsDirectory'] -ine $Source -or
        ![string]::IsNullOrWhiteSpace($current['Assistant__StorageId']) -or
        ![string]::IsNullOrWhiteSpace($current['CustomerService__DatabasePath'])) {
        throw 'Storage configuration has changed since the diagnosis; rerun the read-only inspector.'
    }
    # web.config variables override inherited pool variables inside the application.
    $readerSettings = New-Object Xml.XmlReaderSettings
    $readerSettings.DtdProcessing = [Xml.DtdProcessing]::Prohibit; $readerSettings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create((Join-Path $Web 'web.config'), $readerSettings)
    $xml = New-Object Xml.XmlDocument; $xml.XmlResolver = $null
    try { $xml.Load($reader) } finally { $reader.Dispose() }
    $keys = @('Assistant__StoreSettingsDirectory', 'Assistant__StorageId', 'CustomerService__DatabasePath')
    foreach ($entry in $xml.SelectNodes('//aspNetCore/environmentVariables/environmentVariable')) {
        if ($keys -contains ([string]$entry.name).Replace(':', '__')) {
            throw 'A Web storage environment override must be reviewed before recovery.'
        }
    }
    $effective = Get-GaoRecoveryEffectiveConfiguration $Web $current $xml
    if ($effective['Assistant__StoreSettingsDirectory'] -ine $Source -or
        ![string]::IsNullOrWhiteSpace($effective['Assistant__StorageId']) -or
        ![string]::IsNullOrWhiteSpace($effective['CustomerService__DatabasePath'])) {
        throw 'Effective storage configuration has changed; rerun the read-only inspector before migration.'
    }
    foreach ($key in @('Storage__UploadRoot', 'DataProtection__KeysPath', 'DataProtection__CertificatePath', 'Assistant__PrivateKeyFile')) {
        $value = $effective[$key]
        if ($value -and (Test-GaoRecoveryOverlap $Root ([IO.Path]::Combine($Web, [Environment]::ExpandEnvironmentVariables($value))))) {
            throw 'Recovery storage overlaps uploads or cryptographic material.'
        }
    }
    return $current
}

function Stop-GaoRecoveryPool([string]$PoolName) {
    $manager = Open-GaoRecoveryIis
    try {
        $pool = $manager.ApplicationPools[$PoolName]
        if ([string]$pool.State -ne 'Stopped') { [void]$pool.Stop() }
    } finally { $manager.Dispose() }
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $manager = Open-GaoRecoveryIis
        try {
            if ([string]$manager.ApplicationPools[$PoolName].State -eq 'Stopped' -and
                @($manager.WorkerProcesses | Where-Object { $_.AppPoolName -eq $PoolName }).Count -eq 0) { return }
        } finally { $manager.Dispose() }
        Start-Sleep -Seconds 1
    }
    throw 'IIS workers did not stop; no storage migration was attempted.'
}

function Invoke-GaoRecoveryStorage([string]$Web, [hashtable]$Request, [string]$ExpectedStatus) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = 'dotnet'; $start.Arguments = 'GaoApp.Web.dll --assistant-storage'; $start.WorkingDirectory = $Web
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.EnvironmentVariables.Clear()
    foreach ($key in @('PATH','SystemRoot','TEMP','TMP','USERPROFILE','LOCALAPPDATA','APPDATA','ProgramFiles','ProgramFiles(x86)','DOTNET_ROOT','DOTNET_ROOT_X64')) {
        $value = [Environment]::GetEnvironmentVariable($key, 'Process')
        if ($value) { $start.EnvironmentVariables[$key] = $value }
    }
    $process = New-Object Diagnostics.Process; $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.WriteLine(($Request | ConvertTo-Json -Depth 5 -Compress)); $process.StandardInput.Close()
        if (!$process.WaitForExit(300000)) { $process.Kill(); throw 'Offline storage maintenance timed out.' }
        $output = $stdout.GetAwaiter().GetResult(); $errorText = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            $safeType = [regex]::Match($errorText, 'ASSISTANT_STORAGE_FAILED: ([A-Za-z0-9]+Exception)').Groups[1].Value
            throw ('Offline storage maintenance failed: ' + $safeType + '. Preserve the recovery directory for inspection.')
        }
        $result = $output | ConvertFrom-Json
        if ($result.status -cne $ExpectedStatus) { throw 'The offline storage verification marker is missing.' }
        return $result
    } finally { $process.Dispose(); $start.EnvironmentVariables.Clear() }
}

function Set-GaoRecoveryPrivateAcl([string]$Directory, [string]$PoolName, [switch]$GrantPoolModify) {
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sidText in @('S-1-5-18', 'S-1-5-32-544')) {
        $sid = New-Object Security.Principal.SecurityIdentifier($sidText)
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    $pool = New-Object Security.Principal.NTAccount('IIS AppPool', $PoolName)
    if ($GrantPoolModify) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($pool, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    } else {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($pool, 'ReadAndExecute', 'None', 'None', 'Allow')))
    }
    Set-Acl -LiteralPath $Directory -AclObject $acl
}

function Set-GaoRecoveryPoolEnvironment($Manager, [string]$PoolName, [hashtable]$Desired) {
    $collection = $Manager.ApplicationPools[$PoolName].GetCollection('environmentVariables')
    foreach ($key in $Desired.Keys) {
        $found = @($collection | Where-Object { ([string]$_['name']).Replace(':', '__') -ieq $key })
        if ($found.Count -gt 1) { throw 'Duplicate storage environment variable.' }
        if ($found.Count) { $element = $found[0] }
        else {
            $element = $collection.CreateElement('add')
            $element['name'] = $key; $element['value'] = [string]$Desired[$key]
            [void]$collection.Add($element)
        }
        $element['value'] = [string]$Desired[$key]
    }
    # One IIS commit for the settings directory, database and matching identity.
    $Manager.CommitChanges()
}

function Get-GaoRecoverySettingsHashes([string]$Directory, [switch]$Prepared) {
    $root = Get-GaoRecoveryPath $Directory
    if (!(Test-Path -LiteralPath $root -PathType Container)) { throw 'A recovery settings directory is missing.' }
    $hashes = @{}
    $visit = {
        param([string]$Current)
        foreach ($entry in Get-ChildItem -LiteralPath $Current -Force) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Recovery settings contain a link or reparse point.' }
            if ($entry.PSIsContainer) { & $visit $entry.FullName; continue }
            if ($entry.Name -eq '.gao-assistant-storage-hold') { throw 'Recovery settings are held for unresolved maintenance.' }
            $relative = $entry.FullName.Substring($root.Length + 1).Replace('\', '/')
            if ($relative -eq '.gao-assistant-storage.json' -and $Prepared) { continue }
            $hashes[$relative] = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash
        }
    }
    & $visit $root
    return $hashes
}

function Read-GaoRecoveryResume([string]$Envelope, [string]$Root, [long]$Id, [string]$PoolName, [string]$Source, [hashtable]$Before) {
    $envelopePath = Get-GaoRecoveryPath $Envelope; $rootPath = Get-GaoRecoveryPath $Root
    if (!$envelopePath.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $envelopePath) -notlike 'AssistantRecovery-*') { throw 'Select the original recovery directory under the private recovery root.' }
    $receipt = Get-GaoRecoveryPath (Join-Path $envelopePath 'recovery-result.json')
    if (!(Test-Path -LiteralPath $receipt -PathType Leaf) -or (Get-Item -LiteralPath $receipt).Length -gt 65536) { throw 'The verified recovery receipt is missing or invalid.' }
    try { $evidence = [IO.File]::ReadAllText($receipt) | ConvertFrom-Json }
    catch { throw 'The recovery receipt is not valid JSON.' }
    if ($evidence.status -cne 'storage_verified' -or $evidence.siteId -ne $Id -or $evidence.applicationPool -cne $PoolName -or
        $evidence.sourceSettingsDirectory -ine $Source) { throw 'The recovery receipt does not match this interrupted migration.' }
    $keys = @('Assistant__StoreSettingsDirectory', 'Assistant__StorageId', 'CustomerService__DatabasePath')
    if (@($evidence.settings.PSObject.Properties).Count -ne 3) { throw 'Unexpected recovery configuration keys.' }
    foreach ($key in $keys) {
        if (!$evidence.settings.PSObject.Properties[$key] -or !$evidence.previousSettings.PSObject.Properties[$key] -or
            [string]$evidence.previousSettings.PSObject.Properties[$key].Value -cne [string]$Before[$key]) { throw 'The original IIS storage configuration no longer matches the recovery receipt.' }
    }
    $target = Get-GaoRecoveryPath (Join-Path $envelopePath 'storage')
    if ($evidence.settings.Assistant__StoreSettingsDirectory -ine (Join-Path $target 'settings') -or
        $evidence.settings.CustomerService__DatabasePath -ine (Join-Path $target 'customer-service.db')) { throw 'The recovery receipt points outside the verified storage.' }
    $fileEvidence = Get-GaoRecoveryPath (Join-Path $target 'source-settings-evidence.json')
    if (!(Test-Path -LiteralPath $fileEvidence -PathType Leaf) -or (Get-Item -LiteralPath $fileEvidence).Length -gt 1048576) { throw 'The AI settings continuity evidence is missing or invalid.' }
    try { $original = [IO.File]::ReadAllText($fileEvidence) | ConvertFrom-Json }
    catch { throw 'The AI settings continuity evidence is not valid JSON.' }
    $expected = @{}
    foreach ($property in $original.PSObject.Properties) {
        $key = $property.Name.Replace('\', '/')
        if ($expected.ContainsKey($key) -or [string]$property.Value -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Invalid AI settings continuity evidence.' }
        $expected[$key] = [string]$property.Value
    }
    if (!$expected.Count) { throw 'The existing AI settings evidence is empty.' }
    foreach ($actual in @((Get-GaoRecoverySettingsHashes $Source),
        (Get-GaoRecoverySettingsHashes (Join-Path $target 'source-settings-backup')),
        (Get-GaoRecoverySettingsHashes (Join-Path $target 'settings') -Prepared))) {
        if ($actual.Count -ne $expected.Count) { throw 'AI settings changed after migration; preserve both copies for inspection.' }
        foreach ($key in $expected.Keys) {
            if (!$actual.ContainsKey($key) -or $actual[$key] -ine $expected[$key]) { throw 'AI settings changed after migration; preserve both copies for inspection.' }
        }
    }
    return [pscustomobject]@{storageId=[string]$evidence.settings.Assistant__StorageId;
        settingsDirectory=[string]$evidence.settings.Assistant__StoreSettingsDirectory;databasePath=[string]$evidence.settings.CustomerService__DatabasePath}
}

function Invoke-GaoAssistantIisRecovery {
    [CmdletBinding()]
    param([string]$Web, [long]$Id, [string]$PoolName, [string]$Source, [string]$Root, [bool]$Quiesced, [string]$ResumeFrom = '')
    $ErrorActionPreference = 'Stop'
    if (!$Quiesced) { throw 'Stop any other writers and supply -WritersQuiesced. This script stops the IIS pool itself.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run Windows PowerShell as Administrator.' }
    $Web = Get-GaoRecoveryPath $Web; $Source = Get-GaoRecoveryPath $Source; $Root = Get-GaoRecoveryPath $Root
    foreach ($file in @('GaoApp.Web.dll','GaoApp.Web.runtimeconfig.json','web.config')) {
        if (!(Test-Path -LiteralPath (Join-Path $Web $file) -PathType Leaf)) { throw 'The published Web files are incomplete.' }
    }
    if (!(Test-Path -LiteralPath $Source -PathType Container)) { throw 'The original AI settings directory is missing.' }
    if (!@(Get-ChildItem -LiteralPath $Source -Filter 'store-*.json' -File -Force).Count) { throw 'The diagnosed AI settings files are missing; no empty replacement will be created.' }
    if ((Test-Path -LiteralPath (Join-Path $Source '.gao-assistant-storage.json')) -or
        (Test-Path -LiteralPath (Join-Path $Source 'customer-service.db'))) { throw 'The source is already prepared or contains a database; this settings-only recovery is not applicable.' }
    if (Test-Path -LiteralPath (Join-Path $Source '.gao-assistant-storage-hold')) { throw 'Existing storage maintenance is unresolved; preserve its evidence before proceeding.' }
    $envelope = Join-Path $Root ('AssistantRecovery-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
    if ($ResumeFrom) { $envelope = Get-GaoRecoveryPath $ResumeFrom }
    $manager = Open-GaoRecoveryIis
    try { $before = Assert-GaoRecoveryContext $manager $Web $Id $PoolName $Source $envelope }
    finally { $manager.Dispose() }
    Stop-GaoRecoveryPool $PoolName
    $stage = 'prepare'
    try {
        $target = Join-Path $envelope 'storage' # The CLI requires a NEW, nonexistent destination.
        if ($ResumeFrom) {
            $stage = 'resume-verify'
            $prepared = Read-GaoRecoveryResume $envelope $Root $Id $PoolName $Source $before
        } else {
            [void][IO.Directory]::CreateDirectory($envelope)
            Set-GaoRecoveryPrivateAcl $envelope $PoolName
            $prepared = Invoke-GaoRecoveryStorage $Web @{
                operation='migrate-settings'; sourceSettingsDirectory=$Source; sourceDatabase=(Join-Path $Source 'customer-service.db')
                destinationDirectory=$target; writersQuiesced=$true
            } 'storage_prepared'
        }
        $guid = [Guid]::Empty
        if (![Guid]::TryParseExact([string]$prepared.storageId, 'D', [ref]$guid) -or $guid -eq [Guid]::Empty -or
            $prepared.settingsDirectory -ine (Join-Path $target 'settings') -or $prepared.databasePath -ine (Join-Path $target 'customer-service.db')) {
            throw 'Prepared storage returned an unexpected identity or path.'
        }
        $stage = 'verify'
        [void](Invoke-GaoRecoveryStorage $Web @{
            operation='check'; settingsDirectory=$prepared.settingsDirectory; databasePath=$prepared.databasePath; storageId=$prepared.storageId
        } 'storage_verified')
        $desired = @{
            Assistant__StoreSettingsDirectory=[string]$prepared.settingsDirectory
            Assistant__StorageId=[string]$prepared.storageId
            CustomerService__DatabasePath=[string]$prepared.databasePath
        }
        $receipt = Join-Path $envelope 'recovery-result.json'
        $evidence = [ordered]@{status='storage_verified';siteId=$Id;applicationPool=$PoolName;sourceSettingsDirectory=$Source;previousSettings=@{
            Assistant__StoreSettingsDirectory=$before['Assistant__StoreSettingsDirectory'];Assistant__StorageId=$before['Assistant__StorageId'];CustomerService__DatabasePath=$before['CustomerService__DatabasePath']
        };settings=$desired}
        [IO.File]::WriteAllText($receipt, ($evidence | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
        Set-GaoRecoveryPrivateAcl $target $PoolName -GrantPoolModify
        $stage = 'configure'
        $manager = Open-GaoRecoveryIis
        try {
            [void](Assert-GaoRecoveryContext $manager $Web $Id $PoolName $Source $envelope)
            if ([string]$manager.ApplicationPools[$PoolName].State -ne 'Stopped' -or
                @($manager.WorkerProcesses | Where-Object { $_.AppPoolName -eq $PoolName }).Count) { throw 'The pool was restarted during migration.' }
            Set-GaoRecoveryPoolEnvironment $manager $PoolName $desired
        } finally { $manager.Dispose() }
        $manager = Open-GaoRecoveryIis
        try {
            $actual = Get-GaoRecoveryEnvironment $manager.ApplicationPools[$PoolName]
            foreach ($key in $desired.Keys) { if ($actual[$key] -cne $desired[$key]) { throw 'IIS storage configuration verification failed.' } }
            $stage = 'start-pool'
            [void]$manager.ApplicationPools[$PoolName].Start()
        } finally { $manager.Dispose() }
        $evidence.status = 'GAOAPP_ASSISTANT_STORAGE_CONFIGURED'
        [IO.File]::WriteAllText($receipt, ($evidence | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
        [pscustomobject]@{status=$evidence.status;applicationPool=$PoolName;storageId=$prepared.storageId;settingsDirectory=$prepared.settingsDirectory;
            databasePath=$prepared.databasePath;originalSettingsPreserved=$true;receipt=$receipt;webStartupVerified=$false} | ConvertTo-Json -Depth 4
    } catch {
        # Never erase partial recovery data or silently return to an unprepared storage target.
        Write-Output ("Recovery stopped at stage: $stage. Preserve directory: $envelope")
        throw
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-GaoAssistantIisRecovery -Web $WebPath -Id $SiteId -PoolName $AppPoolName -Source $SourceSettingsDirectory -Root $RecoveryRoot -Quiesced ([bool]$WritersQuiesced) -ResumeFrom $ResumeFromRecovery
}
