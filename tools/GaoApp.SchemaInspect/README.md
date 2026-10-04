# Chẩn đoán cấu trúc database (chỉ đọc)

Công cụ này chỉ đọc migration history và metadata SQL, xuất những thành phần khác với schema của **các migration đã áp dụng**. Các migration mới chưa chạy được liệt kê riêng, không bị coi là thiếu schema. Không gọi Migrate/EnsureCreated, không seed, sửa bảng, hoặc sửa lịch sử migration. Không đọc bản ghi nghiệp vụ và không xuất chuỗi kết nối/mật khẩu.

## Trên server

1. Giải nén gói `SchemaInspect` vào thư mục riêng, ví dụ `C:\GaoAppDeploy\ServerMoi\SchemaInspect`. Không chép đè Web/Migrator đang có.
2. Mở PowerShell và chạy:

```powershell
cd C:\GaoAppDeploy\ServerMoi\SchemaInspect
dotnet GaoApp.SchemaInspect.dll "C:\GaoAppDeploy\ServerMoi\Migrator" ".\schema-report.json"
```

Công cụ dùng cấu hình trong thư mục Migrator được chỉ định: `appsettings.json`, rồi `appsettings.Production.json` (hoặc môi trường `DOTNET_ENVIRONMENT`), cuối cùng là biến môi trường như Migrator. Không cần nhập mật khẩu vào dòng lệnh. Không ghi đè báo cáo có sẵn; lần tiếp theo chọn tên khác.

3. Gửi file `schema-report.json` để xác định hướng sửa. `ExpectedOnly` là định nghĩa được lịch sử yêu cầu nhưng không khớp; `ActualOnly` là định nghĩa đang có nhưng khác với kỳ vọng. Với cột/index bị đổi, có thể có cả hai bản ghi; đây không phải số lượng cột duy nhất.

Exit 0 chỉ xác nhận thu thập báo cáo thành công, **không phải cho phép nâng cấp**. Báo cáo không thay thế kiểm tra đầy đủ của Migrator. Không bỏ preflight hoặc sửa `__EFMigrationsHistory` để ép chạy. Sau khi xác định và xử lý đúng khác biệt, dùng bộ Migrator mới đồng bộ với Web để nâng cấp.

## Build

```powershell
dotnet publish tools/GaoApp.SchemaInspect/GaoApp.SchemaInspect.csproj -c Release -o .artifacts/SchemaInspect
```

Gói không kèm appsettings, chứng thư hoặc dữ liệu server. Cần .NET 8 phù hợp các thành phần GaoApp hiện tại.
