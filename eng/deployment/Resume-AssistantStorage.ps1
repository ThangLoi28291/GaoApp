#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [Parameter(Mandatory)][string]$ExpectedManifestSha256,
    [Parameter(Mandatory)][string]$HoldEvidence,
    [switch]$WritersQuiesced
)
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$lock = Enter-GaoDeploymentLock
try {
    if (!$WritersQuiesced) { throw 'Stop all writers before continuity recovery.' }
    Import-Module WebAdministration -ErrorAction Stop
    if ((Get-WebAppPoolState -Name 'GaoAppPool').Value -ne 'Stopped') { throw 'GaoAppPool must remain stopped during recovery.' }
    Wait-GaoPoolStopped
    Assert-GaoPackage $ReleasePath $ExpectedManifestSha256 ([bool](Test-Path -LiteralPath (Join-Path $ReleasePath 'migrator')))
    $collection = Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.applicationHost/applicationPools/add[@name='GaoAppPool']/environmentVariables" -Name '.'
    $hostSettings = @{}; foreach ($entry in $collection.Collection) { $hostSettings[$entry.name] = $entry.value }
    Complete-GaoAssistantRecovery (Join-Path $ReleasePath 'web') $HoldEvidence $hostSettings $true
    Write-Output 'Continuity verified; owned hold removed. IIS is still stopped. Review schema/Web compatibility before starting. No data was restored or overwritten.'
}
finally { $lock.ReleaseMutex(); $lock.Dispose() }
