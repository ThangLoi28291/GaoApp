#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [Parameter(Mandatory)][string]$ExpectedManifestSha256,
    [Parameter(Mandatory)][string]$BackupRoot,
    [Parameter(Mandatory)][uri]$SmokeUrl,
    [ValidateSet('Production','Test')][string]$TargetEnvironment = 'Production',
    [string]$DatabaseBackupEvidence,
    [switch]$HumanTestBackupWaiver,
    [Parameter(Mandatory)][string]$ReviewedMigrationEvidence,
    [switch]$WritersQuiesced
)
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
. (Join-Path $PSScriptRoot 'SchemaMigration.ps1')
$lock = Enter-GaoDeploymentLock
try {
    if (!$WritersQuiesced) { throw 'Human must confirm other GaoApp writers/jobs are paused before schema migration.' }
    Assert-GaoPackage $ReleasePath $ExpectedManifestSha256 $true
    $context = Get-GaoDeploymentContext $ReleasePath $BackupRoot $SmokeUrl
    Assert-GaoSchemaApproval $TargetEnvironment $DatabaseBackupEvidence $HumanTestBackupWaiver $ReviewedMigrationEvidence $ExpectedManifestSha256 $context.PoolConnection
    Invoke-GaoSchemaDeployment $context $ReleasePath $ExpectedManifestSha256
}
finally { $lock.ReleaseMutex(); $lock.Dispose() }
