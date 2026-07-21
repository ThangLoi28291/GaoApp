param(
    [string]$SourceRoot = "D:\Datacode"
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

if (-not (Test-Path (Join-Path $SourceRoot "GaoApp.sln") -PathType Leaf)) {
    Stop-Fail "Không tìm thấy GaoApp.sln trong: $SourceRoot"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-Fail "Máy chưa có .NET SDK hoặc dotnet chưa nằm trong PATH."
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logRoot = Join-Path $SourceRoot "TestResults\W1-AwesomeAssertions-$timestamp"
New-Item -ItemType Directory -Force $logRoot | Out-Null

Push-Location $SourceRoot
try {
    Write-Host "=== Xác nhận trạng thái patch W1 ===" -ForegroundColor Cyan

    # @() luôn trả về mảng, kể cả khi Select-String trả 0 hoặc 1 kết quả.
    $fluentRefs = @(
        Get-ChildItem .\GaoApp.Tests -Recurse -File -Include *.cs,*.csproj |
            Select-String -SimpleMatch "FluentAssertions"
    )

    $awesomeRefs = @(
        Get-ChildItem .\GaoApp.Tests -Recurse -File -Include *.cs,*.csproj |
            Select-String -SimpleMatch "AwesomeAssertions"
    )

    if ($fluentRefs.Count -gt 0) {
        Stop-Fail "Patch W1 chưa hoàn tất: còn $($fluentRefs.Count) tham chiếu FluentAssertions. Không áp patch lần hai; hãy gửi kết quả lệnh kiểm tra cho ChatGPT."
    }

    if ($awesomeRefs.Count -eq 0) {
        Stop-Fail "Không tìm thấy AwesomeAssertions. Source không ở trạng thái sau patch W1."
    }

    $projectFile = ".\GaoApp.Tests\GaoApp.Tests.csproj"
    $projectText = Get-Content $projectFile -Raw

    if ($projectText -notmatch '<PackageReference\s+Include="AwesomeAssertions"\s+Version="8\.0\.2"\s*/>') {
        Stop-Fail "GaoApp.Tests.csproj chưa chứa AwesomeAssertions 8.0.2 đúng như patch W1."
    }

    Write-Host "Tham chiếu FluentAssertions còn lại: 0" -ForegroundColor Green
    Write-Host "Tham chiếu AwesomeAssertions: $($awesomeRefs.Count)" -ForegroundColor Green
    Write-Host "Trạng thái patch: ĐÃ ÁP, tiếp tục restore/build/test." -ForegroundColor Green

    Run-DotNet `
        -Name "Restore solution" `
        -Arguments @("restore", ".\GaoApp.sln") `
        -LogPath (Join-Path $logRoot "restore.txt")

    Run-DotNet `
        -Name "Build Release" `
        -Arguments @("build", ".\GaoApp.sln", "-c", "Release", "--no-restore") `
        -LogPath (Join-Path $logRoot "build-release.txt")

    Run-DotNet `
        -Name "Run all tests" `
        -Arguments @(
            "test",
            ".\GaoApp.Tests\GaoApp.Tests.csproj",
            "-c", "Release",
            "--no-build",
            "--logger", "console;verbosity=normal",
            "--logger", "trx;LogFileName=w1-awesome-assertions.trx"
        ) `
        -LogPath (Join-Path $logRoot "test-release.txt")

    $testLogPath = Join-Path $logRoot "test-release.txt"
    $testLog = Get-Content $testLogPath -Raw

    if ($testLog -match "Xceed License Agreement" -or
        $testLog -match "Fluent Assertions Community License") {
        Stop-Fail "Test vẫn xuất hiện cảnh báo giấy phép Xceed/FluentAssertions."
    }

    if ($testLog -notmatch "Test Run Successful") {
        Stop-Fail "Không tìm thấy dòng 'Test Run Successful' trong test log."
    }

    $summary = @(
        Select-String -Path $testLogPath -Pattern "Test Run Successful|Total tests:|Passed:|Failed:|Skipped:"
    )

    Write-Host ""
    Write-Host "PASS W1" -ForegroundColor Green
    Write-Host "- FluentAssertions đã được thay bằng AwesomeAssertions 8.0.2"
    Write-Host "- Không còn cảnh báo license Xceed/FluentAssertions"
    Write-Host "- Restore/build/test đều trả exit code 0"
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
