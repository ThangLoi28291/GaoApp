[CmdletBinding()]
param([string]$AndroidSdk, [string]$OutputDirectory, [string]$Serial = 'emulator-5560', [string]$Python = 'python')
$ErrorActionPreference = 'Stop'
if ($Serial -notmatch '^emulator-\d+$') { throw 'Tests are restricted to emulators.' }
if (!$AndroidSdk) { $AndroidSdk = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\Android\Sdk' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))) '.artifacts\acb-android-probe' }
$adb = Join-Path $AndroidSdk 'platform-tools\adb.exe'
function Adb { param([string[]]$Arguments); $result = & $adb -s $Serial @Arguments; if ($LASTEXITCODE -ne 0) { throw "ADB failed: $($Arguments -join ' ')" }; return $result }
function Export-AdbBytes {
    param([string]$Command, [string]$Path)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $adb; $start.Arguments = "-s $Serial exec-out $Command"; $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true; $start.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($start); $output = [IO.File]::Create($Path)
    try { $process.StandardOutput.BaseStream.CopyTo($output) } finally { $output.Dispose() }
    $process.WaitForExit(); if ($process.ExitCode -ne 0) { throw 'ADB binary export failed.' }
}
$boot = Adb @('shell','getprop','sys.boot_completed')
if (($boot -join '').Trim() -ne '1') { throw 'Emulator is still booting. Retry after sys.boot_completed returns 1.' }
$paths = @(Adb @('shell','pm','path','mobile.acb.com.vn'))
$packagePath = @($paths | Where-Object { $_ -match '^package:/data/app/[^\s]+/base\.apk$' })
if ($packagePath.Count -ne 1) { throw 'Cannot identify test source APK safely.' }
$installedApk = Join-Path $OutputDirectory 'installed-fixture-finish-check.apk'
Adb @('pull',$packagePath[0].Substring(8),$installedApk) | Out-Null
if ((Get-FileHash -LiteralPath $installedApk).Hash -ne (Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'TEST-ONLY-NOT-ACB-ONE.apk')).Hash) {
    throw 'Installed app is not the owned test fixture. Refusing to test or remove it.'
}
$passed = $false
try {
    Start-Sleep -Seconds 3
    Adb @('shell','am','start','-f','0x30000000','-n','mobile.acb.com.vn/vn.gaoapp.acbprobe.fixture.FixtureActivity','--es','case','after-restart','--ei','id','2021') | Out-Null
    Start-Sleep -Seconds 2
    Adb @('shell','am','start','-n','vn.gaoapp.acbprobe/.MainActivity') | Out-Null
    Start-Sleep -Seconds 1
    Adb @('shell','uiautomator','dump','/sdcard/gaoapp-probe-qa-ui.xml') | Out-Null
    Adb @('pull','/sdcard/gaoapp-probe-qa-ui.xml',(Join-Path $OutputDirectory 'ui-emulator-tested.xml')) | Out-Null
    Export-AdbBytes 'screencap -p' (Join-Path $OutputDirectory 'ui-emulator-tested.png')
    Adb @('shell','am','force-stop','vn.gaoapp.acbprobe') | Out-Null
    $database = Join-Path $OutputDirectory 'emulator-after-restart.db'
    Export-AdbBytes 'run-as vn.gaoapp.acbprobe cat databases/acb-probe.db' $database
    & $Python (Join-Path $PSScriptRoot 'verify_emulator.py') $database --before (Join-Path $OutputDirectory 'emulator-before-restart.db') --report (Join-Path $OutputDirectory 'emulator-acceptance.json')
    if ($LASTEXITCODE -ne 0) { throw 'After-restart checks failed.' }
    $passed = $true
} finally {
    Adb @('shell','cmd','connectivity','airplane-mode','disable') | Out-Null
    Adb @('uninstall','mobile.acb.com.vn') | Out-Null
    Write-Output 'Removed the SHA-256-verified fake source. Real ACB ONE can now be installed from Google Play.'
    if ($passed) {
        Adb @('shell','pm','clear','vn.gaoapp.acbprobe') | Out-Null
        Adb @('shell','am','start','-n','vn.gaoapp.acbprobe/.MainActivity') | Out-Null
        Write-Output 'Removed local sample evidence from the installed probe; QA snapshots remain in the workspace.'
    }
}
