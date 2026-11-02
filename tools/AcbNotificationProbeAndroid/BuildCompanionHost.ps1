[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) '.artifacts\acb-android-probe' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskHostExe = Join-Path $OutputDirectory 'GaoApp-Companion.exe'
$taskCheckExe = Join-Path $OutputDirectory 'GaoApp-Companion-checks.exe'
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$taskHostExe" (Join-Path $PSScriptRoot 'desktop\CompanionHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Companion helper compilation failed.' }
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$taskCheckExe" (Join-Path $PSScriptRoot 'desktop\CompanionHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Companion check executable compilation failed.' }
& $taskCheckExe --self-test
if ($LASTEXITCODE -ne 0) { throw 'Companion helper self-test failed.' }
Write-Output "Helper: $taskHostExe"
