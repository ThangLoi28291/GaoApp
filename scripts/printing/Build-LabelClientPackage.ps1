param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $workspace 'GaoApp.LabelPrintClient/GaoApp.LabelPrintClient.csproj'
$packageDir = Join-Path $workspace 'GaoApp.Web/ClientPackages'
$publishDir = Join-Path $workspace ('Logs/label-client-package/' + [Guid]::NewGuid().ToString('N'))
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
dotnet publish $project -c Release -r win-x64 --self-contained $selfContained --artifacts-path (Join-Path $publishDir 'build') -o (Join-Path $publishDir 'publish') -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -warnaserror:NU1901,NU1902,NU1903,NU1904 -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false
if ($LASTEXITCODE -ne 0) { throw 'Build bo ket noi client that bai.' }
$publishDir = Join-Path $publishDir 'publish'
$blankConfig = Get-Content -LiteralPath (Join-Path $publishDir 'appsettings.json') -Raw | ConvertFrom-Json
if ($blankConfig.LabelClient.PairingToken -or $blankConfig.LabelClient.AllowedOrigins.Count -gt 0) { throw 'Go client phai de trong ma ket noi va dia chi cua hang.' }
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
$zipPath = Join-Path $packageDir 'GaoApp.LabelPrintClient-win-x64.zip'
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $publishDir -File).FullName -DestinationPath $zipPath -Force
# Include runtime subdirectories too (e.g. native dependencies).
$folders = Get-ChildItem -LiteralPath $publishDir -Directory
if ($folders) { Compress-Archive -LiteralPath $folders.FullName -DestinationPath $zipPath -Update }
Write-Host ('Da tao goi: ' + $zipPath)
