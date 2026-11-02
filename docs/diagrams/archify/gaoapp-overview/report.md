# GaoApp — Báo cáo G0 và G1

Thời điểm: lúc 18:07:05 GMT+7 Thứ Bảy, 24 tháng 10, 2026. Task Level A: discovery/documentation. Chưa thực hiện G2. PASS dưới đây là kiểm tra artifact/phạm vi, không phải TASK PASS, CI PASS hay nghiệm thu nghiệp vụ.

## G0 — Danh tính, quy tắc và phạm vi

- Source/Git root: E:\GaoApp\GAOAPP-SA-20260819-01.
- Branch: codex/delivery-d04-d12-20261006.
- Commit đầy đủ: 4495ffb7ba7cbd0a5d58507a1bf702398d0a8465.
- Origin đã loại userinfo: https://github.com/ThangLoi28291/Datacode.git.
- Snapshot Git chi tiết trước: 790 mục tracked thay đổi, 8184 file untracked ngoài artifact; staged diff rỗng.
- Lần kiểm tra đầu trước snapshot chi tiết: 790 tracked, 8.183 untracked. Snapshot chi tiết sau đó có thêm một file untracked ngoài tác vụ; không gộp hai thời điểm thành một.
- Sau tác vụ, ngoài thư mục artifact: 790 tracked, 8187 untracked.
- Skill thực đọc: C:/Users/wind/.agents/skills/archify/SKILL.md; package 3.2.0, CLI khả dụng; Node v24.18.0. Không cài lại/cập nhật skill hoặc sửa cấu hình dự án.
- Không tìm thấy tên BMAD/planning trong source hoạt động khi tìm file/thư mục; không coi Logs/publish là source chính. Các tài liệu planning hiện có được đọc theo phạm vi ở docs/development.
- Tài liệu thực đọc theo phần liên quan: AGENTS.md; docs/development/GAOAPP-SOURCE-MAP.md; docs/development/GAOAPP-DEVELOPMENT-WORKFLOW.md; docs/development/DECISION-LOG.md; docs/development/DELIVERY-ROADMAP-20261006.md. Đã tuân thủ quy tắc menu, allowlist và discovery Level A. Không áp dụng các cổng triển khai/migration ngoài task.
- Search targets: solution/csproj/entry point; đăng ký DI; controller → service → repository; transaction; DbContext/provider SQL; menu; worker và HTTP client. Chỉ mở các lát cắt liên quan, không đọc tuần tự mọi file.

## Kiến trúc thực tế

Ứng dụng chính là ASP.NET Core MVC/Razor và API trong GaoApp.Web. JavaScript trong trình duyệt gọi endpoint cùng Web host. Web đăng ký Application và Infrastructure; Application có DTO, hợp đồng và service nghiệp vụ; Infrastructure hiện thực repository, EF Core, adapter, transaction và một số service giao hàng. Domain chứa entity/enum. Đây là backend chính phân lớp với các tiến trình phụ riêng; source không chứng minh kiến trúc microservice cho từng module nghiệp vụ.

Solution hiện tại có 9 project: Domain, Application, Infrastructure, Web, Migrator, Tests, LabelPrintServer, AutoInvoiceWorker, LabelPrintClient. Các project hiện tại dùng net10.0. Source map cũ nêu 6 project/.NET 8 nên chỉ dùng định hướng và được đối chiếu lại. HEAD còn net8.0; không lấy HEAD thay cho framework của WT.

Chiều compile dependency: Application → Domain; Infrastructure → Application + Domain; Web/AutoInvoiceWorker/Migrator → Application + Infrastructure; LabelPrintServer và LabelPrintClient → Infrastructure. Tests tham chiếu các lớp ứng dụng và LabelPrintClient; Browser test/tool project có csproj riêng ngoài solution chính. Module Map gom các host có cùng dependency để dễ đọc; không khẳng định các host là cùng process.

DbContext chính: AppDbContext; AuditLogDbContext cũng được đăng ký với cùng khóa cấu hình provider SQL. UseGaoAppSqlServer gọi UseSqlServer. Chưa kết nối SQL, nên các tên DbSet/cấu hình không phải bằng chứng về schema database đang chạy. Tenant/soft delete và quyền được giữ ở các lớp liên quan; menu không cấp quyền thay controller/service.

Tích hợp đã lần theo callsite: ACB HTTP từ Web; ChatbotLocal HTTP client trong WT; Viettel S-Invoice qua AutoInvoiceService → IssueSafelyAsync → ViettelInvoiceIssueService.IssueAsync → IViettelInvoiceIssueClient.IssueInvoiceAsync. Worker quét dữ liệu SQL theo chu kỳ và điều kiện cấu hình. LabelPrintServer poll ProductLabelJob rồi gọi Windows printer transport. LabelPrintClient có route /print trong WT. Chưa gọi bất kỳ dịch vụ bên ngoài nào.

Chức năng sequence được chọn: POST /admin/pos/{orderId}/finalize, nhánh có X-POS-Operation-Id. PosOperationFilter mở transaction, khóa và tìm receipt; replay khớp trả response cũ. POSService kiểm tra đơn/tiền/ca/kho, consume reservation nếu có và dùng allocation theo pháp nhân hoặc InventoryMovementService. Đơn/ca/kho/audit nghiệp vụ được lưu; filter lưu receipt và commit ngoài cùng. Deferred callback sinh hóa đơn chạy sau commit. Controller gọi SignalR trước khi filter commit trong nhánh này; không gán nhãn mọi realtime event là post-commit. Sequence không mô tả bước thu tiền trước finalize hoặc mọi nhánh công nợ/offline.

## G1 — Sơ đồ và validation

| Sơ đồ | HTML | Kết quả | Receipt hiện hành |
|---|---|---|---|
| Architecture | [Kiến trúc tổng thể](architecture.html) | PASS | [finalize](review-2/architecture/architecture.finalize-summary.json) · [browser](review-2/architecture/architecture.browser-check.json) · [ảnh](visual-check-review-2/architecture/architecture.visual-check.html) |
| Module Map | [Module và dependency](modules.html) | PASS | [finalize](review-2/modules/modules.finalize-summary.json) · [browser](review-2/modules/modules.browser-check.json) · [ảnh](visual-check-review-2/modules/modules.visual-check.html) |
| Data Flow | [Dữ liệu UI → SQL](dataflow.html) | PASS | [finalize](dataflow.finalize-summary.json) · [browser](dataflow.browser-check.json) · [ảnh](visual-check/dataflow/dataflow.visual-check.html) |
| Sequence | [Sequence chốt đơn POS](sequence-pos-finalize.html) | PASS | [finalize](review-2/sequence-pos-finalize/sequence-pos-finalize.finalize-summary.json) · [browser](review-2/sequence-pos-finalize/sequence-pos-finalize.browser-check.json) · [ảnh](visual-check-review-2/sequence-pos-finalize/sequence-pos-finalize.visual-check.html) |

Từng sơ đồ đã PASS validate, deliver, strict check và browser-check. Các receipts hiện hành ghim SHA-256/byte count của HTML và candidate. Không có diagnostic ở final receipt. Browser-check đo các viewport 1440×900, 1600×1000, 1920×1080, 2048×1320 và trạng thái light/dark/READ/Still theo hợp đồng skill. Visual-check bổ sung ảnh light/dark 1440×900 và 2048×1320, containment/readability/viewer chrome/theme/captures PASS. Đã mở ảnh light hiện hành của cả bốn sơ đồ; Sequence dùng cuộn dọc, ảnh viewport chưa chứng minh đã xem toàn bộ phần dưới. Receipt visual-check giữ nguyên trạng thái pending của công cụ; đánh giá ảnh của tác giả được ghi riêng trong verification.json, không sửa receipt.

Bản Sequence đầu có hai lỗi note-overlap, đã giãn các bước sau trong cùng timeline và finalize lại đạt. Lần đầu sandbox chặn hard link của Archify; đã chạy cùng lệnh với quyền cần thiết để xuất artifact an toàn, không sửa hoặc cài skill. Các lần bổ sung citation tạo receipts mới ở review-2 để giữ evidence gắn đúng bytes. Receipts ở thư mục gốc của Architecture/Modules/Sequence và ảnh visual-check lần đầu là lịch sử; Dataflow không đổi và dùng receipts gốc.

Nội dung/chú thích tiếng Việt. Viewer cố định dùng tiếng Anh và được công bố rõ. HTML có zoom, focus/quan hệ, Route/Lens, đổi theme và xuất hình theo runtime Archify. Sơ đồ thể hiện quan hệ xác minh bằng đọc source; phần chưa xác minh được ghi trong card/báo cáo, không biến thành mũi tên giả định.

## Git diff/status và bảo toàn source

- Đối chiếu ngoài allowlist: CHANGED_OUTSIDE_ARTIFACTS. Không xác nhận toàn working tree giữ nguyên.
- Branch/commit giữ nguyên: true. Status giữ nguyên ngoài artifact: false.
- Diff stat trước/sau giống nhau: false; danh sách diff giống nhau: true.
- Diff nền: 782 files changed, 81656 insertions(+), 7263 deletions(-).
- Metadata 11483 file nền được đối chiếu theo kích thước/mtime (sai số làm tròn JSON tối đa 128 tick = 12,8 microsecond); 24 thay đổi và 2 file ngoài allowlist mới.
- Các thay đổi này ở vùng CSKH/Zalo/test/log, phát sinh trong thời gian khảo sát; không có thao tác ghi những file đó trong tác vụ Archify. Git status không xác định tác giả. Xem [outside-changes.json](outside-changes.json) để tra cứu thay đổi status và metadata. Ba mốc kiểm tra đầu/snapshot trước/snapshot sau được giữ riêng.
- SHA-256 của 33 file trích dẫn giữ nguyên: true.
- [Status trước](git-status-before.txt), [Status sau](git-status-after.txt), [Diff trước](git-diff-before.txt), [Diff sau](git-diff-after.txt), [diff names trước](git-diff-names-before.txt), [diff names sau](git-diff-names-after.txt).
- Chỉ ghi artifact trong docs/diagrams/archify/gaoapp-overview/. Không sửa app/DB/migration/solution/project/AGENTS/BMAD. Không tạo menu/seeder, refactor, commit/push hoặc triển khai.
- Build/test ứng dụng: NOT RUN — documentation-only. Không chạy Web/Worker/Migrator, database update hoặc thao tác dữ liệu.

## Bằng chứng source và phiên bản

Native sources được kiểm tra tại immutable HEAD. Đã đọc và đối chiếu riêng các callsite tương ứng trong WT; không giả vờ rằng source dirty là bytes ở HEAD. Bảng sau ghi dòng WT chính xác; cột HEAD chỉ là tọa độ baseline. Hai file ChatbotLocalClient và LabelClientHost không có ở HEAD, được ghi WT-only. evidence.json chứa hash của file hiện tại cùng tọa độ hai phía; không chứa giá trị secrets hoặc dữ liệu khách.

| Mã | File:dòng WT | Dòng HEAD |
|---|---|---|
| startup | GaoApp.Web/Program.cs:194–204 | 159–169 |
| ui | GaoApp.Web/wwwroot/Admin/js/pos/pos.payment.js:1873–1883 | 1856–1866 |
| controller | GaoApp.Web/Areas/Admin/Controllers/POSController.cs:406–421 | 405–420 |
| filter | GaoApp.Web/Services/Offline/PosOperationFilter.cs:22–83 | 22–83 |
| filterCommit | GaoApp.Web/Services/Offline/PosOperationFilter.cs:128–137 | 128–137 |
| pos | GaoApp.Application/Services/Orders/POSService.cs:2181–2269 | 2181–2269 |
| commit | GaoApp.Application/Services/Orders/POSService.cs:2333–2345 | 2325–2337 |
| invoiceCallback | GaoApp.Application/Services/Orders/POSService.cs:209–235 | 209–235 |
| inventoryCaller | GaoApp.Application/Services/Orders/POSService.cs:1026–1052 | 1026–1052 |
| movement | GaoApp.Application/Services/Inventory/InventoryMovementService.cs:143–173 | 143–173 |
| movementWrite | GaoApp.Application/Services/Inventory/InventoryMovementService.cs:288–322 | 288–322 |
| repository | GaoApp.Infrastructure/Repositories/Orders/OrderRepository.cs:10–53 | 10–53 |
| uow | GaoApp.Infrastructure/Data/AppUnitOfWork.cs:33–59 | 33–59 |
| context | GaoApp.Infrastructure/Data/AppDbContext.cs:14–54 | 14–54 |
| tenantWrite | GaoApp.Infrastructure/Data/AppDbContext.cs:342–363 | 330–351 |
| sql | GaoApp.Infrastructure/Data/SqlServerConfiguration.cs:18–28 | 18–28 |
| diSql | GaoApp.Infrastructure/DependencyInjection.cs:469–497 | 466–494 |
| realtime | GaoApp.Web/Hubs/PosRealtimeNotifier.cs:76–102 | 76–102 |
| worker | GaoApp.AutoInvoiceWorker/AutoInvoiceWorkerHostedService.cs:58–75 | 58–75 |
| autoIssue | GaoApp.Application/Services/Invoices/AutoInvoiceService.cs:1318–1330 | 1321–1333 |
| autoCycle | GaoApp.Application/Services/Invoices/AutoInvoiceService.cs:238–268 | 237–267 |
| autoDispatch | GaoApp.Application/Services/Invoices/AutoInvoiceService.cs:892–912 | 888–908 |
| issueBridge | GaoApp.Application/Services/Invoices/ViettelInvoiceIssueService.cs:211–223 | 211–223 |
| viettel | GaoApp.Infrastructure/Services/Invoices/ViettelInvoiceIssueClient.cs:90–119 | 90–119 |
| acb | GaoApp.Web/Services/Acb/AcbProtocol.cs:114–129 | 114–129 |
| chatbot | GaoApp.Web/Services/Assistant/ChatbotLocalClient.cs:77–87 | Chỉ WT |
| invoice | GaoApp.Application/Services/Invoices/InvoiceCommandService.cs:77–113 | 77–113 |
| invoiceWrite | GaoApp.Application/Services/Invoices/InvoiceCommandService.cs:183–187 | 183–187 |
| menu | GaoApp.Application/Services/AdminMenus/AdminMenuService.cs:34–57 | 33–56 |
| menuView | GaoApp.Web/Areas/Admin/Views/Shared/Sections/Menu/_VerticalMenu.cshtml:21–37 | 25–41 |
| menuRepo | GaoApp.Infrastructure/Repositories/AdminMenus/AdminMenuRepository.cs:19–30 | 19–30 |
| print | GaoApp.LabelPrintServer/Program.cs:24–50 | 24–50 |
| printDispatch | GaoApp.Infrastructure/Printing/LabelPrintDispatcher.cs:9–40 | 9–40 |
| client | GaoApp.LabelPrintClient/LabelClientHost.cs:78–87 | Chỉ WT |
| webProject | GaoApp.Web/GaoApp.Web.csproj:41–43 | 38–40 |
| workerProject | GaoApp.AutoInvoiceWorker/GaoApp.AutoInvoiceWorker.csproj:12–14 | 11–13 |
| migratorProject | GaoApp.Migrator/GaoApp.Migrator.csproj:38–40 | 38–40 |
| appProject | GaoApp.Application/GaoApp.Application.csproj:18–19 | 18–19 |
| infraProject | GaoApp.Infrastructure/GaoApp.Infrastructure.csproj:27–29 | 24–26 |
| printProject | GaoApp.LabelPrintServer/GaoApp.LabelPrintServer.csproj:10–11 | 10–11 |
| domainProject | GaoApp.Domain/GaoApp.Domain.csproj:1–9 | 1–9 |
| reports | GaoApp.Application/Services/Reports/ManagementReportService.cs:12–32 | 12–32 |

Các file đọc thêm theo phạm vi: GaoApp.sln; Directory.Build.props; csproj của các project; GaoApp.Migrator/Program.cs; GaoApp.AutoInvoiceWorker/Program.cs; GaoApp.LabelPrintServer/Program.cs; GaoApp.LabelPrintClient/Program.cs; GaoApp.Web/Services/Delivery/DeliveryPosService.cs; GaoApp.Infrastructure/Services/Delivery/DeliveryFoundationService.cs, DeliveryPickingService.cs, DeliveryInventoryService.cs, DeliveryTripService.cs và DeliveryTripService.Handover.cs; GaoApp.Application/Services/Inventory/StockDocumentService.cs; GaoApp.Application/Services/Purchases/PurchaseOrderService.cs; GaoApp.Infrastructure/Data/UnitOfWork.cs, AppTransaction.cs; các DI và file được liệt kê trong evidence.json. Các phần source được nêu trong bảng quan hệ của sơ đồ liên kết caller và callee, không chỉ suy luận từ tên file.

## Giới hạn chưa xác minh

- Chưa kiểm chứng runtime, topology IIS, số instance, SQL schema đang chạy hoặc khả dụng tích hợp.
- Native sources được ghim HEAD; WT có framework net10.0 và file chưa commit. evidence.json lưu riêng tọa độ WT và SHA-256.
- Module Map nhóm Web/Worker/Migrator theo dependency chung; LabelPrintClient và ChatbotLocal chỉ có bằng chứng WT.
- Chưa mô tả chi tiết tất cả endpoint, bảng, migration, Zalo/CSKH, kiosk, XML và toàn bộ nhánh offline/delivery.
- Nội dung tiếng Việt; điều khiển cố định của Viewer dùng tiếng Anh.
- Chưa xác minh queue durable claim/retry, concurrency hoặc tính đúng tiền/kho bằng thực thi; mô tả chỉ theo source đã đọc.
- Các module/endpoint ngoài lát cắt chưa được dựng đồ thị đầy đủ. Không có ERD đầy đủ hoặc hứa hẹn về mọi bảng SQL.
- G2 chưa thực hiện.

## File đã tạo

Danh sách đầy đủ: [files-created.txt](files-created.txt). Gồm index.html, bốn HTML sơ đồ, bốn candidate JSON, evidence.json, repository.json, report.md, verification.json, hai script tạo artifact, snapshots Git/metadata, receipts và ảnh validation. Không có file tác vụ bên ngoài thư mục này.
