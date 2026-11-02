# GaoApp UX/UI Large-scale Pilot V2 — A0 và tổng kết A0–A6

Ngày khảo sát: 02/11/2026, Asia/Bangkok. Trạng thái bàn giao: **BLOCKED ở runtime/baseline; STOP — CHỜ DUYỆT PILOT P1**. Đây là bàn giao discovery và kế hoạch, không phải TASK PASS, independent review, CI hay nghiệm thu triển khai.

## Kết luận hiện tại

**SOURCE VERIFIED:** repo `E:\GaoApp\GAOAPP-SA-20260819-01`, branch `codex/p4-p8-20261030`, HEAD `4495ffb7ba7cbd0a5d58507a1bf702398d0a8465`. Đầu lượt có 2.194 mục Git: 978 modified, 67 deleted, 1.149 untracked, không có staged. Không đổi branch/HEAD, commit, push hoặc deploy.

**MEASURED:** đã build Web thật từ working tree, Debug/net10.0, đầu ra riêng: 0 warning, 0 error, exit 0. Ba tệp kiểm thử Node hiện có chạy 33 test, 33 pass, 0 fail/skip. Các test dùng VM/mock trong bộ nhớ; không phải kiểm thử nghiệp vụ Web/SQL. Xem [A1](01-LOCAL-RUN-SAFETY-BASELINE.md).

**NOT TESTED:** chưa khởi động ứng dụng, đăng nhập, mở browser, kết nối SQL hoặc benchmark UX. DB test riêng chưa được chứng minh; Development bật migration và Web có worker nền. Công cụ UiPreview có sẵn build lỗi, không được sửa để vượt Gate. Không có ảnh BEFORE/AFTER trong lượt này.

## Ma trận Gate

| Gate | Trạng thái | Bằng chứng / phần còn thiếu |
|---|---|---|
| A0 | PASS cho discovery; BLOCKED thay đổi | Root/HEAD/Git, authority, project, module và startup risk đã xác minh. Scope tương lai có file dirty. |
| A1 | BLOCKED; BUILD PASS | [Log build riêng](evidence/build-web-isolated.txt); RUN BLOCKED do DB/integration chưa an toàn; preview compile FAIL. |
| A2 | BLOCKED xác minh DB/dung lượng; kế hoạch đã soạn | [Schema, tier và ước lượng](02-LARGE-DATA-TEST-PLAN.md); chưa có DB test, edition/RAM và số bản ghi thật. Không ghi PASS A2 PLAN hoặc EXECUTED khi thiếu điều kiện. |
| A3 | BLOCKED | [Audit source 11 nhóm](03-MULTI-MODULE-UXUI-AUDIT.md); chưa có role test/runtime/ảnh BEFORE. |
| A4 | BLOCKED | [Baseline và protocol](04-PERFORMANCE-BEFORE.md); 0 mẫu runtime, không có p50/p95. |
| A5 | BLOCKED — SKILL CHƯA CÀI | [Inventory và 3 phương án](05-SKILLS-DESIGN-SYSTEM-OPTIONS.md); 0/3 skill UX/UI được chạy, chưa đạt yêu cầu 2 output skill. |
| A6 | BLOCKED điều kiện baseline; kế hoạch sẵn để duyệt | [P1/P2, file, hồi quy, rollback](06-P1-P2-APPROVAL-PLAN.md); thiếu BEFORE/role test. Đã áp dụng STOP, chưa sửa UI. |
| A7 | SKIPPED | Chưa có phê duyệt P1; P2 cần duyệt riêng sau P1. |
| A8 | SKIPPED | Chưa có implementation/AFTER. |

## Authority và phạm vi

Đã đọc toàn bộ **341 dòng** `CODEX_GAOAPP_UXUI_LARGE_SCALE_PILOT_V2.md`, `AGENTS.md`, phần workflow liên quan Level A, source discovery, scope, Git, evidence và handoff. Đã đối chiếu source map/decision log/backlog có trọng tâm, không nhận là đã đọc toàn repo.

- `AGENTS.md:3–6`: menu chỉ đến từ `AdminMenuItems` theo Store/quyền/trạng thái/thứ tự; không thêm menu, seed hay migration. Việc khai báo/kiểm tra quyền độc lập với menu.
- `docs/development/GAOAPP-DEVELOPMENT-WORKFLOW.md`, mục 3/5/6/7/11/14: discovery Level A, bảo toàn dirty work, allowlist, bằng chứng thật. Không có implementation hoặc review độc lập trong lượt này.
- `docs/development/GAOAPP-SOURCE-MAP.md`, mục 1/2/7/8/11/15/16: dùng làm bản đồ. Baseline ghi trong tài liệu cũ; con số sáu project/.NET 8 trong đó không dùng để mô tả source hiện tại. `.sln` hiện liệt kê **10 project**; Web/Application/Domain/Infrastructure/Tests hiện là net10.0.
- `docs/development/DECISION-LOG.md`: GOV-001/002, LEGAL-001–004, POS-ORDERS-PAGE-20260910/A/B/C và các checkpoint Delivery đã được đọc/tra cứu. Quyết định giữ Store/LegalEntity và owner nghiệp vụ tiếp tục khóa.
- `docs/development/BACKLOG-FINDINGS.md`: đã đọc quy ước phân loại, tiêu đề findings và phần cuối về promotion/inventory. Không mở rộng pilot sang sửa các finding nghiệp vụ.
- `docs/development/MOBILE-UI-MOBILE-V2-20261025.md`: navy/lam được chấp thuận; palette xanh lá ở UI-01 là bản cũ. `MOBILE-UI-11-COMPLETE-20261026.md`, `MOBILE-UI-20-COMPLETE-20261028.md`, `INVOICE-PRODUCT-PICKER-COMPLETE-20261029.md` được đọc để tránh đề xuất làm lại phần đã có. Số đo/test lịch sử không được đưa vào baseline mới.

Không thấy mâu thuẫn quyết định áp dụng cho discovery này. Các mô tả runtime/project cũ được ghi là lịch sử, không phải lý do sửa cấu hình. Chưa đưa quyết định dài hạn mới vào authority hoặc sửa backlog, vì contract chỉ cho phép tạo báo cáo riêng.

## Source/project thực tế

| Thành phần | Evidence đã đọc | Vai trò |
|---|---|---|
| Solution | `GaoApp.sln`, danh sách Project | Domain, Application, Infrastructure, Web, Migrator, Tests, LabelPrintServer, AutoInvoiceWorker, LabelPrintClient, Storefront.Web |
| Runtime/build | `global.json`; `Directory.Build.props` | SDK 10.0.402/latestPatch; RuntimeFrameworkVersion 10.0.13/LatestPatch. Máy chọn SDK 10.0.403, có runtime 10.0.14. Không nâng cấp. |
| Startup Web | `GaoApp.Web/GaoApp.Web.csproj`; `Program.cs` | MVC/Razor, Bootstrap/vendor/gds, SQL Server EF; middleware Store/terminal/auth, workers/integration. |
| Persistence | `GaoApp.Infrastructure/DependencyInjection.cs:497–529` | DefaultConnection dùng cho AppDbContext và AuditLogDbContext. Không mở kết nối. |
| Tool có sẵn | `tools/GaoApp.UiPreview/{README.md,Program.cs,GaoApp.UiPreview.csproj}` | Host loopback chỉ render foundation, không thay thế app thật. Build hiện lỗi CS0246. |
| Tests | `GaoApp.Tests/GaoApp.Tests.csproj`; ba Node test; hai application fixture (trích đoạn) | xUnit + SQL/HTTP fixtures và Node; full fixture có migration/seed, không tự chạy. |

Các nhóm chức năng có route thật: đăng nhập/terminal, POS, khuyến mãi, sản phẩm, tra cứu tồn kho, đơn mua, đơn POS, hóa đơn điện tử, khách hàng, báo cáo và quản trị/menu: tổng **11 nhóm** trong [A3](03-MULTI-MODULE-UXUI-AUDIT.md), đăng nhập/terminal cùng một nhóm. Ngoài ra có receipt/warehouse receiving và storefront trong source; chưa nhận là đã audit runtime các nhóm này.

## Nguồn thực sự đã mở/đọc có trọng tâm

Các đường dẫn sau là bằng chứng đọc, không phải allowlist sửa. Nội dung lớn được đọc theo method/đoạn hoặc `rg` có mục tiêu; không nhận là đã đọc đầy đủ mọi dòng.

- Governance/tài liệu: các đường dẫn ở mục Authority; `docs/development/GAOAPP-DEVELOPMENT-WORKFLOW.md`; `docs/development/MOBILE-UI-01-FOUNDATION-20261025.md` (trích đoạn).
- Build/an toàn: bốn csproj Web/Domain/Application/Infrastructure và Tests; `GaoApp.sln`; `global.json`; `Directory.Build.props`; `eng/deployment/{PublishSafety.targets,Assert-ChannelCredentialPublish.ps1}`; `GaoApp.Web/Program.cs`; `GaoApp.Infrastructure/DependencyInjection.cs`; `GaoApp.Web/Configuration/DevelopmentDatabaseMigrationExtensions.cs` (search); launchSettings/appsettings chỉ lấy metadata/boolean đã che, không xuất secret. Chỉ kiểm tra sự tồn tại user-secrets.
- Workers đã mở: `GaoApp.Web/Services/Acb/StorefrontInvoiceWorker.cs`, `Services/Delivery/DeliveryOutboxWorker.cs`, `Services/Media/MediaCleanupWorker.cs`; search `Services/InvoiceNotifications` và `Configuration` để xác định đăng ký/gate. Không nhận là đã xác minh tất cả transport an toàn.
- Controllers/Views/JS/CSS: các path và đoạn/method ghi trong [A3](03-MULTI-MODULE-UXUI-AUDIT.md). Product Index/Search và repository paging, POS entry/search/scan/cart là ưu tiên đọc sâu; các module khác là mapping/audit source giới hạn.
- Schema: `GaoApp.Domain/Entities/{Product,ProductVariant,ProductUnitConversion,ProductVariantUnitBarcode,Order,OrderLine,InvoiceHead,InvoiceDetail,InventoryBalance}.cs`; configurations tương ứng, ưu tiên FK/index/precision; `GaoApp.Application/Services/Products/ProductService.cs`, `GaoApp.Infrastructure/Repositories/Products/ProductRepository.cs`; Inventory/PurchaseOrder read-service/repository theo method được ghi A3.
- Test source: `GaoApp.Tests/Ui/{pos-barcode-races,pos-action-locks,pos-checkout-feedback}.test.cjs`; `pos-order-product-search.test.cjs` chỉ đọc, không chạy vì ghi artifact lịch sử và dùng browser giả lập; `GaoApp.Tests/Security/{FullApplicationFixture,Ui20CurrentModelApplicationFixture}.cs` trích đoạn startup/seed, không thực thi.
- Skill/config: `openai-docs/SKILL.md` và `computer-use/SKILL.md` trong catalog đã đọc; computer-use chỉ kiểm kê, không kích hoạt phiên UI. Kiểm tra metadata thư mục skill và tên section liên quan trong Codex config, không sửa. Nguồn upstream chính thức liệt kê tại A5 đã mở bằng web read-only.

## Changes và rollback của chính lượt này

Chỉ tạo bảy báo cáo `00`–`06` trong thư mục này và evidence của lệnh thực tế. Không tạo `07/08`, ảnh hoặc báo cáo AFTER giả. Generated outputs của build/restore nằm ở bin/obj và `.artifacts/uxui-large-scale-pilot-v2-20261102/web`; không có package bên thứ ba mới.

[Manifest 4.427 đường dẫn source trước lượt build](evidence/source-hashes-before.txt), [Git đầu lượt](evidence/git-before.txt), và [scope verification cuối lượt](evidence/scope-verification.txt) giúp phân biệt thay đổi có sẵn với pilot. **MEASURED:** manifest trước/sau đều 4.427 mục, delta **0**; branch/HEAD không đổi. Git cuối có 2.195 mục; bỏ đúng thư mục report mới `docs/uxui/` thì còn đúng 2.194 mục với cùng số modified/deleted/untracked đầu lượt. Đây là kiểm tra content trong manifest và số mục Git, không phải fingerprint toàn bộ filesystem.

Tổng artifact bàn giao: **7 Markdown +13 evidence =20 file**; liên kết nội bộ không hỏng, không có trailing whitespace trong Markdown mới hoặc match mẫu giá trị credential được quét. `git diff --check` scope trả0; vì report còn untracked nên đã kiểm tra whitespace trực tiếp riêng, không nhận diff rỗng là đã kiểm tra nội dung untracked.

Không có source patch để rollback. Nếu muốn bỏ báo cáo, chỉ loại bỏ đúng các artifact được tạo trong thư mục pilot; giữ nguyên mọi file có sẵn, không reset/clean repository. Build outputs riêng có thể bỏ sau khi xác nhận không dùng; thư mục bin/obj dùng chung không được xóa hàng loạt.

Yêu cầu duyệt kế tiếp duy nhất ở cuối [A6](06-P1-P2-APPROVAL-PLAN.md). **STOP — CHỜ DUYỆT PILOT P1.**
