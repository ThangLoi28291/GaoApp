param(
    [string]$SourceRoot = "D:\Datacode",
    [string]$PatchPath = (Join-Path $PSScriptRoot "W1-Replace-FluentAssertions-With-AwesomeAssertions.patch")
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

if (-not (Test-Path $PatchPath -PathType Leaf)) {
    Stop-Fail "Không tìm thấy patch: $PatchPath"
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Stop-Fail "Máy chưa có Git hoặc Git chưa nằm trong PATH."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-Fail "Máy chưa có .NET SDK hoặc dotnet chưa nằm trong PATH."
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = "D:\Backup\GaoApp-W1-AwesomeAssertions-$timestamp"
$logRoot = Join-Path $SourceRoot "TestResults\W1-AwesomeAssertions-$timestamp"

New-Item -ItemType Directory -Force $backupRoot | Out-Null
New-Item -ItemType Directory -Force $logRoot | Out-Null

Write-Host "=== Backup GaoApp.Tests ===" -ForegroundColor Cyan
& robocopy `
    (Join-Path $SourceRoot "GaoApp.Tests") `
    (Join-Path $backupRoot "GaoApp.Tests") `
    /E /COPY:DAT /R:2 /W:1 `
    /XD bin obj TestResults `
    /XF *.trx *.coverage *.coveragexml | Out-Host

$robocopyExit = $LASTEXITCODE
if ($robocopyExit -gt 7) {
    Stop-Fail "Backup thất bại. Robocopy exit code=$robocopyExit"
}

Write-Host "Backup: $backupRoot" -ForegroundColor Green

Push-Location $SourceRoot
try {
    $fluentBefore = Get-ChildItem .\GaoApp.Tests -Recurse -File -Include *.cs,*.csproj |
        Select-String -SimpleMatch "FluentAssertions"

    $awesomeBefore = Get-ChildItem .\GaoApp.Tests -Recurse -File -Include *.cs,*.csproj |
        Select-String -SimpleMatch "AwesomeAssertions"

    if ($fluentBefore.Count -eq 0 -and $awesomeBefore.Count -gt 0) {
        Write-Host "Patch có vẻ đã được áp trước đó; bỏ qua bước git apply và tiếp tục build/test." -ForegroundColor Yellow
    }
    else {
        Write-Host "" 
        Write-Host "=== Kiểm tra patch ===" -ForegroundColor Cyan
        & git apply --check $PatchPath
        if ($LASTEXITCODE -ne 0) {
            Stop-Fail "git apply --check thất bại. Source có thể đã khác baseline hoặc patch đã áp một phần."
        }

        Write-Host "=== Áp patch ===" -ForegroundColor Cyan
        & git apply $PatchPath
        if ($LASTEXITCODE -ne 0) {
            Stop-Fail "git apply thất bại."
        }
    }

    $fluentAfter = Get-ChildItem .\GaoApp.Tests -Recurse -File -Include *.cs,*.csproj |
        Select-String -SimpleMatch "FluentAssertions"

    if ($fluentAfter.Count -ne 0) {
        Stop-Fail "Vẫn còn $($fluentAfter.Count) tham chiếu FluentAssertions sau khi áp patch."
    }

    $awesomeAfter = Get-ChildItem .\GaoApp.Tests -Recurse -File -Include *.cs,*.csproj |
        Select-String -SimpleMatch "AwesomeAssertions"

    Write-Host "Tham chiếu FluentAssertions còn lại: 0" -ForegroundColor Green
    Write-Host "Tham chiếu AwesomeAssertions: $($awesomeAfter.Count)" -ForegroundColor Green

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

    $testLog = Get-Content (Join-Path $logRoot "test-release.txt") -Raw

    if ($testLog -match "Xceed License Agreement" -or $testLog -match "Fluent Assertions Community License") {
        Stop-Fail "Test vẫn xuất hiện cảnh báo giấy phép Xceed/FluentAssertions."
    }

    if ($testLog -notmatch "Test Run Successful") {
        Stop-Fail "Không tìm thấy dòng 'Test Run Successful' trong test log."
    }

    Write-Host "" 
    Write-Host "PASS W1" -ForegroundColor Green
    Write-Host "- FluentAssertions đã được thay bằng AwesomeAssertions 8.0.2"
    Write-Host "- Không còn cảnh báo license Xceed trong test log"
    Write-Host "- Restore/build/test đều trả exit code 0"
    Write-Host "- Log: $logRoot"
    Write-Host "- Backup: $backupRoot"
    Write-Host ""
    Write-Host "Rollback khi chưa sửa thêm file test:" -ForegroundColor Yellow
    Write-Host "  cd $SourceRoot"
    Write-Host "  git apply -R `"$PatchPath`""
}
finally {
    Pop-Location
}
