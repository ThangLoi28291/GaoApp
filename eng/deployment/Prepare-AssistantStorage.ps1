#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [Parameter(Mandatory)][string]$ExpectedManifestSha256,
    [Parameter(Mandatory)][string]$DestinationDirectory,
    [string]$SourceDatabase,
    [string]$SourceSettingsDirectory,
    [switch]$InitializeNewStorage,
    [switch]$MigrateSettingsOnly,
    [switch]$WritersQuiesced
)
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$lock = Enter-GaoDeploymentLock
try {
    if (!$WritersQuiesced) { throw 'Stop every writer and explicitly supply -WritersQuiesced.' }
    Import-Module WebAdministration -ErrorAction Stop
    if ((Get-WebAppPoolState -Name 'GaoAppPool').Value -ne 'Stopped') { throw 'Stop GaoAppPool before preparing storage.' }
    Wait-GaoPoolStopped
    Assert-GaoPackage $ReleasePath $ExpectedManifestSha256 ([bool](Test-Path -LiteralPath (Join-Path $ReleasePath 'migrator')))
    $target = [IO.Path]::GetFullPath($DestinationDirectory)
    if ($DestinationDirectory -notmatch '^[a-zA-Z]:[\\/]' -or !$target.StartsWith('C:\GaoAppData\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Destination must be an absolute private path under C:\GaoAppData.' }
    $collection = Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.applicationHost/applicationPools/add[@name='GaoAppPool']/environmentVariables" -Name '.'
    $hostSettings = @{}; foreach ($entry in $collection.Collection) { $hostSettings[$entry.name] = $entry.value }
    foreach ($key in @('Storage__UploadRoot','DataProtection__KeysPath','DataProtection__CertificatePath')) {
        if ([string]::IsNullOrWhiteSpace($hostSettings[$key])) { throw "Missing pool setting: $key" }
    }
    $publicRoots = @((Resolve-Path -LiteralPath $ReleasePath).Path) + @(Get-Website | ForEach-Object { [Environment]::ExpandEnvironmentVariables($_.physicalPath) })
    foreach ($root in $publicRoots + @($hostSettings['Storage__UploadRoot'],$hostSettings['DataProtection__KeysPath'],$hostSettings['DataProtection__CertificatePath'])) {
        if (Test-GaoOverlap $target $root) { throw 'Storage destination overlaps release/public/upload/key storage.' }
    }
    $operation = 'migrate'
    if ($InitializeNewStorage -and $MigrateSettingsOnly) { throw 'Choose only one explicit preparation mode.' }
    if ($InitializeNewStorage) {
        if ($SourceDatabase -or $SourceSettingsDirectory) { throw 'New storage initialization cannot include a legacy source.' }
        $operation = 'initialize'
    }
    elseif ([string]::IsNullOrWhiteSpace($SourceDatabase) -or [string]::IsNullOrWhiteSpace($SourceSettingsDirectory)) {
        throw 'Migration requires the exact existing database and settings directory; never replace missing legacy data with new storage.'
    }
    if ($MigrateSettingsOnly) { $operation = 'migrate-settings' }
    $request = @{operation=$operation;destinationDirectory=$target;writersQuiesced=$true;sourceDatabase=[string]$SourceDatabase;sourceSettingsDirectory=[string]$SourceSettingsDirectory}
    $result = Invoke-GaoAssistantStorage (Join-Path $ReleasePath 'web') $request
    Write-Output 'Storage prepared and verified. Source files and Data Protection keys remain in place. Configure the following three GaoAppPool settings before starting Web:'
    [pscustomobject]@{Assistant__StorageId=$result.storageId;Assistant__StoreSettingsDirectory=$result.settingsDirectory;CustomerService__DatabasePath=$result.databasePath}
}
finally { $lock.ReleaseMutex(); $lock.Dispose() }
