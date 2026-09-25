> **Superseded deployment procedure (2026-09-13):** Use [the current TH1/TH2 runbook](GAOAPP-DEPLOYMENT-RUNBOOK-20260913.md). The historical commands/pipeline below are context only: Migrator without a mode now fails closed; normal migration uses `--schema-only`, with all seed/bootstrap flags false. Seed/bootstrap require a separate explicitly approved data operation. Do not run historical migration+seed instructions as normal deployment.

# Đợt 6 — Gói triển khai staging và kiểm thử Production

> Thông tin mới và gói thay thế nằm trong [báo cáo đợt 7](SECURITY-PHASE7-IIS-LOCAL-AUDIT-20260909.md): host là IIS trên máy chủ riêng. Không dùng lại gói đợt 6 để bỏ qua các bản vá mới.

Đã chuẩn bị gói publish và quy trình đưa lên staging. Chưa triển khai lên host: chưa có thông tin hệ điều hành, domain staging, SQL và cách truy cập host. Không áp migration lên database ứng dụng, không restart app đang chạy, không backup/reset dữ liệu test của người dùng.

## Hai vấn đề đã xử lý

**Gói publish có thể mang dữ liệu/cấu hình local.** Trước đây project chưa loại rõ App_Data, upload và appsettings môi trường. SDK còn có pipeline static-web-assets riêng: chỉ lọc danh sách publish cuối chưa đủ để chặn upload. Đã thêm bộ lọc ở cả đầu vào static assets và đầu ra publish. Không đóng gói Development/config local, uploads, key/certificate, log, database, archive/PDB. Thay appsettings trong bản publish bằng template được chọn rõ cho Web/Migrator, không có connection string hay mật khẩu và bắt buộc cấu hình host.

Quy tắc này áp dụng cả publish trực tiếp/Visual Studio, không chỉ script mới. Vì vậy giá trị domain/SQL/storage từ appsettings.Production.json trong source không tự chuyển sang release nữa. Các feature đang tắt trong template cần được cấu hình rõ khi triển khai. Development vẫn dùng file cấu hình local khi chạy từ source.

**Health check có thể làm người đang đăng nhập mất phiên.** Endpoint health bỏ qua tenant nhưng trước đây vẫn đi qua kiểm tra cookie. Cookie hợp lệ của một cửa hàng bị đánh giá sai vì request không có tenant, dẫn đến phản hồi xóa cookie. Kiểm thử bản publish đã tái hiện lỗi khi dùng cookie đó để chờ Web khởi động lại. Hai health endpoint nay xử lý xong trước tenant/terminal/authentication; vẫn qua kiểm tra Host, forwarded headers, HTTPS/HSTS và logging. Các đường nghiệp vụ tiếp tục xác minh phiên và quyền như trước.

## Công cụ và gói đã tạo

- `eng/deployment/PublishSafety.targets`, hai template appsettings Web/Migrator và hai bảng biến môi trường host/bootstrap không chứa secret thật.
- `scripts/publish-staging-release.ps1`: build/publish vào thư mục mới, đối chiếu ba assembly dùng chung giữa Web và Migrator, ghi manifest SHA-256 của từng file. Không copy bin của app đang mở. Manifest đánh dấu source có thay đổi chưa commit; hash giúp phát hiện file bị thay đổi, không phải chữ ký xác thực nguồn.
- `scripts/test-release-package.ps1`: chặn đường dẫn/file không được đóng gói, cấu hình mặc định có secret hoặc demo seed, lệch phiên bản và lệch manifest.
- `scripts/test-host-preflight.ps1`: kiểm tra cấu hình của tài khoản đang chạy script, HTTPS origins, SQL encryption settings, path/quyền ghi, certificate và runtime. Không mở SQL connection hay sửa quyền. Phải kiểm tra tiếp trên dịch vụ thật.
- `scripts/test-staging-endpoint.ps1`: HTTP smoke từ ngoài host với xác minh TLS mặc định; health, login/CSRF, static assets và đường nhạy cảm. Chưa chạy trên host thật.
- `scripts/test-published-production.ps1` và `scripts/test-deployment-tools.ps1`: chạy lại các kiểm chứng local bên dưới.
- CI đã được bổ sung các bước publish/kiểm tra này trong source. Chưa có lượt GitHub Actions từ xa được chạy trong phiên này.

Release cuối: `publish/releases/20260909-085042-a65bfffd` gồm `web`, `migrator` và `release-manifest.json`; 615 file payload, khoảng 131 MiB. Đây là gói cần **cấu hình trên host trước khi khởi động**, không chứa PFX, user/database test hoặc thông tin host thật. Các gói thử trung gian đã được dọn để tránh chọn nhầm.

## Kiểm chứng đã chạy

| Kiểm tra | Kết quả | Bằng chứng |
|---|---|---|
| Hồi quy Security/Tenant/Observability/StartupOrdering và published Production | 234/234 đạt, 0 skipped, 3 phút 47 giây | `TestResults/security-phase6/phase6-regression.trx` |
| Chạy lại trên release cuối sau bổ sung bộ lọc static assets | 2/2 đạt, 0 skipped, 26 giây | `TestResults/security-phase6/published-production.trx`, `Logs/security-phase6-smoke.log` |
| Công cụ triển khai: cấu hình hợp lệ và các trường hợp phải từ chối | 10/10 đạt | `Logs/security-phase6-tool-checks.log` |
| Build Release | 0 lỗi; 10 warning test đã có | `Logs/security-phase6-build.log` |
| Publish, đối chiếu Web/Migrator và hash trước/sau smoke | Đạt | `Logs/security-phase6-publish.log`, manifest và smoke log |

Hai test Production là tập con của lượt 234, sau đó chạy lại trên gói cuối; không cộng thành 236 test độc lập. Không chạy lại 39 test JavaScript của đợt 5 vì không sửa JavaScript ở đợt này.

Kiểm thử Production chạy **file DLL từ thư mục publish thật**, dùng database LocalDB ngẫu nhiên qua Migrator thật. Xác minh migration mới nhất; bootstrap đúng một cửa hàng/chủ tài khoản, chạy lại không thêm người dùng/dữ liệu demo; health chỉ trả status; HTTPS redirect/HSTS; Host lạ bị 400; debug/config/invoice không truy cập công khai; chưa login không mở POS; cookie login Secure/HttpOnly; POS cùng JS/CSS đã publish tải được; khóa XML được mã hóa bằng PFX; health không xóa/đổi cookie; restart Web dùng lại cookie/key ring thành công. Ngoài ra xác minh Web từ chối thiếu certificate, thiếu connection string và upload đặt trong publish.

PFX trong test là certificate tự ký chỉ dùng tại loopback và được pin chính xác trong HttpClient của test, không thêm vào certificate store và không tắt TLS validation của script staging. PFX, thư mục runtime, database và child process test được dọn. Bộ tool checks dùng connection string tổng hợp để kiểm tra cấu trúc; không kết nối tới server trong chuỗi đó.

Đã thêm ba file canary giả trong wwwroot (`.pfx`, `.env...`, `appsettings...json`) khi đóng gói cuối: cả ba bị loại. File canary được dọn khỏi source sau publish. Upload test vốn có cũng không xuất hiện trong release. Những kiểm chứng này không thay cho một cuộc kiểm thử xâm nhập toàn diện.

## Việc tiếp theo phụ thuộc host

Theo [hướng dẫn triển khai staging](STAGING-DEPLOYMENT-RUNBOOK.md): xác định Windows/IIS hay Linux, domain/SQL/đường truy cập; cấp storage/PFX/secret và quyền đúng tài khoản; chạy Migrator, mở Web sau khi kiểm tra đạt; kiểm tra DNS/HTTPS/proxy/WebSocket từ máy khác. Sau đó UAT máy in/scanner/màn hình phụ, hai cửa hàng/các vai trò, ACB sandbox và đo tải 10/20/50 client độc lập trên host.

Chưa có số đo host hoặc nghiệm thu thiết bị/ngân hàng thực tế nên chưa kết luận app đủ 50 client hay sẵn sàng vận hành production. Hiện vẫn dùng một Web instance như các đợt trước. Readiness xác minh kết nối DB/storage, không thay kiểm tra migration và nghiệp vụ.
