# Đợt 1: sửa bảo mật trước khi triển khai host

Ngày 09/09/2026. Thực hiện theo yêu cầu không backup dữ liệu test và bắt đầu bước sửa tiếp theo.
Đánh giá trên working tree hiện tại, không phải một commit release bất biến. Không commit, reset, xóa database hay backup database ứng dụng. Database SQL dùng trong test là database riêng do fixture tạo và dọn.

## Thay đổi đã thực hiện

| Finding | Thay đổi | Giới hạn |
|---|---|---|
| F01 — hash trong audit | AuditSensitiveData nhận diện tên trường nhạy cảm; interceptor bỏ trường mật khẩu/hash/secret/token khỏi snapshot; AuditLogService làm sạch JSON khi ghi và khi đọc log cũ; API và trang audit yêu cầu system.auditlog.view | Không xóa hoặc làm sạch trực tiếp các bản ghi lịch sử trong DB; không thay thế secret scan toàn hệ thống |
| F02 — ngân hàng | Thêm system.bankaccount.view và system.bankaccount.manage vào catalog; tất cả đường xem trong StoreBankAccountsController có quyền xem, sửa/tắt/bật/set default có quyền quản lý | Quyền manage bao hàm view theo alias map hiện có; POS lấy thông tin thanh toán qua luồng POS hiện tại không bị đổi |
| F03 — điều chỉnh tồn trực tiếp | API cũ yêu cầu inventory.adjustment.view; POST ghi tồn ngay phải có cả create và approve; giữ antiforgery | Giữ đường điều chỉnh trực tiếp cho người có đủ quyền. Nếu nghiệp vụ yêu cầu hai người tạo/duyệt khác nhau thì cần loại bỏ đường trực tiếp trong đợt quy trình |
| F04 — cookie cũ | AuthService tạo fingerprint trạng thái xác thực trong LoginResponse nội bộ (JsonIgnore), AccountController đưa vào cookie được mã hóa; SessionPrincipalValidator đọc lại user/membership/role và từ chối phiên không còn khớp; cookie không có stamp cũng bị từ chối | Áp dụng tại request HTTP tiếp theo; chưa có cơ chế chủ động ngắt kết nối SignalR đã mở trước khi quyền bị thu hồi |
| F04 — cache quyền | Bỏ cache quyền 3 phút giữa các request; repository kiểm tra user còn hoạt động và role cùng cửa hàng; thu hồi permission có hiệu lực ở lần kiểm tra tiếp theo | Tăng số query quyền; cần đo chi phí trong đợt tải và chỉ cache lại nếu có invalidation/version bảo đảm |
| F11 — catch rỗng | Tách parse OAuth allowlist thành hàm fallback trả null; tín hiệu callback đầy semaphore trả về vì đã có wake đang chờ | Giữ semantics và không log response body/token ngân hàng |

Fingerprint dùng PasswordHash, SQL RowVersion của user/membership/role và identity của membership/role; không thêm cột hoặc migration. Timestamp được chuẩn hóa bằng ticks để không lệch do DateTime.Kind khi SQL đọc lại. PasswordHash thô không được đưa vào claim/DTO xuất JSON; fingerprint nằm trong cookie xác thực được ASP.NET Core bảo vệ. Mọi thay đổi RowVersion liên quan có thể yêu cầu đăng nhập lại, kể cả chỉnh thông tin tài khoản, đây là lựa chọn thu hồi thận trọng trong đợt này.

## Cách áp dụng tại local

1. Bản app đang chạy giữ DLL Release cũ. Vì vậy đã build vào thư mục riêng thay vì dừng process của người dùng. Source đã sửa, process hiện tại chưa được khởi động lại bởi task này.
2. Khi dừng/chạy lại app từ source mới ở Development, quy trình hiện có gọi SeedPermissionsAsync và SeedDefaultRolesForAllStoresAsync. ADMIN nhận hai quyền ngân hàng mới qua bộ seed hiện tại. Không tự cấp quyền quản lý ngân hàng cho CASHIER hoặc MANAGER; có thể cấp bằng trang quản lý quyền khi nghiệp vụ yêu cầu.
3. Cookie trước bản nâng cấp không có stamp nên phải đăng nhập lại. Không xóa database, tài khoản hay giao dịch test để thực hiện việc này.
4. Production chạy Migrator/mandatory security seeder theo quy trình hiện có trước khi chạy web; không trông chờ web Production tự seed. Chưa thực hiện thao tác production trong đợt này.

Không nên lấy thư mục output của test làm bộ publish để đưa lên host. Đây là artifact kiểm tra. Đợt host sẽ tạo publish và cấu hình triển khai riêng.

## Kiểm tra đã chạy

- Build solution Release với `--artifacts-path Logs/security-phase1-release`: thành công, 0 errors, 10 warnings cũ ở test code.
- Chạy chính DLL test Release vừa build: **314 passed, 0 failed, 0 skipped** cho nhóm Security (trừ SQL chạy riêng), Tenant, Data, Observability và các partial AcbPaymentTests.
- SQL LocalDB riêng: **1 passed, 0 failed, 0 skipped**, kiểm tra stamp với RowVersion thật, reset mật khẩu, disable/re-enable membership và seed quyền ngân hàng ADMIN/CASHIER.
- Test HTTP sử dụng routing/controller metadata và authorization middleware/provider/handler thật; identity và nguồn permission là fixture, business execution được short-circuit. Bao phủ anonymous, thiếu từng quyền, đủ quyền và CSRF cho 8 route audit/ngân hàng/kho. Đây không phải browser E2E hay luồng thanh toán thật.
- Test audit thực hiện create/update/delete với interceptor, ghi service và đọc bản ghi lịch sử. Test permission dùng repository EF để kiểm tra thu hồi grant ngay. Test callback wake mô phỏng 50 thông báo đồng thời, không phải bài đo 50 client.
- Không chạy toàn bộ solution test suite, load test 10/20/50, deploy host, hoặc gọi ngân hàng thật.

Evidence cuối cùng:

- `Logs/security-phase1-release-build.log`
- `Logs/security-phase1-release-regression.log`
- `Logs/security-phase1-release-sql.log`
- `TestResults/security-phase1/security-phase1-release-regression.trx`
- `TestResults/security-phase1/security-phase1-release-sql.trx`

Các log Debug/trung gian trong cùng thư mục có thể chứa test fail đã sửa sau đó. Kết quả nghiệm chứng của đợt này là ba log và hai TRX Release nêu trên. Build Release tại thư mục thông thường bị file lock trước khi đổi sang output riêng; đây không phải lỗi compile.

## Phần tiếp theo trước production

1. Rà tiếp toàn bộ endpoint và phiên SignalR đang mở, kiểm thử vai trò thực tế; không coi các finding ban đầu là danh sách đầy đủ mọi lỗ hổng.
2. Sửa limiter đăng nhập chung IP, proxy trust, storage/media/key và mặc định ngân hàng khi ghi đồng thời.
3. Hoàn thiện quy trình migration/publish và kiểm tra phục hồi cho giai đoạn dữ liệu thật. Việc không backup data test hiện tại không đồng nghĩa production sẽ bỏ backup.
4. Đo POS cùng báo cáo lợi nhuận trên staging, 10 → 20 → 50 client, tối ưu SQL và nghiệm thu nghiệp vụ.

Đợt 1 đã có kết quả kiểm tra local; chưa phải xác nhận app hoàn chỉnh hoặc sẵn sàng mở Internet, và chưa có independent review/CI trên một commit phát hành.
