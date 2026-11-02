[CmdletBinding()]
param(
    [string]$AndroidSdk,
    [string]$OutputDirectory,
    [string]$Serial = 'emulator-5560',
    [string]$Python = 'python',
    [switch]$FixtureAlreadyInstalled
)
$ErrorActionPreference = 'Stop'
if ($Serial -notmatch '^emulator-\d+$') { throw 'Tests are restricted to emulators, never physical devices.' }
if (!$AndroidSdk) { $AndroidSdk = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\Android\Sdk' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))) '.artifacts\acb-android-probe' }
$adb = Join-Path $AndroidSdk 'platform-tools\adb.exe'
function Adb {
    param([string[]]$Arguments)
    $result = & $adb -s $Serial @Arguments
    if ($LASTEXITCODE -ne 0) { throw "ADB failed: $($Arguments -join ' ')" }
    return $result
}
function Export-AdbBytes {
    param([string]$Command, [string]$Path)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $adb; $start.Arguments = "-s $Serial exec-out $Command"
    $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($start)
    $output = [IO.File]::Create($Path)
    try { $process.StandardOutput.BaseStream.CopyTo($output) } finally { $output.Dispose() }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "ADB binary export failed: $Command" }
}
function Emit {
    param([string]$Case, [int]$Id)
    Adb @('shell','am','start','-f','0x30000000','-n','mobile.acb.com.vn/vn.gaoapp.acbprobe.fixture.FixtureActivity','--es','case',$Case,'--ei','id',"$Id") | Out-Null
    Start-Sleep -Milliseconds 900
}
$bank = Adb @('shell','pm','list','packages','mobile.acb.com.vn')
$installedByTest = $false
if (($bank -join "`n") -match '^package:mobile.acb.com.vn$') {
    # Never replace or remove a real bank installation. A test fixture is positively identified
    # by its activity, debug signing certificate and install path before opt-in reuse.
    if (!$FixtureAlreadyInstalled) { throw 'ACB package exists. Refusing to install a test source over it.' }
    $paths = @(Adb @('shell','pm','path','mobile.acb.com.vn'))
    $packagePath = @($paths | Where-Object { $_ -match '^package:/data/app/[^\s]+/base\.apk$' })
    if ($packagePath.Count -ne 1) { throw 'Cannot identify existing bank APK safely.' }
    $installedApk = Join-Path $OutputDirectory 'installed-fixture-check.apk'
    Adb @('pull', $packagePath[0].Substring(8), $installedApk) | Out-Null
    if ((Get-FileHash -LiteralPath $installedApk).Hash -ne (Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'TEST-ONLY-NOT-ACB-ONE.apk')).Hash) {
        throw 'Existing app does not match the owned test fixture APK. Refusing to continue.'
    }
    $installedByTest = $true
} else {
    Adb @('install',(Join-Path $OutputDirectory 'TEST-ONLY-NOT-ACB-ONE.apk')) | Out-Null
    $installedByTest = $true
}
try {
    Adb @('shell','pm','grant','mobile.acb.com.vn','android.permission.POST_NOTIFICATIONS') | Out-Null
    Adb @('shell','cmd','notification','allow_listener','vn.gaoapp.acbprobe/vn.gaoapp.acbprobe.AcbNotificationListener') | Out-Null
    Adb @('shell','am','start','-n','vn.gaoapp.acbprobe/.MainActivity') | Out-Null
    Start-Sleep -Seconds 2
    Emit merchant 1101
    Emit balance 1102
    Emit debit 1103
    Emit ambiguous 1104
    Emit hidden 1105
    Emit otp 1106
    Emit marketing 1107
    Emit group 1108
    Emit long 1109
    Emit truncated 1110
    Emit unknown 1113
    Emit custom-views 1114
    Emit custom-otp 1115
    Emit messaging 1116
    Emit ticker 1117
    Emit redacted-layout 1118
    Emit system-redacted 1119
    Emit burst 1200
    Adb @('shell','input','keyevent','223') | Out-Null
    Emit locked 1111
    Adb @('shell','input','keyevent','224') | Out-Null
    Adb @('shell','wm','dismiss-keyguard') | Out-Null
    Adb @('shell','cmd','connectivity','airplane-mode','enable') | Out-Null
    Emit offline 1112
    Adb @('shell','cmd','connectivity','airplane-mode','disable') | Out-Null
    Start-Sleep -Seconds 2
    Adb @('shell','am','force-stop','vn.gaoapp.acbprobe') | Out-Null
    $before = Join-Path $OutputDirectory 'emulator-before-restart.db'
    Export-AdbBytes 'run-as vn.gaoapp.acbprobe cat databases/acb-probe.db' $before
    & $Python (Join-Path $PSScriptRoot 'verify_emulator.py') $before --report (Join-Path $OutputDirectory 'emulator-before-restart.json')
    if ($LASTEXITCODE -ne 0) { throw 'Pre-restart checks failed.' }
    # Opening the app removes force-stop state before rebooting, as on a real installed app.
    Adb @('shell','am','start','-n','vn.gaoapp.acbprobe/.MainActivity') | Out-Null
    Start-Sleep -Seconds 1
    Adb @('reboot') | Out-Null
    Write-Output 'Restart sent. Run the after-restart checks once sys.boot_completed returns 1.'
} finally {
    # The fixture stays installed only during the reboot check. It is removed by FinishEmulatorTest.ps1.
    if ($installedByTest) { Write-Output 'Owned fake source remains until the after-restart test; never log in to this fixture.' }
}
