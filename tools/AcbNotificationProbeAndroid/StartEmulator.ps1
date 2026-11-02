[CmdletBinding()]
param(
    [string]$AndroidSdk,
    [string]$OutputDirectory,
    [int]$Port = 5560,
    [string]$AvdName = 'GaoApp_AcbProbe',
    [switch]$ShowWindow
)
$ErrorActionPreference = 'Stop'
if ($AvdName -notmatch '^[A-Za-z][A-Za-z0-9_]{1,63}$') { throw 'Invalid isolated AVD name.' }
if (!$AndroidSdk) { $AndroidSdk = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\Android\Sdk' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) '.artifacts\acb-android-probe' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$image = Get-ChildItem -LiteralPath (Join-Path $AndroidSdk 'system-images') -Recurse -Filter 'system.img' |
    Where-Object { $_.FullName -match 'google_apis_playstore' } | Select-Object -First 1
if (!$image) { throw 'Cần cài system image có Google Play trong Android Studio → SDK Manager.' }
$relativeImage = $image.Directory.FullName.Substring($AndroidSdk.TrimEnd('\').Length + 1) + '\'
$target = $relativeImage.Split('\')[1]
$avdRoot = Join-Path $OutputDirectory 'avd'
$avdPath = Join-Path $avdRoot ($AvdName + '.avd')
New-Item -ItemType Directory -Path $avdPath -Force | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
if (!(Test-Path -LiteralPath (Join-Path $avdPath 'config.ini'))) {
    $config = @"
AvdId=$AvdName
avd.ini.displayname=GaoApp ACB Step 1
PlayStore.enabled=true
abi.type=x86_64
disk.dataPartition.size=4G
hw.accelerometer=yes
hw.audioInput=no
hw.battery=yes
hw.cpu.arch=x86_64
hw.cpu.ncore=2
hw.device.manufacturer=Google
hw.device.name=pixel_7
hw.gps=no
hw.gpu.enabled=yes
hw.gpu.mode=swiftshader
hw.keyboard=yes
hw.lcd.density=420
hw.lcd.height=2400
hw.lcd.width=1080
hw.mainKeys=no
hw.ramSize=2048
hw.sensors.orientation=yes
hw.sensors.proximity=yes
hw.camera.back=emulated
hw.camera.front=emulated
image.sysdir.1=$relativeImage
runtime.network.latency=none
runtime.network.speed=full
showDeviceFrame=no
tag.display=Google Play
tag.id=google_apis_playstore
"@
    [IO.File]::WriteAllText((Join-Path $avdPath 'config.ini'), $config, $encoding)
    [IO.File]::WriteAllText((Join-Path $avdRoot ($AvdName + '.ini')), "avd.ini.encoding=UTF-8`npath=$avdPath`ntarget=$target`n", $encoding)
}
$emulator = Join-Path $AndroidSdk 'emulator\emulator.exe'
$adb = Join-Path $AndroidSdk 'platform-tools\adb.exe'
if (!(Test-Path -LiteralPath $emulator) -or !(Test-Path -LiteralPath $adb)) { throw 'Missing Android emulator or ADB.' }
& $adb start-server
if ($LASTEXITCODE -ne 0) { throw 'ADB server did not start.' }
$devices = & $adb devices
if (($devices -join "`n") -match "emulator-$Port\s") { throw "emulator-$Port is already running. Use that device or select another port." }
$arguments = @('-avd', $AvdName, '-port', "$Port", '-no-snapshot', '-no-audio', '-gpu', 'swiftshader', '-camera-back', 'emulated', '-camera-front', 'emulated')
if (!$ShowWindow) { $arguments += '-no-window' }
$oldAvdRoot = $env:ANDROID_AVD_HOME
$oldSdkRoot = $env:ANDROID_SDK_ROOT
try {
    $env:ANDROID_AVD_HOME = $avdRoot
    $env:ANDROID_SDK_ROOT = $AndroidSdk
    $startOptions = @{
        FilePath = $emulator; ArgumentList = $arguments; PassThru = $true
        WindowStyle = $(if ($ShowWindow) { 'Normal' } else { 'Hidden' })
        RedirectStandardOutput = (Join-Path $OutputDirectory 'emulator-stdout.log')
        RedirectStandardError = (Join-Path $OutputDirectory 'emulator-stderr.log')
    }
    $process = Start-Process @startOptions
    Write-Output "Emulator PID: $($process.Id)"
    Write-Output "Device: emulator-$Port"
    Write-Output "AVD data: $avdPath"
} finally {
    $env:ANDROID_AVD_HOME = $oldAvdRoot
    $env:ANDROID_SDK_ROOT = $oldSdkRoot
}
