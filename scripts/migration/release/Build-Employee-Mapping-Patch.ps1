param([string]$OutputDirectory)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(-not $OutputDirectory){$OutputDirectory=Join-Path $repo '.artifacts/employee-mapping-patch-20260929'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new patch output directory.'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$files=@{
 'Migration.sql'='scripts/migration/initial-import/03-sales/Migration.sql'
 'package.json'='scripts/migration/initial-import/03-sales/package.json'
 'Invoke-Migration.ps1'='scripts/migration/initial-import/Invoke-Migration.ps1'
 'Install-Employee-Mapping-Patch.ps1'='scripts/migration/release/Install-Employee-Mapping-Patch.ps1'
 'Verify-Package.ps1'='scripts/migration/release/Verify-Package.ps1'
}
foreach($name in $files.Keys){Copy-Item -LiteralPath (Join-Path $repo $files[$name]) -Destination (Join-Path $OutputDirectory $name)}
$baseline=Get-Content -LiteralPath (Join-Path $repo '.artifacts/handoff-WIN-20260929-final/MIGRATION/checksums.json') -Raw -Encoding UTF8|ConvertFrom-Json
$supplier=Join-Path $repo '.artifacts/supplier-name-patch-20260929/Migration.sql'
$entry=@($baseline.Files|Where-Object {$_.Path.Replace('\','/') -ceq 'scripts/migration/initial-import/01-products/Migration.sql'})[0]
$entry.Sha256=(Get-FileHash -LiteralPath $supplier).Hash;$entry.Bytes=(Get-Item -LiteralPath $supplier).Length
@{Files=@($baseline.Files)}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputDirectory 'expected-package-files.json') -Encoding UTF8
@'
# Bản vá nhân viên lịch sử — 29/09/2026

Chỉ dành cho C:\GaoMigration-20260929, WIN-HU6RO2EMIJF\SQLEXPRESS,
DataGaoStore -> GaoAppDb, sau khi sản phẩm và khách hàng COMMIT + VERIFY thành công.
Phải đã áp dụng bản vá tên nhà cung cấp 110276. Giữ Web/worker và ghi nguồn dừng.

## Cài bản vá

Giải nén ZIP này vào C:\GaoMigration-20260929\employee-mapping-patch.
Kiểm tra bên trong có Install-Employee-Mapping-Patch.ps1, không lồng thêm thư mục.

```powershell
Set-Location 'C:\GaoMigration-20260929'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\employee-mapping-patch\Install-Employee-Mapping-Patch.ps1
```

Kỳ vọng EMPLOYEE_MAPPING_PATCH_PASS. Installer chỉ sửa 3 file của gói,
cập nhật checksum và trạng thái duyệt, lưu bản trước sửa trong runs/real-01.
Không kết nối SQL. Không reset. Không chạy lại sản phẩm/khách hàng.
Không sửa config.json hoặc các bằng chứng backup/reset/COMMIT đã đạt.

## Chạy lại bước bán hàng theo thứ tự, dừng khi có lỗi

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step 03-sales -Mode PREVIEW
```

Xem EMPLOYEE_MAPPING_PLAN: 1 ApprovedExistingMapping, 4 NewInactiveHistoricalEmployees,
PreserveExistingAccounts=True, GrantStoreAccess=False.
EMPLOYEE_MAPPING_ADJUSTMENTS (5 dòng) được xuất trong report-*.csv của bước.
Nguồn 662309 nguyet -> GaoApp 6. Bốn người hoa/quynhi/dung/nhung123 được cấp
ID GaoApp mới ở sau MAX(Users.Id); giữ tên hiển thị nguồn; tên đăng nhập đúng tên nguồn.
Chỉ năm ngoại lệ này được duyệt; bất kỳ nhân viên không khớp khác sẽ bị chặn.
Đối chiếu SALES_PLAN và chờ PREVIEW_PASS_ROLLED_BACK trước khi chạy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step 03-sales -Mode DRYRUN
```

Kỳ vọng EMPLOYEE_IMPORT_EXACT_PASS, SALES_IMPORT_EXACT_PASS, DRYRUN_PASS_ROLLED_BACK.
Bốn nhân viên chỉ được tạo trong giao dịch thử rồi rollback. Không cấp quyền store/role,
không sao chép mật khẩu cũ; IsActive=False, IsHostAdmin=False, hash đánh dấu không đăng nhập.
Giống các INSERT thử trên SQL Server, bộ đếm identity có thể tăng dù dữ liệu rollback;
ID nhân viên được chọn theo MAX(Id), nên mapping không thay đổi chỉ vì lần chạy thử.

Chỉ khi DRYRUN đã PASS và đối chiếu xong:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step 03-sales -Mode COMMIT -AllowCommit
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step 03-sales -Mode VERIFY
```

Chỉ chạy VERIFY sau COMMIT_PASS. VERIFY bao gồm hash toàn bộ Users/UserInStores;
nếu tài khoản hoặc quyền bị sửa trước khi kết thúc chuỗi chuyển dữ liệu sẽ bị phát hiện.
SQL kiểm tra mọi cột của các tài khoản hiện có và quyền store không bị thay đổi.
Đơn bán, dòng hàng, thanh toán, ca thực/ca tổng hợp, thu chi và trả hàng dùng cùng mapping.
Kho lưu người nhập nguồn trong metadata; kho trả hàng lưu LegacyUserId/EmployeeName gốc.
Không sửa dữ liệu nguồn, không phát hành hóa đơn. Bản vá được kiểm tra offline;
PREVIEW/DRYRUN trên server mới kiểm chứng được dữ liệu SQL thực tế.
'@|Set-Content -LiteralPath (Join-Path $OutputDirectory 'HUONG-DAN.md') -Encoding UTF8
$entries=@(Get-ChildItem -LiteralPath $OutputDirectory -File|Sort-Object Name|ForEach-Object {
 [ordered]@{Path=$_.Name;Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
@{CreatedAtUtc=[datetime]::UtcNow.ToString('o');Files=$entries}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputDirectory 'checksums.json') -Encoding UTF8
& (Join-Path $OutputDirectory 'Verify-Package.ps1') -PackageRoot $OutputDirectory
Write-Output "PATCH_BUILT: $OutputDirectory"
