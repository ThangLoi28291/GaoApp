> **Superseded deployment procedure (2026-09-13):** Use [the current TH1/TH2 runbook](GAOAPP-DEPLOYMENT-RUNBOOK-20260913.md). The historical commands/pipeline below are context only: Migrator without a mode now fails closed; normal migration uses `--schema-only`, with all seed/bootstrap flags false. Seed/bootstrap require a separate explicitly approved data operation. Do not run historical migration+seed instructions as normal deployment.

# Triển khai staging từ release đã kiểm tra

Staging dùng `Production` làm tên môi trường của cả Web và Migrator để thực thi các ràng buộc bảo mật đang có. Staging có domain, database, tài khoản và key ring riêng. Không copy database test local. Gói publish không có cấu hình host và sẽ từ chối khởi động khi thiếu cấu hình bắt buộc.

Host đang vận hành cần giữ nguyên menu: xem `PUBLISH-EXISTING-HOST-20260913.md`. Migrator hiện vẫn chạy seed menu hệ thống sau migration dù demo seed đã tắt; quy trình dùng script SQL đã kiểm tra trong tài liệu đó tách cập nhật database khỏi bước seed này.

## 1. Tạo và xác minh release tại máy phát triển

```powershell
./scripts/publish-staging-release.ps1
./scripts/test-deployment-tools.ps1 -ReleasePath '<đường dẫn release vừa tạo>'
./scripts/test-published-production.ps1 -ReleasePath '<đường dẫn release vừa tạo>' -ArtifactsPath Logs/security-phase6-build
```

Script publish tạo thư mục mới trong `publish/releases`, build Web và Migrator từ source hiện tại, đối chiếu ba assembly dùng chung và ghi SHA-256 của từng file vào `release-manifest.json`. Manifest ghi cả việc source có thay đổi chưa commit; commit ID đơn lẻ không đại diện đầy đủ cho source đang bẩn. Không chỉnh sửa source trong lúc đóng gói.

`dotnet publish` trực tiếp hoặc publish từ Visual Studio cũng dùng `eng/deployment/PublishSafety.targets` và hai template cấu hình sạch. Cấu hình local/Production trong project không còn được copy vào release: thiết lập domain, SQL, storage, secret và tính năng của host phải cấp rõ ràng từ môi trường. Upload, App_Data, key/certificate, log, database, archive và PDB được loại khỏi publish; upload còn được loại ở pipeline static-web-assets riêng của SDK.

Chỉ dùng gói có manifest và kiểm tra đạt. Không copy `bin`, `Logs/security-phase4-release` hay cả workspace lên host. Không publish đè vào thư mục đang chạy. Bộ smoke cần Windows, SQL LocalDB và .NET 8, tạo database/HTTPS process/PFX test riêng rồi dọn; không phải test host thật.

## 2. Chuẩn bị host trước khi chạy app

- Một Web instance trong giai đoạn đầu; không bật web garden/nhiều replica. Realtime revocation và hạn chế tải hiện có phần lưu trong từng process. 50 client không đồng nghĩa 50 Web process.
- Cài bản vá phù hợp của .NET 8 và ASP.NET Core 8; Windows/IIS cần Hosting Bundle và WebSocket support. Linux cần reverse proxy có chuyển tiếp WebSocket. Chọn cấu hình IIS hoặc Linux sau khi xác định host thật.
- Tạo database SQL Server staging riêng. Connection string phải trỏ đúng database, dùng `Encrypt=True;TrustServerCertificate=False` và certificate SQL được host tin cậy. Không dùng LocalDB cho host. Ví dụ cấu trúc: `Server=tcp:sql-host,1433;Database=GaoAppStaging;User ID=...;Password=...;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True;Connect Timeout=15;Max Pool Size=100`. Cấp giá trị thật bằng secret của host; không lưu vào Git/chat.
- Tách credential chạy Migrator có quyền thay schema khỏi credential Web chỉ đủ đọc/ghi dữ liệu nghiệp vụ. Xác minh quyền thực tế ở staging; không cấp `sysadmin`/`db_owner` cho Web để chữa lỗi quyền. Việc cấu hình role SQL chưa được script tự thực hiện.
- Tạo các thư mục uploads, Data Protection keys, certificate, logs **ngoài mọi thư mục release**. Tài khoản chạy Web cần ghi uploads/keys/logs, đọc PFX và chỉ đọc release. Đặt keys/PFX ngoài uploads. Chạy preflight bằng chính tài khoản dịch vụ để kiểm tra quyền có ý nghĩa.
- Cấp certificate RSA có private key tối thiểu 2048 bit và mật khẩu cho Data Protection. Giữ cùng key ring, PFX và mật khẩu qua các lần deploy/restart của cùng môi trường. Certificate này dùng bảo vệ dữ liệu/khóa; chứng chỉ TLS của website là cấu hình riêng. Không mang key DPAPI local sang máy khác, không dùng PFX test tự sinh bởi bộ smoke lên host.
- Cấu hình DNS/TLS cho root, admin và các cửa hàng: ví dụ `staging.example.com`, `admin.staging.example.com`, `shop.staging.example.com`. Chỉ mở HTTP/HTTPS qua reverse proxy; backend Kestrel và SQL giới hạn trong mạng nội bộ. Proxy truyền Host/Scheme/client IP và WebSocket upgrade. `KnownProxies` chỉ chứa IP proxy thật; loopback trong template áp dụng khi proxy cùng máy. Không bật cơ chế tự tin mọi forwarded header.

`eng/deployment/host-environment.example.json` là danh sách biến môi trường tham khảo, không phải file app tự nạp. Thay placeholder, đưa giá trị vào cấu hình dịch vụ/secret store của host. Web và Migrator đều đọc dấu `__` cho khóa lồng nhau. Nếu dịch vụ thu console log vào hệ thống quản lý log thì có thể bỏ toàn bộ sink File; nếu dùng File phải đặt đường dẫn và kiểm tra quyền/rotation. Migrator dùng tên log riêng.

Gói mặc định tắt demo seed, bootstrap, tra mã số thuế ngoài, thư viện hóa đơn và các feature workbench đang tắt ở source. Kiểm tra từng feature cần dùng trước UAT. ACB/Viettel dùng cấu hình sandbox riêng qua màn hình/config hiện có; không nhập secret ngân hàng production để chạy thử tải.

## 3. Preflight, migration rồi mới mở Web

Copy nguyên thư mục release cùng `scripts/test-release-package.ps1`, `scripts/test-host-preflight.ps1`, `scripts/test-staging-endpoint.ps1` tới vị trí công cụ riêng trên host. Các script host dùng PowerShell 7. Sau khi cấp môi trường cho đúng tài khoản dịch vụ:

```powershell
./test-host-preflight.ps1 -ReleasePath '<release trên host>' -Component Migrator
# Chạy bằng credential migration, từ thư mục migrator của release:
dotnet GaoApp.Migrator.dll
# Chỉ khi Migrator kết thúc thành công, chuyển sang môi trường/credential Web:
./test-host-preflight.ps1 -ReleasePath '<release trên host>' -Component Web
```

Preflight xác minh hash, cặp assembly, môi trường, URL HTTPS, cấu trúc connection string, khả năng ghi thư mục, certificate và runtime. Nó **không mở SQL connection, không migrate, không thay quyền và không chứng minh DNS/TLS của proxy đúng**. Thư mục đã cấp phải tồn tại. Script không xuất mật khẩu/connection string; chỉ báo tên cấu hình lỗi.

Với database staging hoàn toàn mới, điền các biến trong `bootstrap-environment.example.json`, bật `ProductionBootstrap__Enabled=true` **chỉ cho lần chạy Migrator cần khởi tạo**. Tên cửa hàng, pháp nhân, kho, terminal và người quản trị phải được người vận hành chọn; mật khẩu chủ tài khoản ít nhất 16 ký tự, có chữ hoa/thường/số/ký tự đặc biệt. Migrator đã kiểm tra bootstrap chỉ phù hợp trạng thái trống hoặc đã được khởi tạo khớp. Không bật demo seed. Sau thành công bỏ biến mật khẩu bootstrap và tắt bootstrap; Web không nhận các secret này.

Web không tự migrate ở Production. Release mới cần migration `20260909080000_AddPosCollectionIdempotency`; triển khai Web/JavaScript cùng release sau Migrator. Tải lại các tab POS cũ để dùng request thanh toán có mã chống trùng. Dừng tại lỗi Migrator/preflight; không bật Web bằng cách bỏ kiểm tra hay hạ cấu hình về Development.

Khởi động Web bằng IIS/service đã cấu hình đúng working directory là thư mục `web`; lệnh runtime bên trong dịch vụ là `dotnet GaoApp.Web.dll`. Không khởi động thêm bản song song cùng database để thử nếu chưa xử lý worker/realtime của nhiều instance.

## 4. Kiểm tra từ máy khác

```powershell
./test-staging-endpoint.ps1 -StoreUrl https://shop.staging.example.com
```

Script giữ xác minh TLS mặc định; kiểm tra health/live, health/ready, HSTS, trang login/token CSRF, JS/CSS thật và đường dẫn nhạy cảm không công khai. Readiness hiện kiểm tra kết nối DB/storage; không chứng minh schema đúng hay toàn bộ nghiệp vụ đúng. Manifest/Migrator và UAT là các kiểm tra bổ sung bắt buộc của lần triển khai.

Nghiệm thu tài khoản quản trị/thu ngân/chỉ xem tại hai cửa hàng; xác nhận không đọc chéo dữ liệu. Đi hết mở ca → bán → thu từng phần → chốt → hoàn/void → đóng ca; so tiền mặt, công nợ, tồn và lịch sử. Kiểm tra mất phản hồi rồi gửi lại, đổi giỏ, tải lại tab và restart Web. Giữ mã thu đang chờ khi đối soát, không xóa sessionStorage để thử lại tiền chưa rõ kết quả. Test máy in/scanner/màn hình phụ thực tế, thu hồi quyền khi POS đang mở và WebSocket qua proxy. ACB thử bằng sandbox trước.

Đo tải **từ máy khác** trên host thật với 10/20/50 tài khoản/ca/terminal độc lập: warm-up, tải ổn định, burst và tải kéo dài; bao gồm bán hàng, tra cứu tồn và xuất báo cáo. Ghi tỷ lệ lỗi, p50/p95/p99, 429/409/503 theo nghiệp vụ, CPU/RAM, SQL CPU/locks/pool, độ dài hàng chờ và WebSocket disconnect. Đặt mục tiêu độ trễ trước khi chạy, so lại tiền/tồn sau tải. Health check nhanh và burst cùng một mã thu tiền không thay bài đo này. Chưa có số đo staging thì chưa kết luận host đủ 50 client.

Nếu cần quay lại release cũ: dừng traffic, đối soát khoản thu đang chờ, xác minh app cũ tương thích schema mới rồi mới chuyển release. Không tự chạy Down migration xóa `ClientRequestId` vì làm mất lịch sử chống trùng. Quyết định không backup dữ liệu test local không có nghĩa có thể xóa dữ liệu khách hàng sau khi vận hành thật.

Tham khảo cơ chế chính thức: [Microsoft — dotnet publish](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish), [cấu hình ASP.NET Core và biến môi trường](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-8.0), [loại file khỏi publish](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/visual-studio-publish-profiles?view=aspnetcore-8.0).
