#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [Parameter(Mandatory)][string]$ExpectedManifestSha256,
    [Parameter(Mandatory)][string]$BackupRoot,
    [Parameter(Mandatory)][uri]$SmokeUrl,
    [switch]$WritersQuiesced
)
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$lock = Enter-GaoDeploymentLock
try {
    if (!$WritersQuiesced) { throw 'Confirm all other Assistant/CSKH writers are paused with -WritersQuiesced before activation.' }
    Assert-GaoPackage $ReleasePath $ExpectedManifestSha256 $false
    $context = Get-GaoDeploymentContext $ReleasePath $BackupRoot $SmokeUrl
    Assert-GaoPackage $ReleasePath $ExpectedManifestSha256 $false
    Invoke-GaoWebSwitch $context
}
finally { $lock.ReleaseMutex(); $lock.Dispose() }
