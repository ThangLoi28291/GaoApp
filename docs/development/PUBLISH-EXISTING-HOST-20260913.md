> **Superseded deployment procedure (2026-09-13):** Use [the current TH1/TH2 runbook](GAOAPP-DEPLOYMENT-RUNBOOK-20260913.md). The historical commands/pipeline below are context only: Migrator without a mode now fails closed; normal migration uses `--schema-only`, with all seed/bootstrap flags false. Seed/bootstrap require a separate explicitly approved data operation. Do not run historical migration+seed instructions as normal deployment.

# Cập nhật host đang vận hành, giữ dữ liệu hiện có

Kiểm tra theo source GaoApp ngày 13/09/2026. Hướng dẫn giả định host Windows/IIS + SQL Server. Các đường dẫn host bên dưới là ví dụ cần thay; không phải thông tin đã xác minh trên host. Tài liệu này không thực hiện publish, migration hoặc thay đổi host.

**Điểm cần biết trước khi cập nhật**

- `GaoApp.Web/Program.cs` chỉ gọi tự migrate/seed trong môi trường `Development`. Khởi động Web bình thường ở `Production` không gọi bước này.
- `GaoApp.Migrator` hiện thực hiện migration rồi luôn gọi `MandatorySecuritySeeder`, kể cả khi `EnableDemoSeed=false` và `ProductionBootstrap.Enabled=false`. Seeder này gọi `AdminMenuSeeder`, bổ sung permission và quyền mặc định.
- `AdminMenuSeeder` đối chiếu các menu hệ thống (`IsSystem=true`) với định nghĩa trong source: có thể đặt lại tiêu đề, route, icon, nhóm cha, thứ tự, quyền và trạng thái; đánh dấu xóa mềm mục trùng. Nó không chỉ thêm menu còn thiếu.
- Do đó menu có thể thay đổi sau khi chạy Migrator, ngay cả khi database không có migration mới. Tắt seed demo không tắt seed menu. Hiện chưa có cờ `--schema-only` hay `--skip-seed` trong Migrator.
- Publish không sao chép dữ liệu từ database local sang host. Các thay đổi menu nói trên do code seed chạy trên database được cấu hình. Chưa có log host để khẳng định nguyên nhân của lần menu đã đổi.

**Chuẩn bị chung**

1. Ghi lại đường dẫn IIS đang chạy, tên application pool, database host, đường dẫn uploads, Data Protection keys/PFX và nơi lưu cấu hình host. Giữ bản release cũ để quay lại.
2. Web trên host phải nhận `DOTNET_ENVIRONMENT=Production` và `ASPNETCORE_ENVIRONMENT=Production`. Không đặt hai biến mâu thuẫn. `Release` là cấu hình build, không thay thế môi trường runtime `Production`.
3. Giữ `SeedData__EnableDemoSeed=false`, `SeedData__EnableDefaultAdminSeed=false`, `ProductionBootstrap__Enabled=false` cho website đang vận hành. Các giá trị này không tắt seeder menu của Migrator.
4. Không khởi chạy Web với `--recover-admin-menus-only` trong quy trình cập nhật thường xuyên; đây là thao tác chủ động khôi phục menu theo source.
5. Sao lưu database host bằng SSMS: chọn đúng database → Tasks → Back Up → Full, chọn file mới có ngày/giờ; có thể dùng Copy-only cho bản sao lưu thủ công. Xác nhận backup thành công và có phương án khôi phục đã kiểm tra. File backup nằm trên máy SQL Server, ở nơi dịch vụ SQL có quyền ghi.
6. Chọn giờ bảo trì. Với POS offline, xác nhận các quầy đã đồng bộ hết giao dịch chờ trước khi đóng tab/bảo trì. Không xóa dữ liệu trình duyệt để ép cập nhật khi còn đơn chờ đồng bộ.

**Trường hợp 1 — Chỉ thay đổi code, database host đã đúng phiên bản**

Điều kiện: không có migration mới cần áp dụng, và code mới không yêu cầu thay đổi dữ liệu cấu hình/quyền. Nếu tính năng mới cần permission/menu mới, bổ sung đúng các mục đó bằng bước cập nhật dữ liệu riêng đã kiểm tra, không chạy seed toàn bộ để tiện tay.

Trên máy phát triển:

1. Hoàn tất các kiểm tra liên quan. Không sửa source trong lúc đóng gói.
2. Visual Studio → chuột phải `GaoApp.Web` → Publish → Folder → chọn thư mục mới cho lần phát hành, ví dụ `D:\Publish\GaoApp\20260913-01\Web`.
3. Chọn Configuration `Release`, Target framework `net8.0`, Deployment mode `Framework-dependent` nếu host đang dùng .NET 8 Hosting Bundle tương ứng. Publish vào thư mục đóng gói riêng; không trỏ profile vào website đang chạy hoặc thư mục chứa dữ liệu host.
4. Publish thành công mới chuyển gói lên host. Dùng toàn bộ kết quả publish, gồm DLL, dependencies, runtime config, `wwwroot`, `web.config`; không chỉ chép riêng `GaoApp.Web.dll` hoặc lấy thư mục `bin`.

Dự án cũng có script đóng gói Web và Migrator vào một release mới, kiểm tra gói và tạo manifest. Tên script có chữ staging nhưng cấu hình build/runtime của gói là Release/Production; cấu hình host thật được cấp riêng:

```powershell
Set-Location 'D:\GaoApp\GAOAPP-SA-20260819-01'
.\scripts\publish-staging-release.ps1
```

Kết quả nằm trong `publish\releases\<release-id>\web`, `migrator` và `release-manifest.json`. Script không triển khai lên host, không chạy Migrator và không chạy Run All. Trường hợp 1 chỉ chạy thành phần Web; việc gói có thư mục Migrator không phải yêu cầu chạy nó.

Trên host:

1. Upload giải nén bản mới vào thư mục release mới, ví dụ `D:\GaoAppDeploy\Releases\20260913-01\Web`. Giữ website cũ chạy trong lúc chép gói vào thư mục này.
2. Áp lại cấu hình host hiện hành cho bản mới trước khi mở ứng dụng. `PublishSafety.targets` loại cấu hình local và thay bằng template `appsettings.json` sạch: kết nối SQL trống, URL placeholder, đường dẫn storage/keys trống và một số tính năng mặc định tắt. Không dùng template này thay trực tiếp cấu hình host đang chạy.
3. Ưu tiên giữ cấu hình riêng của host trong biến môi trường của IIS/service. Nếu hiện host dùng file cấu hình, sao lưu rồi chuyển các giá trị host cần thiết sang bản mới; đối chiếu khóa mới trong source. Không copy cấu hình database/mật khẩu từ máy phát triển lên.
4. Nếu biến môi trường đang nằm trong `web.config` cũ, chuyển các giá trị đó vào `web.config` của bản mới; giữ các thay đổi handler/runtime/request limits của bản mới. Không chép đè mù quáng toàn bộ `web.config` từ bản cũ hoặc bản mới.
5. Kiểm tra kết nối vẫn trỏ đúng database host; giữ nguyên uploads, keys, certificate và mật khẩu certificate của môi trường này. Không thay keys bằng keys local. Kiểm tra cả domain, tenant và các feature đang bật trên host. Thư mục dữ liệu nên nằm ngoài release; nếu hiện đang nằm trong release cũ, phải xử lý đường dẫn/dữ liệu trước khi đổi thư mục chạy.
6. Đến giờ bảo trì, dừng đúng application pool của GaoApp (kiểm tra pool có dùng chung website khác không). IIS → website → Basic Settings → Physical path: chuyển tới thư mục Web mới. Khởi động lại pool. Không chạy hai phiên bản Web song song cùng database để thử.
7. Kiểm tra login, menu, ảnh sản phẩm, trang vừa sửa và thao tác theo tài khoản nhân viên. Kiểm tra log lỗi; sau khi đơn offline đã đồng bộ, tải lại các tab POS để nhận JS/CSS mới.
8. Trường hợp chỉ đổi code này không chạy `GaoApp.Migrator`, không restore database local và không chạy script tạo lại database. Nếu phát hiện schema thiếu thì xử lý theo trường hợp 2.

Nếu host chỉ cho upload đè thư mục cũ: tạo `app_offline.htm` tại thư mục gốc ứng dụng, ngang `GaoApp.Web.dll`, nội dung báo bảo trì; đợi ứng dụng dừng, chép đầy đủ file ứng dụng và đối chiếu file cũ cần loại bỏ, bảo toàn cấu hình/dữ liệu host, xóa `app_offline.htm` sau khi hoàn tất. Nếu file vẫn bị khóa, dùng chức năng stop/restart app của host. Không bật tùy chọn xóa toàn bộ file đích khi đích còn chứa uploads/keys/cấu hình. Cơ chế này được mô tả trong [Microsoft: App Offline](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/app-offline?view=aspnetcore-10.0).

Profile `GaoApp.Web/Properties/PublishProfiles/FolderProfile.pubxml` hiện có `DeleteExistingFiles=true`: chỉ dùng cho thư mục đóng gói riêng. Profile khác không thay thế việc xác minh môi trường thật của IIS.

**Trường hợp 2 — Có thay đổi database, giữ dữ liệu đang vận hành**

Luồng dùng với source hiện tại: tạo script SQL migration trên máy phát triển → kiểm tra và thử trên bản sao database host → sao lưu/bảo trì host → chạy script trên đúng database host → đưa Web cùng phiên bản lên → kiểm tra. Không chạy chương trình `GaoApp.Migrator` sau script này nếu muốn tránh seeder menu hiện tại.

Migration thêm bảng/cột thông thường có thể bảo toàn các dòng hiện có. Không thể cam kết mọi migration đều giữ nguyên dữ liệu: xóa cột, thu hẹp kiểu dữ liệu, đổi tên bị sinh thành xóa+tạo, SQL tùy chỉnh hoặc cập nhật dữ liệu có thể làm thay đổi/mất dữ liệu. Thêm cột NOT NULL có thể cần điền giá trị cho các dòng cũ; phải chọn giá trị có ý nghĩa. Không restore bản database local lên database host đang bán hàng.

**Bước 1: Xác định phiên bản host và lưu menu hiện tại**

Trong SSMS kết nối host, chọn đúng database và chạy các truy vấn chỉ đọc:

```sql
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;

SELECT MigrationId, ProductVersion
FROM dbo.__EFMigrationsHistory
ORDER BY MigrationId;

SELECT *
FROM dbo.AdminMenuItems
ORDER BY StoreId, Id;
```

Lưu danh sách migration đầy đủ và kết quả menu trước cập nhật. So menu theo nội dung/ID/thứ tự/trạng thái, không chỉ tổng số dòng. Nếu bảng lịch sử migration không tồn tại, hoặc danh sách không khớp nhánh source hiện tại, dừng để đối chiếu baseline/schema; không xóa hay tự chèn lịch sử để bỏ lỗi. Không chạy initial schema lên một database đã có bảng nghiệp vụ.

**Bước 2: Xác định migration sẽ triển khai**

Trên máy phát triển, Visual Studio → Tools → NuGet Package Manager → Package Manager Console:

```powershell
Get-Migration -NoConnect -Project GaoApp.Infrastructure -StartupProject GaoApp.Web -Context AppDbContext
```

Đối chiếu toàn bộ migration host với source. Nếu đã có file migration đi cùng thay đổi thì dùng các file đó; không tạo migration mới chỉ vì đang publish. Chỉ khi thực sự sửa model/schema mà chưa có migration, người phát triển tạo và kiểm tra `Up()`/`Down()` trước khi đóng gói.

**Bước 3: Sinh script cập nhật trong phạm vi cần thiết**

Tạo trước thư mục chứa file. Trong Package Manager Console, thay hai tên minh họa bằng ID đã xác minh:

```powershell
Script-Migration -From 'MIGRATION_CUOI_DA_AP_DUNG_TREN_HOST' -To 'MIGRATION_CUOI_CUA_BAN_PHAT_HANH' -Idempotent -Project GaoApp.Infrastructure -StartupProject GaoApp.Web -Context AppDbContext -Output 'D:\Publish\GaoApp\update-host.sql'
```

`From` là migration host đã có; `To` là đích của bản phát hành. Đích phải mới hơn điểm đầu. Đây là lệnh tạo file SQL, chưa áp dụng thay đổi lên host. Factory design-time hiện có tạo DbContext riêng; quá trình này không gọi pipeline seed của GaoApp.Migrator. `-Idempotent` kiểm tra lịch sử và bỏ qua migration đã áp dụng trong phạm vi script; nó không tự sửa bảng lịch sử sai hay schema bị sửa tay. Tham khảo [tham số EF Core PMC](https://learn.microsoft.com/en-us/ef/core/cli/powershell) và [áp dụng migration bằng SQL](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

**Bước 4: Kiểm tra script và thử trên bản sao**

- Đọc toàn bộ thay đổi SQL; với yêu cầu chỉ thêm cột/bảng, nội dung phải phù hợp yêu cầu đó. Kiểm tra mọi DROP, TRUNCATE, DELETE, UPDATE, đổi kiểu và SQL động; xem cả trigger nếu có tác động dữ liệu.
- Nếu muốn menu giữ nguyên, script không được sửa dữ liệu `AdminMenuItems`. Phân biệt SQL ghi `__EFMigrationsHistory` là theo dõi migration cần thiết, không phải seed menu.
- SQL script không chạy các hàm seeder C# nhưng vẫn chứa mọi thao tác dữ liệu được viết trong migration, gồm `InsertData`, `UpdateData`, `DeleteData` hoặc `migrationBuilder.Sql`. `-Idempotent` không có nghĩa script không xóa dữ liệu.
- Restore backup host sang database kiểm tra tên khác, cấu hình môi trường kiểm tra riêng để không gọi tích hợp/giao dịch thật, chạy script và thử bản Web mới trên database đó. So menu trước/sau, dữ liệu đơn/tồn và chức năng mới.
- Nếu tính năng mới có permission/menu/dữ liệu cấu hình bắt buộc, chuẩn bị script bổ sung có phạm vi rõ ràng, chỉ thêm mục đã chốt và không đặt lại menu/quyền đang chỉnh. Không bỏ qua dữ liệu bắt buộc rồi kết luận chỉ chạy DDL là đủ.

**Bước 5: Áp dụng lên host trong giờ bảo trì**

1. Upload sẵn Web mới nhưng chưa cho chạy. Xác nhận các quầy đã đồng bộ, dừng Web và các tiến trình khác ghi vào database này.
2. Backup database host lần cuối sau khi dừng ghi; lưu bản menu và `__EFMigrationsHistory` trước cập nhật.
3. Trong SSMS chọn đúng database host; kiểm tra lại `DB_NAME()`/server rồi mở file SQL đã kiểm tra, thực thi toàn bộ. Dùng tài khoản triển khai đủ quyền thay schema; không tăng quyền tài khoản Web để sửa lỗi migration.
4. Nếu có lỗi SQL: dừng triển khai, xem toàn bộ Messages và giao dịch còn mở, kiểm tra migration nào đã hoàn tất. Không giả định mọi migration đã tự rollback, không chạy tiếp/mở Web để thử và không tự xóa dòng lịch sử. Xử lý lỗi hoặc khôi phục theo kế hoạch đã kiểm tra.
5. Khi thành công, truy vấn lại danh sách migration và menu. Xác nhận đã tới đúng `To`, đủ các migration cần có, menu không thay đổi ngoài yêu cầu; kiểm tra bảng/cột mới.
6. Chuyển IIS sang Web mới cùng phiên bản migration, giữ cấu hình host như trường hợp 1. Mở pool, kiểm tra menu, login, quyền nhân viên và chức năng vừa nâng cấp.

Ví dụ trong source hiện tại: `AddCustomerDisplayWifi.Up()` thêm hai cột nullable `GuestWifiName`/`GuestWifiPassword` vào `Stores`; `AddReceivingPackagingPhoto.Up()` thêm cột ảnh nullable vào `StockDocumentProvisionalItems`. Hai `Up()` này không đặt lại menu. Đây là ví dụ về nội dung migration, không phải kết luận host đang thiếu các migration đó.

**Quay lại khi có lỗi**

- Chỉ đổi code, chưa thay database: có thể chuyển IIS về release cũ và cấu hình host đã lưu, sau khi kiểm tra dữ liệu nghiệp vụ mới có tương thích hay không.
- Đã đổi database: chỉ chuyển code cũ về khi nó tương thích schema mới. Không tự chạy `Down`, `Update-Database 0`, xóa migration history hoặc tạo database mới để chữa lỗi.
- Restore backup production là thao tác có thể mất giao dịch phát sinh sau thời điểm backup. Giữ Web dừng và đối soát trước khi quyết định khôi phục. Không khôi phục toàn database chỉ để sửa menu nếu còn cách sửa có phạm vi hẹp và đã kiểm tra.

**Khuyến nghị cho lần nâng cấp công cụ triển khai**

Nên tách chế độ cập nhật schema khỏi khởi tạo dữ liệu, bổ sung quyền và phục hồi menu. Mặc định cập nhật host đang chạy không gọi `AdminMenuSeeder`; thao tác phục hồi menu phải chủ động riêng. Đây là đề xuất sửa tiếp công cụ, chưa phải tính năng đã có trong source tại thời điểm viết tài liệu.
