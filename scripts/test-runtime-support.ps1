#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../eng/deployment/RuntimeSupport.ps1')
$checks = 0
function Runtime-Config([string]$version = '10.0.13', [string]$tfm = 'net10.0') {
    @{ runtimeOptions = @{ tfm=$tfm; rollForward='LatestPatch'; frameworks=@(
        @{name='Microsoft.NETCore.App';version=$version}, @{name='Microsoft.AspNetCore.App';version=$version}
    ) } } | ConvertTo-Json -Depth 8 | ConvertFrom-Json
}
function Accept([string]$name, [scriptblock]$action) { & $action; $script:checks++; Write-Host "PASS $name" }
function Reject([string]$name, [scriptblock]$action, [string]$reason) {
    try { & $action; throw 'EXPECTED_REJECTION_NOT_RAISED' }
    catch { if ($_.Exception.Message -notlike "*$reason*") { throw } }
    $script:checks++; Write-Host "PASS $name"
}
$current = Runtime-Config
Accept 'source SDK and runtime floor match deployment requirements' {
    $repoRoot = Split-Path $PSScriptRoot -Parent
    $requirements = Get-Content -LiteralPath (Join-Path $repoRoot 'eng/deployment/net10-host-requirements.json') -Raw | ConvertFrom-Json
    $global = Get-Content -LiteralPath (Join-Path $repoRoot 'global.json') -Raw | ConvertFrom-Json
    [xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
    if ($global.sdk.version -ne $requirements.sdk -or $props.Project.PropertyGroup.RuntimeFrameworkVersion -ne $requirements.minimumRuntime) { throw 'Source and host servicing baselines differ.' }
}
$installed = @('Microsoft.NETCore.App 10.0.13 [C:\dotnet]', 'Microsoft.AspNetCore.App 10.0.13 [C:\dotnet]')
Accept 'reviewed runtime floor' { Assert-GaoAppInstalledRuntimes $current $installed }
Accept 'later servicing patch' { Assert-GaoAppInstalledRuntimes $current ($installed.Replace('10.0.13', '10.0.14')) }
Reject 'old .NET 8 release' { Assert-GaoAppRuntimeConfiguration (Runtime-Config '8.0.31' 'net8.0') } 'must target net10.0'
Reject 'old .NET 10 release' { Assert-GaoAppRuntimeConfiguration (Runtime-Config '10.0.12') } 'reviewed .NET'
Reject 'preview release' { Assert-GaoAppRuntimeConfiguration (Runtime-Config '10.0.14-preview.1') } 'reviewed .NET'
Reject 'host has only .NET 8' { Assert-GaoAppInstalledRuntimes $current ($installed.Replace('10.0.13', '8.0.31')) } 'Missing serviced runtime'
Reject 'host is behind by one patch' { Assert-GaoAppInstalledRuntimes $current ($installed.Replace('10.0.13', '10.0.12')) } 'Missing serviced runtime'
Reject 'host lacks ASP.NET' { Assert-GaoAppInstalledRuntimes $current @($installed[0]) } 'Missing serviced runtime'
Reject 'preview installed on host' { Assert-GaoAppInstalledRuntimes $current ($installed.Replace('10.0.13', '10.0.14-preview.1')) } 'Missing serviced runtime'
Reject 'future major is not automatic compatibility' { Assert-GaoAppInstalledRuntimes $current ($installed.Replace('10.0.13', '11.0.13')) } 'Missing serviced runtime'
$unsafe = Runtime-Config; $unsafe.runtimeOptions.rollForward = 'Major'
Reject 'unreviewed major roll forward' { Assert-GaoAppRuntimeConfiguration $unsafe } 'LatestPatch'
$hybrid = Runtime-Config
$hybrid.runtimeOptions | Add-Member -NotePropertyName includedFrameworks -NotePropertyValue $current.runtimeOptions.frameworks
Reject 'mixed self-contained and shared declarations cannot bypass roll-forward policy' { Assert-GaoAppRuntimeConfiguration $hybrid } 'exactly one deployment mode'
$hybrid = Runtime-Config
$hybrid.runtimeOptions | Add-Member -NotePropertyName framework -NotePropertyValue $current.runtimeOptions.frameworks[0]
Reject 'ambiguous shared framework declarations' { Assert-GaoAppRuntimeConfiguration $hybrid } 'exactly one deployment mode'
$selfContained = @{ runtimeOptions=@{ tfm='net10.0'; includedFrameworks=$current.runtimeOptions.frameworks } } | ConvertTo-Json -Depth 8 | ConvertFrom-Json
Accept 'self-contained client validates bundled runtime' { Assert-GaoAppInstalledRuntimes $selfContained @('No installed shared frameworks') }
$selfContained.runtimeOptions.includedFrameworks[1].version = '10.0.12'
Reject 'self-contained package with old ASP.NET runtime' { Assert-GaoAppRuntimeConfiguration $selfContained } 'reviewed .NET'
Write-Host "RESULT: $checks/$checks runtime support checks passed."
