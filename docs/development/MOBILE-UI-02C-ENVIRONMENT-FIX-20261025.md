# UI-02c: sửa môi trường Visual Studio và áp dụng migration

Ngày 25/10/2026. Người dùng yêu cầu sửa lỗi trong ảnh và cập nhật migration. Lượt này có build và thao tác schema theo yêu cầu đó; không mở Web, không chạy bộ test hoặc tự khởi động lại Visual Studio.

## Kết quả

- Đã cài SDK x64 `10.0.402`, kèm runtime .NET/ASP.NET Core/Windows Desktop `10.0.13` bằng bộ cài Microsoft. SHA-512 khớp release metadata, chữ ký Authenticode hợp lệ. Installer trả mã 0; `dotnet --version` trong repo trả `10.0.402`.
- Giữ nguyên `global.json`, `Directory.Build.props` và yêu cầu runtime của dự án. Lỗi [NETSDK1141](https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1141) xuất hiện do máy trước đó chỉ có SDK `10.0.401`.
- Build cuối của `GaoApp.Web`, `GaoApp.Migrator` và `GaoApp.SchemaInspect` đều thành công với **0 lỗi, 0 cảnh báo**.
- Database phát triển `DESKTOP-E059ENS\SQLEXPRESS / GaoAppDb` đã từ 79 lên **81 migration**, mới nhất `20261025090000_AddAdminMenuPinSettings`. Không cập nhật server khác.
- Kiểm tra cuối: `MatchingCurrentSchema`, Pending=0, ExpectedOnly=0, ActualOnly=0; Migrator trả `SCHEMA_ONLY_VERIFIED; SourceMigrations=81; AppliedMigrations=81`.
- `AdminMenuPinSettings` đã có unique index theo cửa hàng và FK hợp lệ đến `Stores`; bảng vẫn **0 dòng**, không tạo cấu hình ghim hoặc menu mặc định.
- Số dòng/checksum của tám bảng `AdminMenuItems`, `Roles`, `RolePermissions`, `Users`, `UserInStores`, `Stores`, `Products`, `ProductVariant` khớp trước/sau migration. Không chạy seeder/bootstrap.

## Sửa mã cần thiết

Migrator bỏ đăng ký `IAdminMenuPinService` khỏi host schema vì chức năng này dùng dịch vụ render menu của Web. Công cụ schema không cần dựng màn hình hoặc chỉnh mục ghim.

SQL Server sinh bộ lọc `[StoreId] IS NOT NULL` cho unique index của migration thủ công. Bước hậu kiểm đầu tiên đã phát hiện metadata chưa khai báo tường minh bộ lọc này. Đã đồng bộ configuration, migration và model snapshot với định nghĩa SQL thực tế. `StoreId` bắt buộc nên bộ lọc vẫn bao phủ mọi dòng; ràng buộc một cấu hình mỗi cửa hàng giữ nguyên. Không sửa lịch sử migration, không rollback hay xóa bảng để ép kiểm tra qua.

Hai migration được áp dụng theo thứ tự:

1. `20261022090000_AddDurableAuditOutbox`: cột EventId nullable ở AuditLogs, unique filtered index và bảng hàng đợi audit.
2. `20261025090000_AddAdminMenuPinSettings`: bảng ID ghim và index theo cửa hàng.

## Sao lưu và bằng chứng

Trước cập nhật đã tạo bản sao lưu SQL `COPY_ONLY, CHECKSUM`, sau đó `RESTORE VERIFYONLY WITH CHECKSUM` thành công. Đây là kiểm tra tính toàn vẹn backup, không phải bài thử restore sang database khác.

Backup: `C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\GaoAppDb_before_UI02c_20261025_033728_e9b9c3763b7c47759ffa66c4c5c061d0.bak`.

Bằng chứng local nằm trong `.artifacts/ui-02c-environment-fix/`: `sdk-install.log`, `web-build.log`, `migrator-build.log`, `schema-inspect-build.log`, `database-before.json`, `database-backup.json`, `schema-before.json`, `schema-after.json`, `schema-final.json`, `VerifyCurrent.log`, `database-verified.json`, `business-before.json`, `business-after.json`. `schema-after.json` giữ lại chẩn đoán index ban đầu; kết luận cuối dùng `schema-final.json` và `VerifyCurrent.log`. Không lưu chuỗi kết nối/mật khẩu vào báo cáo.

## Người dùng kiểm tra tiếp

Visual Studio vẫn đang mở nên chưa xác nhận trực tiếp popup `SelfHostWebServer` trong IDE đã biến mất. Không tự đóng IDE để tránh mất nội dung đang sửa. Cache IDE hiện hành được giữ, các tệp cache đã sao lưu được phục hồi nếu thiếu và không ghi đè tệp đang có.

Đóng hoàn toàn Visual Studio, mở lại `GaoApp.sln`, chọn `GaoApp.Web` rồi F5. Không cần chạy lại migration cho database local này. Mở `/admin/menu-pins` bằng ADMIN và kiểm tra ở chiều rộng điện thoại theo [hướng dẫn UI-02c](MOBILE-UI-02C-PINS-20261025.md). Phần giao diện và các bài test quyền/đồng thời vẫn chờ nghiệm thu; build/schema thành công không thay cho kiểm thử giao diện.

Migrator còn ghi hai cảnh báo model có từ trước về quan hệ `AcbCallbackRouteChange` và sentinel của `Promotion.ComboPricingMode`; đây không phải lỗi SDK hoặc sai lệch schema UI-02c và không được sửa trong lượt này.
