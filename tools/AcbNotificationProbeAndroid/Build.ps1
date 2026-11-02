[CmdletBinding()]
param(
    [string]$AndroidSdk,
    [string]$JavaHome,
    [string]$OutputDirectory,
    [string]$Platform,
    [string]$BuildToolsVersion,
    [string]$VersionName = '0.2.5-bridge',
    [int]$VersionCode = 12
)
$ErrorActionPreference = 'Stop'
if ($VersionName -notmatch '^\d+\.\d+\.\d+(-[a-z0-9]+)?$' -or $VersionCode -lt 1) { throw 'Invalid APK version.' }
$projectDirectory = $PSScriptRoot
$repositoryDirectory = [IO.Path]::GetFullPath((Join-Path $projectDirectory '..\..'))
if (!$AndroidSdk) {
    $AndroidSdk = $env:ANDROID_SDK_ROOT
    if (!$AndroidSdk) { $AndroidSdk = $env:ANDROID_HOME }
    if (!$AndroidSdk) { $AndroidSdk = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\Android\Sdk' }
}
if (!$JavaHome) {
    $JavaHome = $env:JAVA_HOME
    if (!$JavaHome) { $JavaHome = Join-Path $env:ProgramFiles 'Android\Android Studio\jbr' }
}
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repositoryDirectory '.artifacts\acb-android-probe' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (!$Platform) {
    $Platform = Get-ChildItem -LiteralPath (Join-Path $AndroidSdk 'platforms') -Directory |
        Where-Object { $_.Name -match '^android-\d+(\.\d+)?$' } |
        Sort-Object { [version]($_.Name.Replace('android-', '') + $(if ($_.Name -notmatch '\.') { '.0' } else { '' })) } -Descending |
        Select-Object -First 1 -ExpandProperty Name
}
if (!$BuildToolsVersion) {
    $BuildToolsVersion = Get-ChildItem -LiteralPath (Join-Path $AndroidSdk 'build-tools') -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty Name
}
$androidJar = Join-Path $AndroidSdk "platforms\$Platform\android.jar"
$buildTools = Join-Path $AndroidSdk "build-tools\$BuildToolsVersion"
$java = Join-Path $JavaHome 'bin\java.exe'
$javac = Join-Path $JavaHome 'bin\javac.exe'
$jar = Join-Path $JavaHome 'bin\jar.exe'
$keytool = Join-Path $JavaHome 'bin\keytool.exe'
$aapt2 = Join-Path $buildTools 'aapt2.exe'
$aapt = Join-Path $buildTools 'aapt.exe'
$zipalign = Join-Path $buildTools 'zipalign.exe'
$d8 = Join-Path $buildTools 'lib\d8.jar'
$signer = Join-Path $buildTools 'lib\apksigner.jar'
foreach ($required in @($androidJar, $java, $javac, $jar, $keytool, $aapt2, $aapt, $zipalign, $d8, $signer)) {
    if (!(Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing Android/JDK tool: $required" }
}
function Invoke-ProbeTool {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Executable)) failed (exit $LASTEXITCODE)" }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$workDirectory = Join-Path $OutputDirectory ('build-' + [Guid]::NewGuid().ToString('N'))
$classes = Join-Path $workDirectory 'classes'
$generated = Join-Path $workDirectory 'generated'
$dex = Join-Path $workDirectory 'dex'
$testClasses = Join-Path $workDirectory 'test-classes'
foreach ($directory in @($classes, $generated, $dex, $testClasses)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$main = Join-Path $projectDirectory 'app\src\main'
$parser = Join-Path $main 'java\vn\gaoapp\acbprobe\TransferProbeParser.java'
$tests = Join-Path $projectDirectory 'tests\TransferProbeParserTests.java'
Invoke-ProbeTool $javac @('--release', '8', '-encoding', 'UTF-8', '-d', $testClasses, $parser, $tests)
Invoke-ProbeTool $java @('-cp', $testClasses, 'vn.gaoapp.acbprobe.TransferProbeParserTests')
$protocol = Join-Path $main 'java\vn\gaoapp\acbprobe\CompanionProtocol.java'
$protocolTests = Join-Path $projectDirectory 'tests\CompanionProtocolTests.java'
Invoke-ProbeTool $javac @('--release', '8', '-encoding', 'UTF-8', '-d', $testClasses, $protocol, $protocolTests)
Invoke-ProbeTool $java @('-cp', $testClasses, 'vn.gaoapp.acbprobe.CompanionProtocolTests')
Invoke-ProbeTool $javac @('--release', '8', '-encoding', 'UTF-8', '-d', $testClasses, $parser,
    (Join-Path $main 'java\vn\gaoapp\acbprobe\WebBridgeProtocol.java'), (Join-Path $projectDirectory 'tests\WebBridgeProtocolTests.java'))
Invoke-ProbeTool $java @('-cp', $testClasses, 'vn.gaoapp.acbprobe.WebBridgeProtocolTests')
Invoke-ProbeTool $javac @('--release', '8', '-encoding', 'UTF-8', '-cp', $testClasses, '-d', $testClasses,
    (Join-Path $projectDirectory 'tests\RecognitionGoldenTests.java'))
Invoke-ProbeTool $java @('-cp', $testClasses, 'vn.gaoapp.acbprobe.RecognitionGoldenTests',
    (Join-Path $projectDirectory 'tests\recognition-golden.tsv'))
Invoke-ProbeTool $javac @('--release', '8', '-encoding', 'UTF-8', '-d', $testClasses,
    (Join-Path $main 'java\vn\gaoapp\acbprobe\ResilientRelayLoop.java'),
    (Join-Path $main 'java\vn\gaoapp\acbprobe\RelayRecoveryPolicy.java'),
    (Join-Path $main 'java\vn\gaoapp\acbprobe\RelayRequestDeadline.java'),
    (Join-Path $projectDirectory 'tests\ResilientRelayLoopTests.java'))
Invoke-ProbeTool $java @('-cp', $testClasses, 'vn.gaoapp.acbprobe.ResilientRelayLoopTests')
$resources = Join-Path $workDirectory 'resources.zip'
$unsigned = Join-Path $workDirectory 'probe-unsigned.apk'
$aligned = Join-Path $workDirectory 'probe-aligned.apk'
$classesJar = Join-Path $workDirectory 'classes.jar'
Invoke-ProbeTool $aapt2 @('compile', '--dir', (Join-Path $main 'res'), '-o', $resources)
Invoke-ProbeTool $aapt2 @('link', '-I', $androidJar, '--manifest', (Join-Path $main 'AndroidManifest.xml'),
    '--min-sdk-version', '26', '--target-sdk-version', '35', '--version-code', "$VersionCode", '--version-name', $VersionName,
    '--java', $generated, '-o', $unsigned, $resources)
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $main 'java') -Recurse -Filter '*.java' | Select-Object -ExpandProperty FullName)
$sourceFiles += @(Get-ChildItem -LiteralPath $generated -Recurse -Filter '*.java' | Select-Object -ExpandProperty FullName)
$sourceList = Join-Path $workDirectory 'javac-sources.txt'
[IO.File]::WriteAllLines($sourceList, @($sourceFiles | ForEach-Object { '"' + $_.Replace('\', '/') + '"' }), [Text.UTF8Encoding]::new($false))
Invoke-ProbeTool $javac @('--release', '8', '-encoding', 'UTF-8', '-classpath', $androidJar, '-d', $classes, ('@' + $sourceList))
Invoke-ProbeTool $jar @('--create', '--file', $classesJar, '-C', $classes, '.')
Invoke-ProbeTool $java @('-cp', $d8, 'com.android.tools.r8.D8', '--min-api', '26', '--lib', $androidJar, '--output', $dex, $classesJar)
Invoke-ProbeTool $jar @('--update', '--file', $unsigned, '-C', $dex, 'classes.dex')
Invoke-ProbeTool $zipalign @('-f', '-p', '4', $unsigned, $aligned)
$keystore = Join-Path $OutputDirectory 'probe-debug.keystore'
if (!(Test-Path -LiteralPath $keystore)) {
    # Public Android debug credentials; this key must never be used for a production release.
    Invoke-ProbeTool $keytool @('-genkeypair', '-keystore', $keystore, '-storepass', 'android', '-keypass', 'android',
        '-alias', 'gaoprobe', '-keyalg', 'RSA', '-keysize', '2048', '-validity', '3650',
        '-dname', 'CN=GaoApp ACB Probe Test, O=GaoApp, C=VN')
}
$bridgeBuild = $VersionName.EndsWith('-bridge')
$apkBaseName = $(if ($bridgeBuild) { 'GaoApp-AcbProbe-bridge' } else { 'GaoApp-AcbProbe-step1' })
$apkFileName = $apkBaseName + '-' + ($VersionName -replace '-(step1|bridge)$', '') + '.apk'
$apk = Join-Path $OutputDirectory $apkFileName
Invoke-ProbeTool $java @('-jar', $signer, 'sign', '--ks', $keystore, '--ks-key-alias', 'gaoprobe',
    '--ks-pass', 'pass:android', '--key-pass', 'pass:android', '--out', $apk, $aligned)
Invoke-ProbeTool $java @('-jar', $signer, 'verify', '--verbose', $apk)
Invoke-ProbeTool $zipalign @('-c', '4', $apk)
$badging = & $aapt dump badging $apk
if ($LASTEXITCODE -ne 0) { throw 'APK inspection failed.' }
if (($badging -join "`n") -match "uses-permission: name='android.permission.(READ_SMS|SEND_SMS|READ_CONTACTS|CAMERA)'") {
    throw 'Unexpected permission in local observation APK.'
}
if (!$bridgeBuild -and ($badging -join "`n") -match "uses-permission: name='android.permission.INTERNET'") { throw 'Network access requires an explicit bridge build version.' }
$badging | Select-String -Pattern 'package:|sdkVersion:|targetSdkVersion:|application-label:|launchable-activity:'
$hash = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $OutputDirectory ($apkFileName -replace '\.apk$', '.sha256.txt')), "$hash  $apkFileName`r`n", [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $apk -Destination (Join-Path $OutputDirectory ($apkBaseName + '.apk')) -Force
[IO.File]::WriteAllText((Join-Path $OutputDirectory ($apkBaseName + '.sha256.txt')), "$hash  $apkBaseName.apk`r`n", [Text.UTF8Encoding]::new($false))
Write-Output "APK: $apk"
Write-Output "SHA256: $hash"
Write-Output "Toolchain: $Platform / build-tools $BuildToolsVersion / $JavaHome"
