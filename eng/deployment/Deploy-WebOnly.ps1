#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [Parameter(Mandatory)][string]$ExpectedManifestSha256,
    [Parameter(Mandatory)][string]$BackupRoot,
    [Parameter(Mandatory)][uri]$SmokeUrl
)
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$lock = Enter-GaoDeploymentLock
try {
    Assert-GaoPackage $ReleasePath $ExpectedManifestSha256 $false
    $context = Get-GaoDeploymentContext $ReleasePath $BackupRoot $SmokeUrl
    Assert-GaoPackage $ReleasePath $ExpectedManifestSha256 $false
    Invoke-GaoWebSwitch $context
}
finally { $lock.ReleaseMutex(); $lock.Dispose() }
