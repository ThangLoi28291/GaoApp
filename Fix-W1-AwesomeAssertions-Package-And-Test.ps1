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
$projectPath = Join-Path $SourceRoot "GaoApp.Tests\GaoApp.Tests.csproj"
$testsRoot = Join-Path $SourceRoot "GaoApp.Tests"

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
$backupRoot = "D:\Backup\GaoApp-W1-Fix1-$timestamp"
$logRoot = Join-Path $SourceRoot "TestResults\W1-Fix1-$timestamp"

New-Item -ItemType Directory -Force $backupRoot | Out-Null
New-Item -ItemType Directory -Force $logRoot | Out-Null

Copy-Item $projectPath (Join-Path $backupRoot "GaoApp.Tests.csproj") -Force

Push-Location $SourceRoot
try {
    Write-Host "=== Kiểm tra trạng thái source W1 ===" -ForegroundColor Cyan

    $fluentRefs = @(
        Get-ChildItem $testsRoot -Recurse -File -Include *.cs,*.csproj |
            Select-String -SimpleMatch "FluentAssertions"
    )

    $awesomeRefs = @(
        Get-ChildItem $testsRoot -Recurse -File -Include *.cs,*.csproj |
            Select-String -SimpleMatch "AwesomeAssertions"
    )

    if ($fluentRefs.Count -gt 0) {
        Stop-Fail "Source còn $($fluentRefs.Count) tham chiếu FluentAssertions. Không chạy Fix1 trên trạng thái trộn."
    }

    if ($awesomeRefs.Count -eq 0) {
        Stop-Fail "Không tìm thấy AwesomeAssertions trong source test."
    }

    $projectText = Get-Content $projectPath -Raw
    $expectedPattern = '<PackageReference\s+Include="AwesomeAssertions"\s+Version="' +
        [regex]::Escape($PackageVersion) + '"\s*/>'

    if ($projectText -notmatch $expectedPattern) {
        Write-Host "PackageReference chưa đúng. Dùng dotnet CLI để cập nhật." -ForegroundColor Yellow
        & dotnet add $projectPath package AwesomeAssertions --version $PackageVersion --no-restore
        if ($LASTEXITCODE -ne 0) {
            Stop-Fail "Không cập nhật được PackageReference AwesomeAssertions $PackageVersion."
        }
    }

    Write-Host "FluentAssertions còn lại: 0" -ForegroundColor Green
    Write-Host "AwesomeAssertions trong source: $($awesomeRefs.Count)" -ForegroundColor Green

    Write-Host ""
    Write-Host "=== Xác định NuGet global-packages ===" -ForegroundColor Cyan
    $globalPackagesOutput = @(& dotnet nuget locals global-packages --list)
    if ($LASTEXITCODE -ne 0) {
        Stop-Fail "Không đọc được NuGet global-packages path."
    }

    $globalPackagesLine = $globalPackagesOutput |
        Where-Object { $_ -match '^\s*global-packages\s*:' } |
        Select-Object -First 1

    if (-not $globalPackagesLine) {
        Stop-Fail "Không tìm thấy dòng global-packages trong kết quả dotnet nuget locals."
    }

    $globalPackagesPath = ($globalPackagesLine -replace '^\s*global-packages\s*:\s*', '').Trim()
    if ([string]::IsNullOrWhiteSpace($globalPackagesPath)) {
        Stop-Fail "NuGet global-packages path rỗng."
    }

    $packageCachePath = Join-Path $globalPackagesPath ("awesomeassertions\" + $PackageVersion)

    Write-Host "Global packages: $globalPackagesPath"
    Write-Host "Cache mục tiêu: $packageCachePath"

    Write-Host ""
    Write-Host "=== Xóa cache/obj cũ của riêng W1 ===" -ForegroundColor Cyan

    if (Test-Path $packageCachePath) {
        Remove-Item $packageCachePath -Recurse -Force
        Write-Host "Đã xóa package cache cũ: $packageCachePath"
    }
    else {
        Write-Host "Không có package cache cũ cần xóa."
    }

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
        -Arguments @("restore", ".\GaoApp.sln", "--force", "--no-cache") `
        -LogPath (Join-Path $logRoot "restore-force-no-cache.txt")

    Write-Host ""
    Write-Host "=== Kiểm tra compile asset AwesomeAssertions ===" -ForegroundColor Cyan

    $assetsPath = Join-Path $testsRoot "obj\project.assets.json"
    if (-not (Test-Path $assetsPath -PathType Leaf)) {
        Stop-Fail "Không tạo được project.assets.json: $assetsPath"
    }

    $assetsText = Get-Content $assetsPath -Raw

    if ($assetsText -notmatch [regex]::Escape("AwesomeAssertions/$PackageVersion")) {
        Stop-Fail "project.assets.json không chứa AwesomeAssertions/$PackageVersion."
    }

    if ($assetsText -notmatch 'AwesomeAssertions\.dll') {
        Stop-Fail "project.assets.json có package nhưng không có compile/runtime asset AwesomeAssertions.dll."
    }

    $packageDlls = @(
        Get-ChildItem $packageCachePath -Recurse -File -Filter "AwesomeAssertions.dll" -ErrorAction SilentlyContinue
    )

    if ($packageDlls.Count -eq 0) {
        Stop-Fail "NuGet cache không có AwesomeAssertions.dll sau restore."
    }

    Write-Host "Assets package: PASS" -ForegroundColor Green
    foreach ($dll in $packageDlls) {
        Write-Host "DLL: $($dll.FullName)"
    }

    Run-DotNet `
        -Name "Build test project Release" `
        -Arguments @("build", ".\GaoApp.Tests\GaoApp.Tests.csproj", "-c", "Release", "--no-restore") `
        -LogPath (Join-Path $logRoot "build-tests-release.txt")

    Run-DotNet `
        -Name "Build full solution Release" `
        -Arguments @("build", ".\GaoApp.sln", "-c", "Release", "--no-restore") `
        -LogPath (Join-Path $logRoot "build-solution-release.txt")

    Run-DotNet `
        -Name "Run all tests" `
        -Arguments @(
            "test",
            ".\GaoApp.Tests\GaoApp.Tests.csproj",
            "-c", "Release",
            "--no-build",
            "--logger", "console;verbosity=normal",
            "--logger", "trx;LogFileName=w1-fix1-awesome-assertions.trx"
        ) `
        -LogPath (Join-Path $logRoot "test-release.txt")

    $testLogPath = Join-Path $logRoot "test-release.txt"
    $testLog = Get-Content $testLogPath -Raw

    if ($testLog -match "Xceed License Agreement" -or
        $testLog -match "Fluent Assertions Community License") {
        Stop-Fail "Test vẫn còn cảnh báo giấy phép FluentAssertions/Xceed."
    }

    if ($testLog -notmatch "Test Run Successful") {
        Stop-Fail "Không tìm thấy 'Test Run Successful' trong test log."
    }

    $summary = @(
        Select-String -Path $testLogPath `
            -Pattern "Test Run Successful|Total tests:|Passed:|Failed:|Skipped:"
    )

    Write-Host ""
    Write-Host "PASS W1 FIX1" -ForegroundColor Green
    Write-Host "- Cache NuGet AwesomeAssertions đã được làm mới"
    Write-Host "- project.assets.json có AwesomeAssertions.dll"
    Write-Host "- Build test project: PASS"
    Write-Host "- Build full solution: PASS"
    Write-Host "- Full test: PASS"
    Write-Host "- Không còn cảnh báo license FluentAssertions/Xceed"
    Write-Host "- Backup csproj: $backupRoot"
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
