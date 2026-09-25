# Đánh giá source, bảo mật và khả năng chạy 10–20–50 client

> Cập nhật đợt 7: [bản vá phân quyền, bảo vệ tài khoản giữa cửa hàng và kiểm tra bảo mật local cho IIS](SECURITY-PHASE7-IIS-LOCAL-AUDIT-20260909.md). Host đã được xác nhận là IIS; chưa triển khai, kiểm thử tải host làm sau.

> Cập nhật đợt 6: [gói staging, lỗi health làm mất phiên và kiểm thử Production](SECURITY-PHASE6-PUBLISHED-STAGING-20260909.md).

> Cập nhật đợt 5: [thu tiền chống gửi trùng và kiểm thử POS](SECURITY-PHASE5-PAYMENT-IDEMPOTENCY-20260909.md). Các nhận định bên dưới là kết quả khảo sát trước sửa.

> Cập nhật sau khảo sát: xem SECURITY-PHASE1-REMEDIATION-20260909.md cho các thay đổi F01–F04 và F11 cùng kết quả kiểm tra mới. Nội dung bên dưới giữ nguyên làm bằng chứng trước sửa.

> Đợt 2: xem SECURITY-PHASE2-HOST-PREPARATION-20260909.md cho F05, F07–F09, phần key encryption của F10 và quy trình migration. F06 và đo tải toàn ứng dụng còn tiếp tục.

> Đợt 3–4: xem [báo cáo và tải local](SECURITY-PHASE3-REPORTS-LOAD-20260909.md) và [thu hồi realtime/kiểm thử nghiệp vụ](SECURITY-PHASE4-REALTIME-WORKFLOWS-20260909.md) cho kết quả mới, giới hạn và các việc còn lại trước production.

Ngày: 09/09/2026. Phạm vi: working tree hiện tại của GaoApp, gồm cả thay đổi chưa commit.
Branch: `fix/r2-4-c1-invoice-identity-uniqueness`. HEAD nền: `db6a6c5e98b69472624eeb31aa6be2161576c3d2`.

## 1. Kết luận để quyết định triển khai

**Chưa đủ điều kiện mở production ra Internet.** Hệ thống đã có nhiều chức năng và các lớp bảo vệ hữu ích, nhưng còn lỗi phân quyền và nguy cơ lộ password hash qua audit. Cần xử lý các mục P1 trước khi đưa dữ liệu thật lên host.

**Chưa có số đo chứng minh phục vụ tốt 10, 20 hoặc 50 client.** Năm test tranh chấp SQL đạt chứng minh các tình huống cụ thể trong fixture, không chứng minh throughput hoặc độ trễ của toàn ứng dụng. Điểm nghẽn đáng chú ý nhất từ source là báo cáo lợi nhuận đọc nhiều bảng trong transaction Serializable, cộng với số query nền trên mỗi request và polling của màn hình POS.

Đây là khảo sát có trọng tâm, không phải chứng nhận pentest toàn hệ thống. Không sửa runtime/schema, không chạy tải lên database nghiệp vụ, không thực hiện thanh toán thật. Các finding dưới đây là hiện trạng source; chưa thử khai thác qua HTTP với tài khoản thật.

## 2. Đã có gì, phần nào chưa thể coi là hoàn tất

| Nhóm | Bằng chứng hiện có | Phần cần hoàn thiện/xác nhận |
|---|---|---|
| Kiến trúc | Web MVC .NET 8, Application, Domain, Infrastructure EF Core SQL Server, Migrator, Tests; solution build được | Chốt bản release chứa đầy đủ thay đổi chưa commit và CI trên đúng bản đó |
| Tài khoản, tenant, quyền | Tenant theo host; cookie HttpOnly/Secure ngoài Development; kiểm tra user/store còn hoạt động mỗi request; permission handler đọc DB; test tenant/security | Quyền không áp dụng đồng đều tại các endpoint; thu hồi phiên khi reset mật khẩu/đổi vai trò còn thiếu |
| Danh mục | Controller/service/repository cho hàng hóa, đơn vị, thuế, thương hiệu; ProductRepository phân trang SQL, AsNoTracking, chặn pageSize trên 200 | Chưa chạy UAT mọi màn hình; rà tiếp quyền sửa/xóa và các endpoint phân trang cũ |
| POS, kho | POSService có transaction; InventoryMovementService có idempotency và thứ tự khóa; năm test SQL concurrency đạt | Đo cùng SKU/cùng kho/cùng đơn dưới tải thật; không suy rộng từ năm test thành mọi luồng đều an toàn |
| Mua hàng, nhập hàng, pháp nhân, XML | Có controller, service, migration và nhiều test riêng trong source | Chưa chạy toàn bộ suite hoặc UAT xuyên luồng PO → nhập → hóa đơn → trả hàng trong đợt này |
| ACB và QR | Webhook kiểm tra API key; ghi inbox trước ACK; chống payload trùng mâu thuẫn; worker xử lý theo tenant với mức song song 4 và batch 50 | Test từ Internet trên hostname thật, restart/retry, đối soát và quota ngân hàng; chưa xác nhận tích hợp production |
| Báo cáo | ProfitReport controller → service → repository và aggregation; giới hạn kỳ tương tác 366 ngày | Đọc dữ liệu lớn rồi phân trang trong RAM; transaction đọc có thể cản POS |
| Upload và hóa đơn | Kiểm tra dung lượng, extension, MIME, chữ ký file; chặn static `/uploads/invoices`; có Data Protection | Storage media vẫn dùng webroot dù Production khai báo thư mục ngoài publish; key portability/restore cần diễn tập |
| Vận hành | Global exception middleware, health live/ready, log rolling, validation startup, CI build/test/publish; web Production không tự migrate | Chưa có bằng chứng phục hồi backup, đo tải 10/20/50, giám sát lock/queue hoặc nghiệm thu host |
| Cảnh báo tồn kho | OrderInventoryIssueService cập nhật overdue/severity | `UpdateOverdueFlagAsync` và `UpdateOverdueFlagsAsync` còn TODO tạo notification/push; không coi phần thông báo chủ động là đã xong |

Các dòng chỉ ghi “có controller/test” là kiểm kê source, không phải xác nhận nghiệp vụ đã nghiệm thu.

## 3. Finding cần ưu tiên

Quy ước: P1 cần giải quyết trước production; P2 cần xử lý trước nghiệm thu vận hành/tải; P3 là hoàn thiện. “Xác nhận từ source” khác với “đã tái hiện bằng HTTP”.

### F01 — P1: Audit có thể làm lộ password hash cho người chỉ có quyền đăng nhập

- `GaoApp.Domain/Entities/User.cs:15`: User implement `IAuditTrackedEntity`.
- `GaoApp.Application/Services/Security/UserInStoreAdminService.cs`, `ResetPasswordAsync`: thay đổi `mapping.User.PasswordHash` rồi SaveChanges.
- `GaoApp.Infrastructure/Interceptors/AuditSaveChangesInterceptor.cs:37`: danh sách bỏ qua chỉ chứa trường thời gian/người sửa/RowVersion; `BuildPendingAudit` và `DetectModifiedValues` chụp giá trị thuộc tính, serialize vào OldValuesJson/NewValuesJson.
- `HttpAuditExecutionContextAccessor.GetCurrent` cung cấp StoreId từ tenant của request, nên User dù là global vẫn có thể có audit gắn cửa hàng.
- `GaoApp.Web/Areas/Admin/Controllers/AuditLogsApiController.cs:9` chỉ có `[Authorize]`; GetDetail → AuditLogService.GetDetailAsync trả OldValuesJson/NewValuesJson. Không có kiểm tra quyền xem audit trong chuỗi đã đọc.
- Tác động: tài khoản thường cùng cửa hàng có đường đọc hash từ audit đã phát sinh, hỗ trợ dò mật khẩu offline. Không khẳng định database hiện tại đã có hash bị lộ vì không đọc dữ liệu thật.
- Cần làm: loại bỏ/redact password/hash/secret/token bằng chính sách dùng chung; yêu cầu permission riêng cho audit; kiểm tra và làm sạch audit lịch sử có kiểm soát; nếu xác nhận đã bị truy cập trái phép thì xử lý credential/phiên bị ảnh hưởng.
- Nghiệm thu: reset/create user không lưu hash vào audit; user không có quyền nhận 403 ở cả list/detail; người có quyền cũng không được xem hash.

### F02 — P1: Người đăng nhập có thể sửa cấu hình tài khoản ngân hàng

- `GaoApp.Web/Areas/Admin/Controllers/StoreBankAccountsController.cs:9,74,100`: Edit, ToggleStatus, SetDefault chỉ được bảo vệ bởi đăng nhập và antiforgery.
- `StoreBankAccountService` và `StoreBankAccountRepository` không kiểm tra permission người gọi; cho sửa AccountNumber/BankCode, trạng thái và tài khoản mặc định.
- Tác động: nhân viên có phiên hợp lệ có thể thay đổi nơi nhận tiền/cấu hình QR trong cửa hàng dù UI không hiển thị menu. Antiforgery không thay thế phân quyền.
- Cần làm: permission riêng cho xem và quản lý tài khoản ngân hàng, kiểm tra tại backend; audit thay đổi cấu hình nhận tiền; rà đồng thời các controller danh mục chỉ có BaseAdminController.
- Nghiệm thu: cashier không được sửa/set default kể cả gửi trực tiếp request hợp lệ với CSRF token của chính họ; quản lý được cấp quyền vẫn thao tác được.

### F03 — P1: API điều chỉnh tồn kho cũ thiếu quyền nghiệp vụ

- `GaoApp.Web/Areas/Admin/Controllers/InventoryAdjustmentsController.cs:11,35`: POST `/admin/api/inventory-adjustments` chỉ yêu cầu đăng nhập.
- `InventoryAdjustmentService.CreateAsync` kiểm tra loại điều chỉnh, số lượng, kho, đơn vị rồi gọi InventoryMovementService, không kiểm tra quyền của người gọi trong luồng này.
- Tác động: nhân viên hợp lệ có thể điều chỉnh tồn qua API cũ mà không đi qua luồng phê duyệt chứng từ mới. Cách ly tenant không giải quyết vượt quyền trong cùng tenant.
- Cần làm: khóa API bằng permission và xác định có tiếp tục cho phép đường điều chỉnh trực tiếp hay chuyển hoàn toàn sang chứng từ. Phải giữ quy tắc phê duyệt/ghi nhận tồn thống nhất.
- Nghiệm thu: role chỉ bán hàng bị 403; không có endpoint cũ vượt qua quy trình duyệt đã chọn.

### F04 — P1: Đổi mật khẩu/role chưa thu hồi hoặc làm mới cookie cũ đầy đủ

- `GaoApp.Web/Program.cs:223`, OnValidatePrincipal chỉ đối chiếu user, membership, store, tình trạng hoạt động và role chưa bị xóa. Không đối chiếu phiên bản mật khẩu/security stamp hay RoleId trong cookie với RoleId hiện tại.
- AccountController đưa role vào claims lúc đăng nhập; `InvoiceCorrectionController` dùng `[Authorize(Roles = "ADMIN")]`.
- Tác động: reset mật khẩu không tự vô hiệu cookie đã lấy cắp; hạ vai trò có thể để lại quyền dựa trên role claim cũ. Policy động đọc DB có thể phản ánh quyền mới, nhưng không làm role claim cũ tự cập nhật. Cookie có sliding expiration nên không nên mặc định rủi ro chỉ kéo dài 8 giờ.
- Cần làm: session/security version, invalidation khi reset/disable/change role, làm mới claims hoặc chuyển endpoint nhạy cảm sang permission hiện hành.
- Nghiệm thu: cookie trước reset/thu hồi bị từ chối; hạ role thì request tiếp theo mất quyền cũ.

### F05 — P2: Giới hạn đăng nhập 5 lần/phút/IP gây nghẽn cho nhóm máy chung mạng

- `GaoApp.Web/Program.cs:299`: fixed window 5 request POST login mỗi phút cho một RemoteIpAddress; không phân biệt đăng nhập thành công hay thất bại, không partition theo account/store.
- Với 10/20/50 máy qua cùng public IP, trong cùng một fixed window chỉ tối đa 5 POST được limiter cho qua; phần còn lại nhận 429. Đây là suy ra trực tiếp từ cấu hình, chưa là benchmark HTTP. Máy có IP khác sẽ không chia bucket này.
- AuthService chưa có bộ đếm thất bại/lockout theo account trong luồng đã đọc; chỉ giới hạn IP chưa đủ chống dò phân tán.
- Cần làm: phối hợp ngưỡng IP phù hợp số người dùng, ngưỡng theo account/tenant cho thất bại, backoff và bảo vệ tại proxy. Không bỏ chống brute force để giải quyết nghẽn. Nếu nhiều instance, limiter trong RAM không phải quota chung.
- Nghiệm thu: đăng nhập đồng thời từ một NAT đáp ứng yêu cầu vận hành; tấn công vào cùng tài khoản vẫn bị giới hạn.

### F06 — P2: Báo cáo lợi nhuận có nguy cơ khóa luồng bán hàng và tăng RAM

- `ProfitReportReadRepository.cs:26`: transaction Serializable bao trùm đọc snapshot nhiều bảng; MaterializeAsync tải orders/lines/returns/valuation và liên kết thành danh sách.
- `ProfitReportReadService.ReadAsync` aggregate/filter trước Skip/Take trong RAM. PageSize nhỏ không giới hạn lượng dữ liệu được đọc.
- `SalesReportingPeriodPolicy` cho 366 ngày; bật so sánh kỳ trước còn mở rộng khoảng dữ liệu cần lấy.
- Tác động dự kiến: lock/range lock lâu, POS ghi phải chờ, timeout/deadlock và RAM tăng khi nhiều quản lý mở báo cáo. AsNoTracking không bỏ khóa SQL của Serializable. Mức ảnh hưởng phụ thuộc dữ liệu/index và cần đo.
- Cần làm: đo actual plan/Query Store/lock waits; thiết kế đọc snapshot nhất quán hoặc bảng tổng hợp/reporting riêng, đẩy aggregate/filter xuống SQL, giới hạn số báo cáo/export song song. Không sửa sang NOLOCK hoặc bỏ consistency chỉ để nhanh hơn.
- Nghiệm thu: vừa chạy báo cáo kỳ dài vừa bán/hoàn/nhập vẫn đạt latency, số liệu đối soát đúng.

### F07 — P2: Cấu hình upload Production không áp dụng cho toàn bộ media

- `appsettings.Production.json` đặt Storage:UploadRoot ngoài publish; StartupValidationService xác nhận thư mục đó.
- Tuy nhiên `DependencyInjection.cs:191` đăng ký IFileStorageService → LocalFileStorageService; service này resolve theo `_env.WebRootPath`, không đọc StorageOptions.
- TempUploadService.UploadAsync gọi service này với `uploads/_temp/...`; media vẫn có thể nằm trong thư mục publish.
- Tác động: deploy thay toàn thư mục có thể mất ảnh; backup riêng UploadRoot có thể bỏ sót media; thư mục temp có ExpireAtUtc nhưng cần xác nhận job dọn file thực sự chạy. Chưa kết luận mọi loại file hóa đơn dùng storage này.
- Cần làm: thống nhất root/persistence cho media, cơ chế phục vụ ảnh công khai có chủ đích, quota/dọn temp; nghiệm thu upload → deploy lại → ảnh còn và restore được.

### F08 — P2, có điều kiện: Proxy trust có thể mở rộng thành tin mọi nguồn

- `ForwardedHeadersExtensions.cs:27` clear cả KnownProxies/KnownNetworks rồi nạp config; StartupValidationService.ValidateProxy không chặn hai danh sách cùng rỗng hoặc CIDR bao trùm mọi IP.
- Config gốc có danh sách rỗng và enabled=true. **Production JSON hiện có 127.0.0.1/::1, vì vậy không kết luận Production mặc định đang trust mọi nguồn.** Rủi ro xảy ra khi config production thiếu/bị override hoặc dùng môi trường khác kế thừa config gốc.
- Tác động có điều kiện: client chạm được origin có thể giả forwarded IP/host/scheme, ảnh hưởng tenant routing, log và login limiter. Không đồng nghĩa tự vượt được cookie tenant validation.
- Cần làm: fail startup ngoài Development nếu enabled nhưng không có proxy cụ thể; allowlist đúng proxy thật; chặn truy cập origin trực tiếp; xác minh header Host tại proxy.
- Hành vi trust-all khi clear hai danh sách được Microsoft xác nhận: [Forwarded headers unknown proxies](https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/8.0/forwarded-headers-unknown-proxies).

### F09 — P2: Chưa ràng buộc chỉ một ngân hàng mặc định khi ghi đồng thời

- StoreBankAccountService.SetDefaultAsync/CreateAsync gọi ClearDefaultAsync rồi SaveChanges; repository đọc defaults và cập nhật entity.
- StoreBankAccountConfiguration có unique StoreId+AccountNumber, nhưng StoreId+IsDefault là index thường.
- Khi chưa có default, hai transaction đồng thời có thể cùng thấy danh sách rỗng rồi đặt hai tài khoản khác nhau làm default. GetDefaultActiveAsync dùng FirstOrDefaultAsync, không giải quyết được invariant bị phá.
- Cần làm: khóa theo store/transaction phù hợp và filtered unique index cho default hợp lệ, kèm xử lý dữ liệu lịch sử trước migration. Nghiệm thu hai request cạnh tranh: tối đa một default, phản hồi xung đột rõ ràng.

### F10 — P2: Chuyển máy hoặc tăng web instance cần thiết kế lại key/realtime/storage

- Data Protection có key path bền vững, nhưng Windows dùng DPAPI protectToLocalMachine (`DependencyInjection.cs:134`). Sao chép file key sang máy mới không đủ bảo đảm giải mã cookie/credential cũ; Linux không có nhánh bảo vệ key at rest tương đương trong đăng ký đã đọc.
- Program chỉ AddSignalR; chưa có backplane/managed SignalR; cache là MemoryCache. Nhiều instance cần phối hợp realtime, cache invalidation, key dùng chung và storage.
- Cần làm: diễn tập export/reprotect hoặc nhập lại credential có kế hoạch, backup key cùng DB, ACL chặt; nếu scale-out thì thiết kế shared key encryption phù hợp, realtime và affinity theo transport. **50 client không tự động bắt buộc nhiều web instance.**
- Tham chiếu: [SignalR hosting and scaling](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-8.0).

### F11 — P2: Test xử lý exception đang đỏ

- ExceptionSourceContractTests.Runtime_source_has_no_empty_or_comment_only_catch báo `AcbApiException.cs:35` (JsonException) và `AcbCallbackInbox.cs:96` (SemaphoreFullException).
- Hai catch có thể là fallback/coalescing có chủ đích; test fail chưa chứng minh mất giao dịch. Cần diễn đạt exception policy rõ và thống nhất contract test, không thêm log chứa body/token ngân hàng để làm test xanh.

## 4. Đánh giá tải 10–20–50 client

Một máy local vẫn có thể mô phỏng nhiều virtual user. Tuy nhiên chạy load generator + app + SQL cùng máy sẽ tranh CPU/RAM và không mô phỏng đầy đủ host, mạng Internet, TLS/proxy hoặc nhiều browser. “50 client mở trang” khác “50 client cùng thanh toán”.

| Mức | Đánh giá hiện tại | Tình huống quyết định |
|---|---|---|
| 10 | Chưa benchmark; có blocker đăng nhập chung IP nếu dồn vào một phút | Login burst, bán cùng SKU, một quản lý mở báo cáo |
| 20 | Chưa benchmark; chi phí query nền/polling bắt đầu cần đo rõ | Checkout song song, màn hình khách hàng, callback ACB, nhập hàng |
| 50 | Chưa có bằng chứng đáp ứng; phải đo host tương đương production | Báo cáo dài chạy cùng POS, lock/pool waits, hàng đợi callback, reconnect hàng loạt |

Chi phí nền đã thấy trong source:

- Mỗi request tenant hợp lệ thường đọc Store; có device cookie thì lookup terminal; request xác thực kiểm tra user/store trong DB; endpoint permission có thể phát sinh query quyền. Không coi đây là số query cố định cho mọi endpoint.
- `pos.customer-display.js:1231`: fallback polling 3 giây, backup 15 giây. Nếu 50 màn hình cùng fallback, riêng timer 3 giây phát khoảng 16,7 lần refresh/giây trước guard/dedup bên trong; backup và nghiệp vụ còn cộng thêm. Đây là tính toán timer, không phải RPS đo được.
- ACB inbox accept dùng lock theo store (key 0); worker batch 50, song song 4, wake timeout 5 giây. Cần theo dõi tuổi receipt chưa xử lý và retry, không chỉ tốc độ trả HTTP 200.
- Audit ghi bằng DbContext riêng sau SaveChanges, làm tăng lượt ghi/connection; không tự bật retry SQL toàn cục. SqlServerConfiguration hiện chủ động tắt retry vì transaction/audit chưa an toàn để replay toàn bộ.
- AuditLogRepository.SearchAsync chưa có trần PageSize ở repository: cần giới hạn tại biên request để tránh truy vấn/trả dữ liệu quá lớn.

### Kịch bản đo tải cần thực hiện

1. Tạo staging với bản publish Release, schema giống production, DB clone đã ẩn thông tin, credential ACB/Viettel sandbox hoặc mock. Không dùng account ngân hàng thật để load test.
2. Ghi CPU/RAM/OS, loại SQL/edition, IOPS, vị trí app–SQL, số SKU/orders/lines/audit rows và kết nối mạng. Chọn dữ liệu tương đương ít nhất một chu kỳ vận hành thực tế; không chỉ seed vài sản phẩm.
3. Dùng 10/20/50 user riêng với terminal phù hợp, thêm cửa hàng thứ hai để kiểm tra isolation. Login mỗi user có cookie và CSRF token riêng; không dùng một cookie admin cho toàn bộ tải. Warm login có pacing để đo nghiệp vụ, nhưng có bài riêng login burst cùng NAT để đánh giá F05.
4. Mỗi mức: warm-up 5 phút, steady 15 phút, spike 5 phút; sau khi sửa lỗi chạy soak 60 phút ở mức mục tiêu. Load generator nên ở máy khác app/SQL.
5. Mix xuất phát để hiệu chỉnh theo thực tế: 65% tra cứu/giỏ hàng, 20% checkout/giữ đơn/hoàn, 10% màn hình phụ và trạng thái, 5% báo cáo/nhập hàng. Báo cáo 366 ngày và export cần bài riêng; không ép mọi VU gọi mọi endpoint liên tục không có thời gian người dùng thao tác.
6. Bài correctness độc lập: 50 request cùng SKU tồn ít; double-submit cùng đơn; callback trùng và đảo thứ tự; timeout ngay lúc commit rồi retry; restart worker sau ACK; bán/hoàn/nhập và báo cáo chạy đồng thời. Kiểm tra DB sau bài: không double payment/stock movement, tổng tiền/tồn đúng, không dữ liệu chéo store.
7. Thu p50/p95/p99 theo endpoint, throughput, lỗi 5xx/429/timeout, CPU/RAM/GC/thread pool, SQL CPU/IO/lock waits/deadlocks/connection pool, queue age ACB và SignalR reconnect. Lưu raw report cùng phiên bản app và kích thước dữ liệu.

Ngưỡng nghiệm thu đề xuất, **chưa phải kết quả đã đạt**: p95 lookup/giỏ dưới 500 ms; checkout nội bộ dưới 2 giây; báo cáo thường dưới 5 giây, báo cáo nặng chuyển background nếu vượt ngân sách; 5xx/timeouts dưới 0,1%; sai lệch tiền/tồn/tenant bằng 0; không queue age hoặc RAM tăng liên tục qua soak. Tách latency ngân hàng khỏi phần xử lý nội bộ. Hiệu chỉnh ngưỡng theo yêu cầu kinh doanh và mạng thực tế.

Không đưa cấu hình mua host cụ thể khi chưa có profile tải/dữ liệu. Bắt đầu đo một instance web trên staging, rồi quyết định tăng tài nguyên, tối ưu SQL hay tách reporting dựa trên bottleneck đo được.

## 5. Điều kiện local → host

- Chốt artifact Release và toàn bộ migration; chạy Migrator một lần có kiểm soát trước web. Tài khoản web chỉ có quyền DB tối thiểu, tách tài khoản migration; DB không mở Internet.
- Production environment đúng; domain/subdomain/wildcard DNS, TLS, proxy trusted IP, forwarded host/proto, WebSocket và timeout phải kiểm thử. Production hiện có đường dẫn `C:\GaoAppData\...`; đổi sang Linux phải thay cấu hình tương ứng.
- Cung cấp connection string/credential bằng cấu hình secret của host; kiểm tra SQL TLS/chứng chỉ, không mang cấu hình LocalDB/TrustServerCertificate của dev sang host theo thói quen. Không đưa key/password vào log hoặc publish artifact.
- Backup gồm DB, key giải mã, invoice/media và cấu hình cần phục hồi. Restore vào máy riêng và kiểm tra mở credential, tải file, đăng nhập, đối soát tồn/tiền; chỉ có file backup chưa đủ.
- Theo dõi health, disk free, dung lượng temp/log, SQL waits, callback backlog; có cảnh báo và người nhận chịu trách nhiệm. Giới hạn truy cập/rate tại proxy cho readiness đụng DB và endpoint tốn tài nguyên.
- Chạy UAT vai trò cashier/kho/manager/admin trên ít nhất hai store; gọi trực tiếp endpoint để kiểm tra 401/403/cross-store, không dựa vào menu ẩn.

## 6. Kết quả kiểm tra thực tế

| Kiểm tra | Kết quả |
|---|---|
| `dotnet build GaoApp.sln --no-restore -v minimal` | Thành công, 0 errors, 10 warnings; đây là Debug build, chưa phải nghiệm thu publish Release |
| Nhóm Security, Tenant, Data, Observability, AcbCallbackTests, AcbPaymentTests | 295 tests: 294 passed, 1 failed, 0 skipped |
| InventoryMovementSqlServerConcurrencyTests trên LocalDB ngoài sandbox | 5 passed, 0 failed, 0 skipped, khoảng 19 giây |
| NuGet vulnerable scan gồm transitive packages, toàn solution | Không thấy vulnerable package từ nguồn advisory đã truy vấn; không chứng minh source hoặc JS/CDN không có lỗi |
| Tải HTTP 10/20/50, browser E2E, host thật, pentest, backup restore | Chưa thực hiện |

Lần chạy đầu gộp SQL trong sandbox gặp lỗi LocalDB “Cannot create an automatic instance”; đã dừng, chạy lại nhóm không cần SQL và nhóm SQL riêng. Không coi lỗi kết nối môi trường đó là defect inventory.

Bằng chứng lưu trong repository:

- `Logs/audit-build-20260909.log`
- `Logs/audit-security-20260909.log`
- `Logs/audit-sql-concurrency-20260909.log`
- `Logs/audit-packages-20260909.log`
- `TestResults/audit-20260909/audit-security-20260909.trx`
- `TestResults/audit-20260909/audit-sql-concurrency-20260909.trx`

## 7. Thứ tự công việc tiếp theo

1. **Khóa rủi ro truy cập dữ liệu:** F01–F04, regression test HTTP theo role/store và cookie cũ. Ưu tiên hash audit, tài khoản nhận tiền, điều chỉnh tồn trực tiếp.
2. **Chuẩn bị triển khai ổn định:** storage/backup/key, proxy và login limiter; sửa test đỏ; rà default bank concurrency và endpoint legacy. Chốt release/CI.
3. **Đo và tối ưu tải:** staging, report/POS đồng thời, 10 → 20 → 50; xử lý nút thắt dựa trên số đo rồi đo lại đúng trường hợp thay đổi.
4. **Nghiệm thu production:** UAT nhiều role/store, restore và callback Internet, rollout có giám sát và kế hoạch quay lại bản trước tương thích schema.

Phạm vi chưa xác nhận: toàn bộ SQL thủ công/IgnoreQueryFilters, toàn bộ XSS/CSRF/SSRF, secret trong lịch sử Git, JS dependencies, tất cả migration trên bản dữ liệu thật, hệ điều hành/firewall/SQL host và dịch vụ ngoài. Không có cơ sở để cam kết “không thể bị hack” hoặc “50 máy chắc chắn không nghẽn” từ khảo sát local này.
