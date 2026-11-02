[CmdletBinding()]
param([string]$AndroidSdk, [string]$JavaHome, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (!$AndroidSdk) { $AndroidSdk = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\Android\Sdk' }
if (!$JavaHome) { $JavaHome = Join-Path $env:ProgramFiles 'Android\Android Studio\jbr' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))) '.artifacts\acb-android-probe' }
$platform = Get-ChildItem -LiteralPath (Join-Path $AndroidSdk 'platforms') -Directory | Where-Object Name -Match '^android-\d+(\.\d+)?$' | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name
$version = Get-ChildItem -LiteralPath (Join-Path $AndroidSdk 'build-tools') -Directory | Where-Object Name -Match '^\d+\.\d+\.\d+$' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1 -ExpandProperty Name
$tools = Join-Path $AndroidSdk "build-tools\$version"
$androidJar = Join-Path $AndroidSdk "platforms\$platform\android.jar"
$java = Join-Path $JavaHome 'bin\java.exe'
function Run { param([string]$Exe, [string[]]$ArgsList); & $Exe @ArgsList; if ($LASTEXITCODE -ne 0) { throw "$Exe exited $LASTEXITCODE" } }
$work = Join-Path $OutputDirectory ('fixture-' + [Guid]::NewGuid().ToString('N'))
$classes = Join-Path $work 'classes'; $dex = Join-Path $work 'dex'; $generated = Join-Path $work 'generated'
New-Item -ItemType Directory -Path $classes,$dex,$generated -Force | Out-Null
$unsigned = Join-Path $work 'unsigned.apk'; $aligned = Join-Path $work 'aligned.apk'; $classesJar = Join-Path $work 'classes.jar'
$resources = Join-Path $work 'resources.zip'
Run (Join-Path $tools 'aapt2.exe') @('compile', '--dir', (Join-Path $PSScriptRoot 'fixture\res'), '-o', $resources)
Run (Join-Path $tools 'aapt2.exe') @('link', '-I', $androidJar, '--manifest', (Join-Path $PSScriptRoot 'fixture\AndroidManifest.xml'), '--java', $generated, '-o', $unsigned, $resources)
Run (Join-Path $JavaHome 'bin\javac.exe') @('--release', '8', '-encoding', 'UTF-8', '-cp', $androidJar, '-d', $classes, (Join-Path $PSScriptRoot 'fixture\FixtureActivity.java'), (Join-Path $generated 'mobile\acb\com\vn\R.java'))
Run (Join-Path $JavaHome 'bin\jar.exe') @('--create', '--file', $classesJar, '-C', $classes, '.')
Run $java @('-cp', (Join-Path $tools 'lib\d8.jar'), 'com.android.tools.r8.D8', '--min-api', '26', '--lib', $androidJar, '--output', $dex, $classesJar)
Run (Join-Path $JavaHome 'bin\jar.exe') @('--update', '--file', $unsigned, '-C', $dex, 'classes.dex')
Run (Join-Path $tools 'zipalign.exe') @('-f', '4', $unsigned, $aligned)
$apk = Join-Path $OutputDirectory 'TEST-ONLY-NOT-ACB-ONE.apk'
Run $java @('-jar', (Join-Path $tools 'lib\apksigner.jar'), 'sign', '--ks', (Join-Path $OutputDirectory 'probe-debug.keystore'),
    '--ks-key-alias', 'gaoprobe', '--ks-pass', 'pass:android', '--key-pass', 'pass:android', '--out', $apk, $aligned)
Write-Output "Test-only fixture: $apk (NEVER distribute as ACB ONE)"
