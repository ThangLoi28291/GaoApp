#requires -Version 7.0
# Prepares a NEW private directory and path-only updates. Never modifies source
# credentials, IIS or the existing host environment file. Run elevated on the host.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentFile,
    [Parameter(Mandatory)][string]$SourceWebPath,
    [Parameter(Mandatory)][string]$PreparedWebPath,
    [Parameter(Mandatory)][string]$DestinationDirectory,
    [Parameter(Mandatory)][string[]]$PublicRoots
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$environment=Get-Content -LiteralPath $EnvironmentFile -Raw | ConvertFrom-Json -AsHashtable
$roots=@($PublicRoots)+@(Get-GaoIisPublicRoots)
[void](New-GaoChannelCredentialCopy $environment $SourceWebPath $PreparedWebPath $DestinationDirectory $roots)
Write-Output 'Prepared and verified private credential copies plus path-only channel-path-updates.json. Apply the reviewed paths to GaoAppPool before activating the new release. Source files and IIS were not changed.'
