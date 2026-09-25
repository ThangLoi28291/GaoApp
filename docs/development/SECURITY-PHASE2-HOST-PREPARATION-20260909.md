# Đợt 2 — chuẩn bị source chạy trên host

Ngày 09/09/2026. Tiếp nối `SECURITY-PHASE1-REMEDIATION-20260909.md`.
Thực hiện theo lựa chọn **không backup dữ liệu test**. Không xóa/reset database ứng dụng, không đổi secret/certificate thực tế, không dừng hay khởi động lại app đang chạy. SQL test dùng database tên ngẫu nhiên riêng của fixture và tự dọn.

## Kết quả sửa

| Mục | Hành vi mới | Giới hạn cần biết |
|---|---|---|
| F05 — đăng nhập chung IP | Sliding window mặc định 120 POST/phút/IP và 10 lần/phút/tài khoản; trả 429 kèm Retry-After. Tài khoản tồn tại dùng UserId do SQL xác định, tránh tách quota bằng các biến thể tên mà collation coi là một người. Tên chưa tồn tại dùng khóa băm từ tên chuẩn hóa. CSRF được kiểm tra trước quota tài khoản. | Đếm cả lần thành công và thất bại; quota trong RAM của một process, mất khi restart. Chưa có quota chung nhiều web instance. Thêm một query nhẹ chỉ lấy UserId mỗi POST login hợp lệ. |
| F07 — media ngoài publish | LocalFileStorageService và upload màn hình khách hàng dùng Storage:UploadRoot. URL uploads/products, uploads/display và uploads/data vẫn hoạt động; host phục vụ file từ thư mục dữ liệu riêng. | Không tự chuyển media cũ sang thư mục khác. Khi tồn tại file trùng đường dẫn, file trong UploadRoot được ưu tiên; media cũ còn trong wwwroot được đọc làm phương án tương thích. |
| F07 — giới hạn file công khai | Static middleware chỉ phục vụ JPG/JPEG/PNG/GIF/WEBP/MP4/WEBM trong ba nhóm media trên; chặn invoices, _temp, PDF/XML/PFX/SVG qua static URL. Chặn path traversal và link/junction trong đường dẫn media; thêm nosniff, giữ hỗ trợ range video. | Media sản phẩm/quảng cáo vẫn là tài nguyên công khai như thiết kế hiện tại. File hóa đơn đi qua controller có xác thực/phân quyền. Thư mục storage cần do tài khoản dịch vụ/quản trị kiểm soát. |
| F07 — ghi file bị ngắt | Ghi file tạm cùng thư mục rồi đổi tên; hủy upload không làm hỏng file đích cũ. Move dùng một thao tác thay thế, bỏ bước xóa đích trước. | Chưa có cơ chế quét mọi file upload mồ côi sau mất điện hoặc sau khi lưu file thành công nhưng ghi DB thất bại. |
| F08 — trust proxy | Bật forwarded headers bắt buộc có proxy/network hợp lệ; từ chối cấu hình rỗng, IP unspecified và mạng /0. Config gốc tắt; Development/Production giữ allowlist loopback đã có. | Khi proxy ở máy/container khác phải khai báo IP/network thực tế. Kiểm tra trên hostname/HTTPS thật vẫn thuộc bước triển khai. |
| F09 — ngân hàng mặc định | Ghi cấu hình trong transaction, lấy khóa SQL theo StoreId; flush bỏ mặc định cũ trước khi đặt mới. Thêm unique filtered index bảo đảm tối đa một mặc định chưa xóa mỗi cửa hàng. Tắt tài khoản thì bỏ mặc định; không cho đặt mặc định tài khoản đang tắt. | Timeout chờ khóa sau 5 giây trả lỗi xung đột. Migration dừng khi dữ liệu cũ có nhiều mặc định; không tự chọn tài khoản nhận tiền. |
| F10 — khóa mã hóa trên host | Có tùy chọn PFX để mã hóa Data Protection keys, dùng private key trong bộ nhớ, không cài vào certificate store. Kiểm tra đường dẫn tuyệt đối ngoài publish, private RSA >= 2048 bit và thời hạn certificate. Ngoài Development không cho dùng cấu hình ghi khóa không mã hóa. | Windows chưa cấu hình certificate tiếp tục DPAPI hiện có. Chọn certificate không tự mã hóa lại khóa DPAPI cũ; thử nghiệm đã xác nhận hai service provider độc lập đọc cùng key ring/certificate, chưa phải diễn tập hai máy host vật lý. |
| Quy trình nâng cấp DB | Bộ schema manifest nhận diện chính xác SQL kiểm tra default mới. Bổ sung nhận diện theo SHA-256 cho đoạn backfill ACB `20260908192844_AddAcbQrNotificationReconciliation` đã có trong source nhưng thiếu trong allowlist; cập nhật danh sách migration kỳ vọng trong test. | Giữ cơ chế từ chối raw SQL chưa được rà soát; không nới thành chấp nhận mọi SQL của một migration. |

Migration mới: **20260909055844_EnforceSingleDefaultBankAccount**.
Index: **UX_StoreBankAccounts_OneDefaultPerStore**, điều kiện **IsDefault = 1 AND IsDeleted = 0**.
Migration chỉ kiểm tra xung đột và tạo index; không xóa hay tự sửa bản ghi nghiệp vụ.

## Cách áp dụng

1. App đang chạy vẫn dùng DLL cũ. Dừng/chạy lại theo quy trình local khi sẵn sàng; Development tự chạy migration/seed hiện có. Bản kiểm tra được build ở thư mục riêng để không đụng DLL đang khóa.
2. Với host, dùng GaoApp.Migrator của **cùng bản source/release** trước khi chạy Web Production. Web Production không tự nâng cấp DB. Nếu báo trùng mặc định, chọn lại duy nhất một tài khoản mặc định mỗi cửa hàng rồi chạy lại; không cần xóa toàn database.
3. Tạo thư mục UploadRoot, DataProtectionKeys và Logs ngoài publish, cấp quyền cần thiết cho tài khoản chạy app. Config Production hiện dùng các đường dẫn Windows dưới C:\GaoAppData; phải đổi khi host sử dụng hệ điều hành/đường dẫn khác.
4. Host mới dùng dữ liệu mới có thể cấu hình certificate ngay từ đầu. Không sao chép bộ khóa DPAPI local và giả định máy mới sẽ giải mã được. Nếu không chuyển dữ liệu test thì nhập cấu hình tích hợp mới trên host.
5. Chạy bản đầu tiên với **một web process/instance**. Trước khi tăng nhiều instance cần quota đăng nhập dùng chung, SignalR backplane/dịch vụ tương đương, key ring/certificate và media dùng chung, cùng kiểm tra worker ACB.

Các biến môi trường liên quan (ví dụ đường dẫn; không chứa giá trị secret thật):

| Biến | Giá trị/cách đặt |
|---|---|
| Storage__UploadRoot | Thư mục media bền vững ngoài publish |
| DataProtection__KeysPath | Thư mục key ring ngoài publish |
| DataProtection__RequirePortableKeys | true nếu host bắt buộc dùng khóa chuyển được giữa máy |
| DataProtection__CertificatePath | Đường dẫn tuyệt đối tới PFX, ví dụ C:\GaoAppData\Secrets\protection.pfx |
| DataProtection__CertificatePassword | Secret của PFX, đưa từ môi trường/secret store; không commit vào JSON |
| Security__LoginRateLimit__IpAttemptsPerMinute | Mặc định 120, chấp nhận 50–1000 |
| Security__LoginRateLimit__AccountAttemptsPerMinute | Mặc định 10, chấp nhận 3–20 |
| Proxy__KnownProxies__0 | IP reverse proxy thực tế; loopback chỉ đúng khi proxy cùng máy |

Giữ certificate và private key tương ứng với các key ring cần đọc; đổi certificate cần kế hoạch giữ khả năng giải mã khóa cũ. Đợt này không cấu hình certificate thực tế và không chuyển khóa cũ.

## Kiểm tra

- Build solution Release: 0 lỗi, 10 warning test đã có (nullability/xUnit analyzer). Output: `Logs/security-phase2-release`.
- Nhóm Security, Tenant, Data, Observability, AcbPaymentTests và cấu hình liên quan: 373 passed, 0 failed, 0 skipped.
- Nhóm SQL/migration: 10 passed, 0 failed, 0 skipped. Gồm 50 yêu cầu đổi mặc định với DbContext riêng, rollback giữa hai lần save, SQL từ chối mặc định thứ hai khi bỏ qua service, cách ly cửa hàng, nâng cấp dữ liệu có trùng default, schema preflight, nâng cấp từ baseline và chạy migration lại không đổi schema.
- HTTP thật trên Kestrel test: 50 tên tài khoản cùng IP qua được limiter; quota tài khoản/IP trả 429 đúng ngưỡng; CSRF sai bị 400; public media/range video hoạt động, file riêng tư bị 404. Bài login cô lập phần giới hạn, không chạy nghiệp vụ đăng nhập/POS thật của 50 người.
- Certificate test dùng đăng ký Infrastructure thật, key ring/PFX tổng hợp riêng, hai service provider độc lập; bảo vệ/giải mã thành công và file key có encryptedSecret.

Bằng chứng:

- `Logs/security-phase2-release-build.log`
- `Logs/security-phase2-regression.log`
- `Logs/security-phase2-sql-migrations.log`
- `TestResults/security-phase2/security-phase2-regression.trx`
- `TestResults/security-phase2/security-phase2-sql-migrations.trx`

Không coi đây là toàn bộ test suite/UAT hoặc chứng nhận chịu tải 50 client. Không dùng output test thay bộ publish triển khai.

## Bước còn lại trước khi mở production

Ưu tiên tiếp theo là F06: tối ưu báo cáo lợi nhuận, kiểm tra transaction đọc và giới hạn báo cáo đồng thời. Sau đó tạo staging tương đương host và đo hỗn hợp POS/kho/báo cáo/realtime/ACB ở 10–20–50 client, ghi p95/p99, lỗi, CPU/RAM và SQL lock waits. Còn cần kiểm tra phân quyền các luồng khác, thu hồi kết nối SignalR đã mở, UAT xuyên nghiệp vụ và cấu hình HTTPS/proxy trên host thật.

Tham chiếu cấu hình: [Microsoft — rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0), [Microsoft — Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-8.0). Việc đăng ký certificate dùng API ProtectKeysWithCertificate để đăng ký cả mã hóa và giải mã, theo [source ASP.NET Core 8.0.29](https://github.com/dotnet/aspnetcore/blob/v8.0.29/src/DataProtection/DataProtection/src/DataProtectionBuilderExtensions.cs).
