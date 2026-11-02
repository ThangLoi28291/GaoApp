#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [string]$DotnetPath = (Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'),
    [string]$IisAppPoolName = ''
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'test-release-package.ps1') -ReleasePath $ReleasePath
. (Join-Path $PSScriptRoot '../eng/deployment/RuntimeSupport.ps1')
if (![Environment]::Is64BitProcess) { throw 'Run the win-x64 release preflight from 64-bit PowerShell.' }
if (!(Test-Path -LiteralPath $DotnetPath -PathType Leaf)) { throw 'The reviewed x64 dotnet host is missing.' }
$installed = @(& $DotnetPath --list-runtimes)
if ($LASTEXITCODE -ne 0) { throw 'Unable to read installed runtimes from the selected host.' }
$releaseRoot = (Resolve-Path -LiteralPath $ReleasePath).Path
foreach ($component in @('Web', 'Migrator', 'AutoInvoiceWorker', 'LabelPrintServer')) {
    $config = Join-Path $releaseRoot "$($component.ToLowerInvariant())/GaoApp.$component.runtimeconfig.json"
    if (Test-Path -LiteralPath $config) {
        Assert-GaoAppInstalledRuntimes (Get-Content -LiteralPath $config -Raw | ConvertFrom-Json) $installed
    }
}
if ($IisAppPoolName) {
    Import-Module WebAdministration -ErrorAction Stop
    if (!(Test-Path -LiteralPath "IIS:\AppPools\$IisAppPoolName")) { throw 'The reviewed IIS application pool does not exist.' }
    $pool = Get-Item -LiteralPath "IIS:\AppPools\$IisAppPoolName"
    if ($pool.enable32BitAppOnWin64) { throw 'This win-x64 release requires a 64-bit IIS pool; review the existing pool before activation.' }
    $module = @(Get-WebGlobalModule | Where-Object Name -eq 'AspNetCoreModuleV2')
    if ($module.Count -ne 1 -or !(Test-Path -LiteralPath ([Environment]::ExpandEnvironmentVariables($module[0].Image)))) {
        throw 'ASP.NET Core Hosting Bundle / AspNetCoreModuleV2 is missing. Install the reviewed .NET 10 bundle in the deployment window.'
    }
    $requirements = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../eng/deployment/net10-host-requirements.json') -Raw | ConvertFrom-Json
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo([Environment]::ExpandEnvironmentVariables($module[0].Image))
    $installedModule = [Version]::new($fileVersion.FileMajorPart, $fileVersion.FileMinorPart, $fileVersion.FileBuildPart, $fileVersion.FilePrivatePart)
    if ($installedModule -lt [Version]$requirements.minimumIisModuleVersion) {
        throw "ASP.NET Core IIS module is behind the reviewed Hosting Bundle baseline: >= $($requirements.minimumIisModuleVersion) required."
    }
}
Write-Host 'PASS: selected x64 host satisfies every packaged .NET/ASP.NET 10 servicing requirement.'
if ($IisAppPoolName) { Write-Host 'PASS: IIS pool architecture and ASP.NET Core module registration.' }
Write-Host 'Read-only preflight; no runtime installation, IIS restart, SQL connection or service change was performed.'
