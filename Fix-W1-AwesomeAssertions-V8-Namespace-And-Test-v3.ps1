param(
    [string]$SourceRoot = "D:\Datacode",
    [string]$PackageVersion = "8.0.2"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Stop-Fail {
    param([string]$Message)

    Write-Host ""
    Write-Host "FAIL: $Message" -ForegroundColor Red
    exit 1
}

function Run-DotNet {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [string]$LogPath
    )

    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan

    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $LogPath
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        Stop-Fail "$Name thất bại. Exit code=$exitCode. Xem log: $LogPath"
    }
}

if (-not (Test-Path $SourceRoot -PathType Container)) {
    Stop-Fail "Không tìm thấy thư mục source: $SourceRoot"
}

$solutionPath = Join-Path $SourceRoot "GaoApp.sln"
$testsRoot = Join-Path $SourceRoot "GaoApp.Tests"
$projectPath = Join-Path $testsRoot "GaoApp.Tests.csproj"

if (-not (Test-Path $solutionPath -PathType Leaf)) {
    Stop-Fail "Không tìm thấy solution: $solutionPath"
}

if (-not (Test-Path $projectPath -PathType Leaf)) {
    Stop-Fail "Không tìm thấy test project: $projectPath"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-Fail "Không tìm thấy dotnet trong PATH."
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = "D:\Backup\GaoApp-W1-Fix2-$timestamp"
$backupTestsRoot = Join-Path $backupRoot "GaoApp.Tests"
$logRoot = Join-Path $SourceRoot "TestResults\W1-Fix2-$timestamp"

New-Item -ItemType Directory -Force $backupTestsRoot | Out-Null
New-Item -ItemType Directory -Force $logRoot | Out-Null

Write-Host "=== Backup GaoApp.Tests trước Fix2 ===" -ForegroundColor Cyan

& robocopy `
    $testsRoot `
    $backupTestsRoot `
    /E `
    /COPY:DAT `
    /R:2 `
    /W:1 `
    /XD bin obj TestResults `
    /XF *.trx *.coverage *.coveragexml

$robocopyExit = $LASTEXITCODE

if ($robocopyExit -gt 7) {
    Stop-Fail "Backup thất bại. Robocopy exit code=$robocopyExit"
}

Write-Host "Backup: $backupRoot" -ForegroundColor Green

Push-Location $SourceRoot
try {
    Write-Host ""
    Write-Host "=== Kiểm tra PackageReference ===" -ForegroundColor Cyan

    $projectText = [System.IO.File]::ReadAllText($projectPath)

    $awesomePackagePattern = '<PackageReference\s+Include="AwesomeAssertions"\s+Version="' +
        [regex]::Escape($PackageVersion) + '"\s*/>'

    if ($projectText -notmatch $awesomePackagePattern) {
        Stop-Fail "GaoApp.Tests.csproj không chứa AwesomeAssertions $PackageVersion."
    }

    if ($projectText -match '<PackageReference\s+Include="FluentAssertions"') {
        Stop-Fail "GaoApp.Tests.csproj vẫn còn PackageReference FluentAssertions."
    }

    Write-Host "PackageReference AwesomeAssertions ${PackageVersion}: PASS" -ForegroundColor Green
    Write-Host "PackageReference FluentAssertions: KHÔNG CÓ" -ForegroundColor Green

    Write-Host ""
    Write-Host "=== Sửa namespace tương thích AwesomeAssertions 8.x ===" -ForegroundColor Cyan

    $testFiles = @(
        Get-ChildItem $testsRoot -Recurse -File -Filter "*.cs"
    )

    $awesomeUsingBefore = @(
        $testFiles |
            Select-String -SimpleMatch "using AwesomeAssertions;"
    )

    if ($awesomeUsingBefore.Count -eq 0) {
        Write-Host "Không còn dòng 'using AwesomeAssertions;' cần đổi." -ForegroundColor Yellow
    }
    else {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

        foreach ($file in $testFiles) {
            $content = [System.IO.File]::ReadAllText($file.FullName)

            if ($content.Contains("using AwesomeAssertions;")) {
                $updated = $content.Replace(
                    "using AwesomeAssertions;",
                    "using FluentAssertions;"
                )

                [System.IO.File]::WriteAllText(
                    $file.FullName,
                    $updated,
                    $utf8NoBom
                )
            }
        }
    }

    $awesomeUsingAfter = @(
        Get-ChildItem $testsRoot -Recurse -File -Filter "*.cs" |
            Select-String -SimpleMatch "using AwesomeAssertions;"
    )

    $fluentUsingAfter = @(
        Get-ChildItem $testsRoot -Recurse -File -Filter "*.cs" |
            Select-String -SimpleMatch "using FluentAssertions;"
    )

    if ($awesomeUsingAfter.Count -ne 0) {
        Stop-Fail "Vẫn còn $($awesomeUsingAfter.Count) dòng using AwesomeAssertions."
    }

    if ($fluentUsingAfter.Count -eq 0) {
        Stop-Fail "Không tìm thấy namespace FluentAssertions sau khi sửa."
    }

    Write-Host "Đã đổi namespace trong $($awesomeUsingBefore.Count) file." -ForegroundColor Green
    Write-Host "using AwesomeAssertions còn lại: 0" -ForegroundColor Green
    Write-Host "using FluentAssertions hiện có: $($fluentUsingAfter.Count)" -ForegroundColor Green
    Write-Host "Lưu ý: đây chỉ là namespace tương thích của package AwesomeAssertions 8.x, không phải package FluentAssertions." -ForegroundColor Yellow

    Write-Host ""
    Write-Host "=== Xác định NuGet global-packages ===" -ForegroundColor Cyan

    $globalPackagesOutput = @(
        & dotnet nuget locals global-packages --list
    )

    if ($LASTEXITCODE -ne 0) {
        Stop-Fail "Không đọc được NuGet global-packages path."
    }

    $globalPackagesLine = $globalPackagesOutput |
        Where-Object { $_ -match '^\s*global-packages\s*:' } |
        Select-Object -First 1

    if (-not $globalPackagesLine) {
        Stop-Fail "Không tìm thấy dòng global-packages."
    }

    $globalPackagesPath = (
        $globalPackagesLine -replace '^\s*global-packages\s*:\s*', ''
    ).Trim()

    $packageCachePath = Join-Path `
        $globalPackagesPath `
        ("awesomeassertions\" + $PackageVersion)

    Write-Host "Global packages: $globalPackagesPath"
    Write-Host "Cache package: $packageCachePath"

    Write-Host ""
    Write-Host "=== Làm sạch output test cũ ===" -ForegroundColor Cyan

    $testObj = Join-Path $testsRoot "obj"
    $testBin = Join-Path $testsRoot "bin"

    if (Test-Path $testObj) {
        Remove-Item $testObj -Recurse -Force
        Write-Host "Đã xóa: $testObj"
    }

    if (Test-Path $testBin) {
        Remove-Item $testBin -Recurse -Force
        Write-Host "Đã xóa: $testBin"
    }

    Run-DotNet `
        -Name "Restore solution forced/no-cache" `
        -Arguments @(
            "restore",
            ".\GaoApp.sln",
            "--force",
            "--no-cache"
        ) `
        -LogPath (Join-Path $logRoot "restore-force-no-cache.txt")

    Write-Host ""
    Write-Host "=== Kiểm tra assets thực tế của AwesomeAssertions 8.x ===" -ForegroundColor Cyan

    $assetsPath = Join-Path $testsRoot "obj\project.assets.json"

    if (-not (Test-Path $assetsPath -PathType Leaf)) {
        Stop-Fail "Không tạo được project.assets.json: $assetsPath"
    }

    $assetsText = [System.IO.File]::ReadAllText($assetsPath)

    if ($assetsText -notmatch [regex]::Escape("AwesomeAssertions/$PackageVersion")) {
        Stop-Fail "project.assets.json không chứa AwesomeAssertions/$PackageVersion."
    }

    if ($assetsText -notmatch 'FluentAssertions\.dll') {
        Stop-Fail "project.assets.json không chứa compile/runtime asset FluentAssertions.dll của AwesomeAssertions 8.x."
    }

    $packageDlls = @(
        Get-ChildItem `
            $packageCachePath `
            -Recurse `
            -File `
            -Filter "FluentAssertions.dll" `
            -ErrorAction SilentlyContinue
    )

    if ($packageDlls.Count -eq 0) {
        Stop-Fail "NuGet cache không có FluentAssertions.dll trong package AwesomeAssertions $PackageVersion."
    }

    Write-Host "AwesomeAssertions package assets: PASS" -ForegroundColor Green

    foreach ($dll in $packageDlls) {
        Write-Host "DLL: $($dll.FullName)"
    }

    Run-DotNet `
        -Name "Build test project Release" `
        -Arguments @(
            "build",
            ".\GaoApp.Tests\GaoApp.Tests.csproj",
            "-c",
            "Release",
            "--no-restore"
        ) `
        -LogPath (Join-Path $logRoot "build-tests-release.txt")

    Run-DotNet `
        -Name "Build full solution Release" `
        -Arguments @(
            "build",
            ".\GaoApp.sln",
            "-c",
            "Release",
            "--no-restore"
        ) `
        -LogPath (Join-Path $logRoot "build-solution-release.txt")

    Run-DotNet `
        -Name "Run all tests" `
        -Arguments @(
            "test",
            ".\GaoApp.Tests\GaoApp.Tests.csproj",
            "-c",
            "Release",
            "--no-build",
            "--logger",
            "console;verbosity=normal",
            "--logger",
            "trx;LogFileName=w1-fix2-awesome-assertions.trx"
        ) `
        -LogPath (Join-Path $logRoot "test-release.txt")

    $testLogPath = Join-Path $logRoot "test-release.txt"
    $testLog = [System.IO.File]::ReadAllText($testLogPath)

    if ($testLog -match "Xceed License Agreement" -or
        $testLog -match "Fluent Assertions Community License") {
        Stop-Fail "Test vẫn còn cảnh báo giấy phép FluentAssertions/Xceed."
    }

    if ($testLog -notmatch "Test Run Successful") {
        Stop-Fail "Không tìm thấy 'Test Run Successful' trong test log."
    }

    $summary = @(
        Select-String `
            -Path $testLogPath `
            -Pattern "Test Run Successful|Total tests:|Passed:|Failed:|Skipped:"
    )

    Write-Host ""
    Write-Host "PASS W1 FIX2" -ForegroundColor Green
    Write-Host "- Package thực tế: AwesomeAssertions $PackageVersion (Apache 2.0)"
    Write-Host "- Namespace tương thích v8: FluentAssertions"
    Write-Host "- Không còn PackageReference FluentAssertions"
    Write-Host "- Build test project: PASS"
    Write-Host "- Build full solution: PASS"
    Write-Host "- Full test: PASS"
    Write-Host "- Không còn cảnh báo license Xceed/FluentAssertions"
    Write-Host "- Backup: $backupRoot"
    Write-Host "- Log: $logRoot"

    Write-Host ""
    Write-Host "Tóm tắt test:" -ForegroundColor Cyan

    foreach ($line in $summary) {
        Write-Host "  $($line.Line)"
    }
}
finally {
    Pop-Location
}
