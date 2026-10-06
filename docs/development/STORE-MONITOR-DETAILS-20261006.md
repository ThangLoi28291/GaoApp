# Thông báo nghiệp vụ cụ thể trên màn tổng — 06/10/2026

## Contract revision 4 — READY

SM-DETAILS-04, Level B, implementation. User yêu cầu các phần ngoài POS thể hiện chi tiết như quầy tính tiền. Branch/source/working: chore/source-checkpoint-20260925; base/expected parent/HEAD: 418bb23aefff35cff228fc8e1cbb72b4ee8bc40b. Giữ toàn bộ dirty tree, D01 và revision trước. Không commit/push/PR/merge/deploy/restart/đổi branch.

Mục tiêu: tên hàng, số lượng/đơn vị, thay đổi trước/sau, phiếu/kho, số tem thực tế và nội dung phiếu duyệt từ dữ liệu đã lưu thành công. Giữ POS, nhận diện nhiều người/nhiều phiếu, animation đã duyệt, quyền/tenant/SSE. Giao hàng chưa kết nối. Không sửa nghiệp vụ, schema, DTO, migration hoặc app điện thoại.

Allowed modified files:

- GaoApp.Web/Services/StoreMonitor/StoreActivityDetails.cs
- GaoApp.Web/Services/StoreMonitor/StoreActivityFilter.cs
- GaoApp.Web/Services/StoreMonitor/StoreActivityCatalog.cs
- GaoApp.Web/Services/StoreMonitor/StoreActivityEnricher.cs (mới)
- GaoApp.Tests/Observability/StoreActivityEnricherTests.cs (mới)
- GaoApp.Tests/Security/StoreMonitorHttpTests.cs
- GaoApp.Tests.Browser/store-monitor.browser.cjs
- docs/store-monitor.md
- Báo cáo này.

Locked: mọi source khác, controller/service nghiệp vụ, registry/presence/ticket/auth/config/CSS/markup, D01, revision trước. Evidence: Logs/store-monitor-upgrade/run-20261006-04/, artifacts/bin/obj/TestResults trong evidence; fixture SQL disposable. Không ghi GaoAppDb hiện tại. Một agent, không delegation.

Mandatory read: workflow development đã đọc trong phiên, báo cáo revision 3, monitor details/filter/catalog/work context/registry/JS, entity/DTO/controller phục vụ dữ liệu đã lưu, tests HTTP/details/browser, fixture build/start/cleanup. Search theo endpoint/mutation/lineId/parent/StoreId/quantity/unit/label result để xác minh nguồn, không lấy request notes/customer/photos. Trước xóa/sửa lấy snapshot nhỏ; sau thành công lấy dữ liệu không tracking với StoreId/parent tường minh. Enrichment lỗi dùng sự kiện nền, không làm thất bại nghiệp vụ. Tem gửi in phân biệt xác nhận số thực in. Review draft phân biệt duyệt.

Plan: baseline hash/backup; thêm resolver và format allowlist; tích hợp filter sau thành công; tests formatter + HTTP/SQL cho thêm/sửa/xóa/kho/tem/duyệt và tenant; browser regression nội dung/15 ô/animation/responsive; build/test/ảnh/scope/cleanup; handoff. Không migration/legal-owner business writes; tenant isolation được kiểm tra SQL fixture.

Validation: build Release solution và browser với SDK artifacts layout trong evidence (Web hiện tại đang chạy, không thay DLL bị khóa), restore cache khi artifacts mới cần assets; node --check browser; test filter StoreActivityTests/StoreActivityDetailsTests/StoreActivityEnricherTests/StoreMonitorHttpTests/EndpointSecurityCoverageTests, discovery đủ 5 lớp, >0 mỗi lớp, 0 fail/skip; browser --store-monitor và xem ảnh. SQL database inventory trước/sau giống nhau, source chỉ allowlist, HEAD không đổi. Tests có before/delete, số lượng 0, sai store/parent, lỗi HTTP không event, queue khác confirmation, draft khác approval; POS rollback/idempotency/SSE giữ test cũ. Required CI/independent review chưa chạy; chỉ Local TEST PASS/READY FOR COORDINATOR REVIEW.

Rủi ro: lấy nhầm parent/tenant, deleted row mất tên, yêu cầu chưa lưu bị báo như đã lưu, count/print bị nói quá, câu dài vượt khung, query failure che mất event. Mitigation: projection allowlist, persisted snapshots, success guards, số thực in, bounded name summaries, browser bounds và fallback. Rollback riêng diff 7 source/docs so với source-backup đầu task; không reset/stash tree. Handoff ghi commands/counts/evidence/giới hạn/rollback, không tự TASK PASS.

## Kết quả và handoff

**Local TEST PASS — READY FOR COORDINATOR REVIEW.** Chưa có independent review/CI/TASK PASS. Evidence riêng Logs/store-monitor-upgrade/run-20261006-04/; không tiếp tục D02 trong task này.

Source verification: đã đọc workflow trong phiên, revision 3, details/filter/catalog/work context/registry và renderer JS; entity StockDocument/Line/ProvisionalItem, StockCountDocument/Line, StockTransferDocument/Line, Warehouse/Unit/ProductLabelPrinting; intake DTO/controller/service và receipt recent contract; stock/count/transfer/management controllers, price draft/pricing/approval DTO và đoạn service liên quan; ProductLabelDesign/Service/Plan/Confirm/Complete; tests monitor/details/security/label/intake, FullApplicationFixture, TestApplicationBuild, SQL fixture/data-source và browser runner/Program. Xác minh tên hàng/đơn vị snapshot, parent/StoreId, status/HTTP result, các endpoint thực, server-confirmed line IDs, payload/result tem và redirect TempData của cước. Không sửa các source nghiệp vụ đã đọc.

Thay đổi:

- Enricher mới: projection không tracking, StoreId và parent tường minh; dữ liệu trước sửa/xóa và dữ liệu sau lưu. Tên hàng/đơn vị/lượng trước/sau, chênh lệch kiểm kê, kho chuyển, số phiếu và tên đại diện. Dòng thiếu đơn vị snapshot dùng liên kết đơn vị đã lưu; nhập đơn vị gốc chỉ khi Factor=1 và không có đơn vị riêng. Giá nhập đổi chỉ báo đã thay đổi, không công khai giá vốn/request prices.
- Intake: Capture/Known ghép đúng CommandId với RecentReceipts do server trả; Quantity/Review/Remove ghép itemId. Lưu draft khác duyệt; xóa giữ tên/lượng cũ. Review phiếu/giá nháp có loại/số phiếu và hàng liên quan; giá nháp chỉ đếm các ID server xác nhận trong LineVersions. Header đổi kho/ngày có trước/sau; cước hiện tổng đã lưu, chỉ nhận redirect có Success và không Error.
- Tem: đọc task LinesJson, job PayloadJson/ResultJson đã lưu; kế hoạch mỗi hàng, số gửi, số thực nhận/tổng gửi, số hủy và tên hàng đúng nội dung lệnh. Số 0/thiếu vẫn rõ; không khẳng định gửi lệnh là đã in vật lý. Complete giữ ý nghĩa hoàn tất phiếu. Không serialize JSON gốc, ghi chú, ảnh, người nhận/khách/nhà cung cấp.
- Filter: giữ publish sau inner transaction thành công, lỗi/model invalid/replay duplicate không ghi; lỗi enrich dùng thông báo nền. POS catalog/formatter/transactions giữ nguyên. Catalog thêm hai write thực Submit/Reject chuyển kho và sửa caption Complete tem. Không đổi registry/ticket/presence/SSE/quyền/CSS/markup/animation, schema hoặc posting business.
- Tests và probe: unit format trường hợp đặc biệt, HTTP/SQL thật cả nhập/xóa/kiểm kê/chuyển/duyệt/giá nháp/header/cước/tem/intake, tên và đơn vị lưu thật trong ô nhập, nhiều công việc, dữ liệu mô phỏng chi tiết để kiểm tra bố cục toàn bộ khu vực; tên hàng chứa HTML cũng được escape. Tài liệu vận hành cập nhật.

Validation cuối:

| Command | Kết quả / evidence |
|---|---|
| dotnet restore solution và runner browser, explicit NuGet.Config trong evidence, SDK artifacts path | exit 0, chỉ cache hiện có, packageSources clear; restore.log/restore-browser.log |
| dotnet build GaoApp.sln -c Release --no-restore --artifacts-path Logs/store-monitor-upgrade/run-20261006-04/artifacts | exit 0, 26 warnings cũ/0 errors; build-solution.log |
| dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj -c Release --no-restore cùng artifacts path | exit 0, 0 warnings/0 errors; build-browser.log |
| node --check GaoApp.Tests.Browser/store-monitor.browser.cjs | exit 0 sau thay đổi cuối |
| dotnet test GaoApp.Tests/GaoApp.Tests.csproj -c Release --no-build --no-restore cùng artifacts path, filter 5 lớp đã khóa, logger details-final4.trx | exit 0, **34/34**, 0 fail/skip, 1m13s; test-final4.log/tests/details-final4.trx |
| runner artifacts/bin/PosOffline.Browser/release/PosOffline.Browser.dll --store-monitor, GAOAPP_TEST_SQL_SERVER=.\SQLEXPRESS, GAO_MONITOR_TEST_OUTPUT=browser evidence | exit 0/PASS, 0 page errors; browser-final3.log/browser/browser-results.json |
| verify-evidence.ps1 và SQL inventory trước/sau | exit 0, 9 file đúng allowlist, 0 ngoài scope, HEAD/inventory giữ nguyên; scope-check.json/source manifests/test-discovery.json/browser-artifacts.csv |

Discovery: StoreActivityTests 11, StoreActivityDetailsTests 7, StoreActivityEnricherTests 10, StoreMonitorHttpTests 4, EndpointSecurityCoverageTests 2. Bốn HTTP tests kiểm tra Web/SQL thật: signed context nhiều nhân viên/nhiều phiếu và tem, auth/SSE/thu hồi quyền/POS rollback/replay, nhập/sửa/xóa và kiểm kê/chuyển/duyệt/tem in thiếu, intake/header/cước/giá nháp. Forbidden/not found/redirect lỗi không thêm event; foreign store không nhận sự kiện hoặc facts. Tem dùng máy in giả và chỉ đổi trạng thái fixture để xác nhận, không gọi máy in vật lý. Thêm kế hoạch 12 tem và xác nhận 8/12 tem qua HTTP thật, queue replay không ghi thêm.

Các lần chưa đạt được giữ trong log/TRX cũ: NuGet không đọc được config máy trong sandbox, giải quyết bằng quyền đọc cache được auto review chấp thuận; expression tree không cho optional argument, đã truyền null tường minh; phát hiện snapshot đơn vị trống sau update và bổ sung fallback liên kết; input fixture thiếu item version/warehouse và giả định redirect là 2xx, sửa đúng contract thật; kỳ vọng tên tem khác nội dung đã lưu và assertion browser còn câu cũ, sửa theo dữ liệu/wording mới. Không bỏ qua case hoặc nới guard nghiệp vụ để đạt test. Không có thay đổi production C# sau build/test final4; browser final3 kiểm tra đúng artifact cuối. Các database test mới tự dọn.

Browser: thao tác nhập thật hiện tên hàng/lượng/đơn vị riêng ba nhân viên/bốn phiếu (một người hai phiếu), POS ba quầy giữ behavior; hai phiếu tem signed context và đóng tab độc lập. UI fixture riêng 15 công việc (3 mỗi module), tên hàng/lượng thay đổi và số tem chi tiết, fit 1920×1080, không tràn ngang ở 1366/1024/704/390; sáng/tối, offline/reconnect, motion off/reduced motion, năm art thực sự chuyển động, save/edit ordering và một lần xác nhận, DOM/animation của ô khác không restart đều PASS. Đã xem ảnh cuối office-light và office-live-multiple-receipts; light có đủ ô/giờ/nội dung, ảnh live có dữ liệu Web/SQL fixture thật và đơn vị liên kết. office-metrics.json xác nhận height=1080. UI fixture được gắn nhãn mô phỏng, không gửi dữ liệu giả vào registry/SQL. Không cam kết mọi số lượng/độ dài tùy ý đều vừa một màn hình.

Phạm vi: baseline 3126 source files → 3129, thêm Enricher/test/report; đúng 9 file thay đổi so với bytes trước task, 0 scope violation. Branch/HEAD/base/parent giữ nguyên; D01/dirty tree/revision trước giữ nguyên. Không commit/push/PR/merge/deploy/restart hoặc writes vào GaoAppDb hiện tại. SQL inventory trước/sau còn nguyên hai database fixture cũ, không còn GUID database mới từ test/browser. Evidence hashes và source-backup phục vụ rollback riêng task này; không reset/stash repository.

Giới hạn: Web hiện đang chạy vẫn dùng build trước; cần chạy Web với build mới để dùng thay đổi. Màn tổng lưu trong bộ nhớ một Web instance như trước; query không đọc được thì fallback cơ bản, tên rất dài/phiếu nhiều hàng hiển thị tối đa hai tên đại diện và tổng số. Presence là tương tác phần mềm; tem gửi in không chứng minh máy in đã chạy; giao hàng chưa kết nối. Chưa có CI/independent review/thiết bị in vật lý. Reviewer tập trung Store/parent trước xóa, đơn vị thiếu snapshot, quantity trước/sau, CommandId intake, số thực nhận trên tem, redirect lỗi và publish sau commit. Bàn giao **READY FOR COORDINATOR REVIEW**.
