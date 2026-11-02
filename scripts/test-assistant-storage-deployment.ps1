#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$WebPath)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. (Join-Path $repo 'eng/deployment/Deployment.Common.ps1')
$checks=0
function Check([string]$Name,[scriptblock]$Action) { & $Action; $script:checks++; Write-Output "PASS $Name" }
function Reject([scriptblock]$Action,[string]$Expected) {
    try { & $Action; throw 'EXPECTED_REJECTION_MISSING' }
    catch { if($_.Exception.Message -notlike "*$Expected*") { throw } }
}
$work=Join-Path ([IO.Path]::GetTempPath()) ('gao-c1-deployment-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$storage=$null
Check 'Real offline CLI initializes only explicit isolated storage without Web/SQL startup' {
    $script:storage=Invoke-GaoAssistantStorage $WebPath @{operation='initialize';destinationDirectory=(Join-Path $work 'private');writersQuiesced=$true}
    if($storage.status -ne 'storage_prepared') { throw 'Initialization marker missing' }
}
Check 'Real CLI verifies identity and consistent SQLite backup with private settings' {
    [IO.File]::WriteAllText((Join-Path $storage.settingsDirectory 'store-1.json'),'TEST original private settings')
    $script:request=@{operation='snapshot';databasePath=$storage.databasePath;settingsDirectory=$storage.settingsDirectory;storageId=$storage.storageId;snapshotDirectory=(Join-Path $work 'snapshot');writersQuiesced=$true}
    [void](Invoke-GaoAssistantStorage $WebPath $request)
    $request.operation='verify';[void](Invoke-GaoAssistantStorage $WebPath $request)
}
Check 'Real CLI rejects mismatched identity and lost database instead of creating another' {
    Reject { Invoke-GaoAssistantStorage $WebPath @{operation='check';databasePath=$storage.databasePath;settingsDirectory=$storage.settingsDirectory;storageId=[Guid]::NewGuid().ToString('D')} } 'maintenance failed'
    $missing=Join-Path $work 'missing.db'
    Reject { Invoke-GaoAssistantStorage $WebPath @{operation='check';databasePath=$missing;settingsDirectory=$storage.settingsDirectory;storageId=$storage.storageId} } 'maintenance failed'
    if(Test-Path -LiteralPath $missing) { throw 'Missing database was recreated' }
}
Check 'Real continuity verification refuses changed files and never restores over them' {
    $file=Join-Path $storage.settingsDirectory 'store-1.json';[IO.File]::WriteAllText($file,'TEST new data after snapshot')
    Reject { Invoke-GaoAssistantStorage $WebPath $request } 'maintenance failed'
    if([IO.File]::ReadAllText($file) -ne 'TEST new data after snapshot') { throw 'New data was overwritten' }
}
Check 'Hold collision preserves foreign hold and permits removal of only owned files' {
    $state=[pscustomobject]@{SettingsDirectory=$storage.settingsDirectory;DatabasePath=$storage.databasePath;HoldOwner='';HoldFiles=@()}
    $foreign=Join-Path (Split-Path $storage.databasePath -Parent) '.gao-assistant-storage-hold';[IO.File]::WriteAllText($foreign,'TEST other owner')
    Reject { Enter-GaoAssistantHold $state } 'already exists'
    Exit-GaoAssistantHold $state
    if([IO.File]::ReadAllText($foreign) -ne 'TEST other owner') { throw 'Foreign hold overwritten' }
    Remove-Item -LiteralPath $foreign -Force
}
Check 'Real junction cannot redirect storage creation or backup into another directory' {
    $target=Join-Path $work 'junction-target';$link=Join-Path $work 'junction-link';New-Item -ItemType Directory -Path $target | Out-Null
    New-Item -ItemType Junction -Path $link -Target $target | Out-Null
    Reject { Invoke-GaoAssistantStorage $WebPath @{operation='initialize';destinationDirectory=(Join-Path $link 'must-not-create');writersQuiesced=$true} } 'maintenance failed'
    Reject { Assert-GaoNoLinks $link } 'Link/reparse'
    if(Test-Path -LiteralPath (Join-Path $target 'must-not-create')) { throw 'Junction target was changed' }
    # Keep this isolated tree for inspection; never recursively remove a junction.
}

function RecoveryCase([string]$Name) {
    $prepared=Invoke-GaoAssistantStorage $WebPath @{operation='initialize';destinationDirectory=(Join-Path $work $Name);writersQuiesced=$true}
    [IO.File]::WriteAllText((Join-Path $prepared.settingsDirectory 'store-1.json'),'TEST recovery original')
    $state=[pscustomobject]@{SettingsDirectory=$prepared.settingsDirectory;DatabasePath=$prepared.databasePath;StorageId=$prepared.storageId;SnapshotDirectory=(Join-Path $work ($Name+'-snapshot'));HoldOwner='';HoldFiles=@()}
    [void](Invoke-GaoAssistantStorage $WebPath (New-GaoAssistantRequest $state 'snapshot'))
    Enter-GaoAssistantHold $state
    $evidence=Join-Path $work ($Name+'-hold.json');[IO.File]::WriteAllText($evidence,($state|ConvertTo-Json -Depth 4))
    return [pscustomobject]@{State=$state;Evidence=$evidence;Environment=@{CustomerService__DatabasePath=$state.DatabasePath;Assistant__StoreSettingsDirectory=$state.SettingsDirectory;Assistant__StorageId=$state.StorageId}}
}
Check 'Recovery removes only owned holds after real SQLite and file continuity verification' {
    $case=RecoveryCase 'recovery-good'
    Complete-GaoAssistantRecovery $WebPath $case.Evidence $case.Environment $true
    foreach($file in $case.State.HoldFiles){if(Test-Path -LiteralPath $file){throw 'Owned hold not released'}}
    if(!(Test-Path -LiteralPath $case.State.DatabasePath)){throw 'Database missing after recovery'}
}
Check 'Recovery never unlocks or restores over new data after a snapshot' {
    $case=RecoveryCase 'recovery-new-data';$file=Join-Path $case.State.SettingsDirectory 'store-1.json';[IO.File]::WriteAllText($file,'TEST recovery new data')
    Reject {Complete-GaoAssistantRecovery $WebPath $case.Evidence $case.Environment $true} 'maintenance failed'
    if([IO.File]::ReadAllText($file) -ne 'TEST recovery new data'){throw 'Recovery overwrote newer data'}
    foreach($hold in $case.State.HoldFiles){if(!(Test-Path -LiteralPath $hold)){throw 'Failed recovery released a hold'}}
}
Check 'Recovery rejects mismatched target and missing snapshot while keeping writers blocked' {
    $case=RecoveryCase 'recovery-wrong-target';$case.Environment.CustomerService__DatabasePath+='-wrong'
    Reject {Complete-GaoAssistantRecovery $WebPath $case.Evidence $case.Environment $true} 'does not match'
    $case.Environment.CustomerService__DatabasePath=$case.State.DatabasePath;$case.State.SnapshotDirectory=''
    [IO.File]::WriteAllText($case.Evidence,($case.State|ConvertTo-Json -Depth 4))
    Reject {Complete-GaoAssistantRecovery $WebPath $case.Evidence $case.Environment $true} 'No verified snapshot'
    foreach($hold in $case.State.HoldFiles){if(!(Test-Path -LiteralPath $hold)){throw 'Invalid recovery released a hold'}}
}
Check 'Recovery refuses a hold whose ownership has changed' {
    $case=RecoveryCase 'recovery-owner';$file=$case.State.HoldFiles[0];[IO.File]::WriteAllText($file,'TEST foreign owner')
    Reject {Complete-GaoAssistantRecovery $WebPath $case.Evidence $case.Environment $true} 'ownership changed'
    if([IO.File]::ReadAllText($file) -ne 'TEST foreign owner'){throw 'Foreign hold was removed'}
}

# All IIS and C:\GaoAppData interaction from this point is mocked. No server configuration changes.
$script:fakeEnvironment=@{
    DOTNET_ENVIRONMENT='Production';ASPNETCORE_ENVIRONMENT='Production';SeedData__EnableDemoSeed='false';SeedData__EnableDefaultAdminSeed='false';ProductionBootstrap__Enabled='false';
    ConnectionStrings__DefaultConnection='Server=SYNTHETIC_ONLY;Database=NOT_USED';Storage__UploadRoot='C:\GaoAppData\uploads';DataProtection__KeysPath='C:\GaoAppData\keys';DataProtection__CertificatePath='C:\GaoAppData\keys\test.pfx';
    Assistant__StoreSettingsDirectory='C:\GaoAppData\assistant\settings';CustomerService__DatabasePath='C:\GaoAppData\assistant\customer-service.db';Assistant__StorageId=[Guid]::NewGuid().ToString('D')
}
foreach($name in @('old-web','release/web','backups','mart')) { New-Item -ItemType Directory -Path (Join-Path $work $name) -Force | Out-Null }
$realNoLinks=${function:Assert-GaoNoLinks}
function Assert-GaoNoLinks([string]$Path) { if(!$Path.StartsWith('C:\GaoAppData',[StringComparison]::OrdinalIgnoreCase)){& $realNoLinks $Path} }
function Test-Path([string]$LiteralPath,[string]$PathType) {
    if($LiteralPath.StartsWith('C:\GaoAppData',[StringComparison]::OrdinalIgnoreCase)){return $true}
    return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $(if($PathType){$PathType}else{'Any'})
}
function Import-Module {param($Name,$ErrorAction)}
function Get-Website([string]$Name) {
    $gao=[pscustomobject]@{Name='GaoApp';applicationPool='GaoAppPool';State='Started';physicalPath=(Join-Path $work 'old-web');Bindings=[pscustomobject]@{Collection=@([pscustomobject]@{protocol='https';bindingInformation='*:443:gaoapp.example.test'})}}
    $mart=[pscustomobject]@{Name='GaoMart';applicationPool='GaoMartPool';State='Started';physicalPath=(Join-Path $work 'mart')}
    if($Name -eq 'GaoApp'){return $gao};if($Name -eq 'GaoMart'){return $mart};return @($gao,$mart)
}
function Get-WebApplication {param($Site) return @()}
function Get-GaoIisPublicRoots {return @((Join-Path $work 'old-web'),(Join-Path $work 'mart'))}
function Invoke-GaoChannelCredentialCheck {param($WebPath,$Environment,$PublicRoots,[switch]$CheckAcl) return [pscustomobject]@{status='verified';connections=0;files=0}}
function Get-WebAppPoolState {param($Name) return [pscustomobject]@{Value='Started'}}
function Get-GaoMartState {return [pscustomobject]@{Configuration='synthetic';Files=@()}}
function Get-WebConfigurationProperty {param($PSPath,$Filter,$Name) return [pscustomobject]@{Collection=@($script:fakeEnvironment.GetEnumerator()|ForEach-Object{[pscustomobject]@{name=$_.Key;value=$_.Value}})}}
$script:nativeCalls=0
function Invoke-GaoAssistantStorage([string]$WebPath,[hashtable]$Request) {$script:nativeCalls++;return [pscustomobject]@{status='storage_verified'}}
function Context {Get-GaoDeploymentContext (Join-Path $work 'release') (Join-Path $work 'backups') ([uri]'https://gaoapp.example.test/health/ready')}
Check 'Deployment blocks every missing persistent key before activation' {
    foreach($key in @('Assistant__StorageId','Assistant__StoreSettingsDirectory','CustomerService__DatabasePath')) {
        $previous=$fakeEnvironment[$key];$fakeEnvironment[$key]=''
        try{Reject {Context} 'Missing persistent storage setting'}finally{$fakeEnvironment[$key]=$previous}
    }
    if($script:nativeCalls -ne 0){throw 'Invalid paths reached native maintenance'}
}
Check 'Deployment rejects relative release upload key and traversal paths' {
    $key='CustomerService__DatabasePath';$previous=$fakeEnvironment[$key]
    foreach($path in @('relative.db',(Join-Path $work 'release/web/data.db'),'C:\GaoAppData\uploads\data.db','C:\GaoAppData\keys\data.db','C:\GaoAppData\assistant\..\uploads\data.db')) {
        $fakeEnvironment[$key]=$path
        try{Reject {Context} 'storage'}finally{$fakeEnvironment[$key]=$previous}
    }
}
Check 'Deployment accepts only prepared explicit paths and checks the new release CLI' {
    $context=Context
    if($context.AssistantStorage.StorageId -ne $fakeEnvironment.Assistant__StorageId -or $script:nativeCalls -ne 1){throw 'Continuity context missing'}
}
Write-Output "RESULT: $checks/$checks PASS (synthetic SQLite/files; mocked IIS; no production deployment or SQL Server)."
