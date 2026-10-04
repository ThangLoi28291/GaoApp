# Xử lý đúng lỗi cấu trúc trong schema-report ngày 28/09/2026

## Kết luận

- Database đã áp dụng 43 migration và còn hai migration ngày 28/09 chưa chạy.
- Thiếu đúng ba index không unique: `IX_AutoInvoiceOperations_InvoiceHeadId`, `IX_AutoInvoiceOperationSources_AutoInvoiceOperationId`, `IX_AutoInvoiceOperationSources_InvoiceHeadId`.
- `dbo.GaoStoreMigrationRunsV2` và `dbo.GaoStoreProductImageRunsV1` là biên nhận do các runner `scripts/migration/initial-import/Invoke-Migration.ps1` và `scripts/migration/product-images/Invoke-Images.ps1` tạo. Giữ nguyên chúng và dữ liệu trong đó.
- Preflight mới chấp nhận hai bảng tùy chọn này chỉ khi cấu trúc khớp chính xác định nghĩa đã kiểm tra. Bảng lạ hoặc thay đổi cột, PK, FK, index, default/check vẫn bị chặn. Không bỏ kiểm tra schema hay lịch sử migration.

## Trên server

1. Sao lưu database theo quy trình đang dùng. Dừng Web và các dịch vụ GaoApp đang dùng database trong lúc sửa/nâng cấp.
2. Giải nén `SchemaRecovery-20260928.zip` vào `C:\GaoAppDeploy\ServerMoi`. Gói có ba thư mục `Migrator`, `SchemaRepair`, `SchemaInspect`. Cho phép cập nhật các DLL trùng tên. Gói **không chứa appsettings**, không thay đổi cấu hình kết nối đang có trong thư mục Migrator. Giữ nguyên bản cấu hình đã chạy kết nối thành công.
3. Kiểm tra chỉ đọc:

```powershell
cd C:\GaoAppDeploy\ServerMoi
dotnet .\SchemaRepair\GaoApp.SchemaRepair.dll --check .\Migrator
```

Phải có `REPAIR_PLAN_VERIFIED` và ba tên index bên trên. Nếu đã sửa trước đó, có thể hiện `ALREADY_COMPATIBLE`. Nếu lỗi hoặc hiện khác, dừng và gửi kết quả, không chuyển sang bước 4.

4. Bổ sung index (thực hiện một lần):

```powershell
dotnet .\SchemaRepair\GaoApp.SchemaRepair.dll --apply .\Migrator
```

Chỉ tiếp tục khi có `AUTO_INVOICE_INDEX_REPAIR_VERIFIED` hoặc `ALREADY_COMPATIBLE`. Công cụ xác minh lại schema, tạo các index còn thiếu trong một transaction rồi kiểm tra preflight trước commit; lỗi thì rollback. Không sửa bảng dữ liệu, bản ghi nghiệp vụ, mật khẩu hoặc `__EFMigrationsHistory`. Nếu có khác biệt ngoài ba index, công cụ từ chối.

5. Chạy **Migrator mới trong gói**, vẫn dùng cấu hình server:

```powershell
cd C:\GaoAppDeploy\ServerMoi\Migrator
dotnet GaoApp.Migrator.dll --schema-only
```

Chờ `SCHEMA_ONLY_VERIFIED; SourceMigrations=45; AppliedMigrations=45` và `GaoApp.Migrator completed successfully`. Sau đó khởi động Web bản mới. Gói khắc phục không kèm Web/Worker; các thành phần này cần là bản đã publish theo thay đổi nghiệp vụ trước đó.

Nếu cần xuất báo cáo lại, dùng SchemaInspect mới trong gói:

```powershell
cd C:\GaoAppDeploy\ServerMoi
dotnet .\SchemaInspect\GaoApp.SchemaInspect.dll .\Migrator .\schema-report-after.json
```

## Kiểm chứng cục bộ

Tái tạo schema 43 migration, cả hai bảng nhật ký có dữ liệu, xóa đúng ba index để mô phỏng báo cáo. Kiểm tra chỉ đọc không tạo index; apply tạo đúng ba index; chạy lại không thay đổi; lịch sử và dữ liệu nhật ký giữ nguyên; nâng cấp đủ 45 migration thành công. Kiểm thử thêm: bảng lạ, nhật ký thêm cột hoặc đổi nullability đều bị chặn; lỗi khi tạo index thứ hai rollback cả index thứ nhất.

Chưa chạy công cụ trên server thật. Log cục bộ: `Logs/import-journal-repair-tests.log`, `Logs/schema-repair-preflight-regression.log`.
