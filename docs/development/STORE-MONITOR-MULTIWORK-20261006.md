# Giám sát nhiều công việc đồng thời — 06/10/2026

## Contract revision 2 — READY

Revision riêng theo steering mới của user: nhập hàng và mọi khu vực khác phải hiện nhiều nhân viên/mỗi nhân viên một hoặc nhiều phiếu, không chỉ POS. Giữ nguyên contract/evidence revision 1. Branch `chore/source-checkpoint-20260925`, base/expected parent/HEAD `418bb23aefff35cff228fc8e1cbb72b4ee8bc40b`; dùng dirty working tree hiện tại và giữ tất cả thay đổi có sẵn.

Level C cho identity của presence/chứng từ và tenant checks read-only, không thay đổi authorization business/schema. Mục tiêu: mỗi khu vực có ô theo người + phiếu/quầy; cùng người hai phiếu tách riêng; mọi ô hoạt động có animation, nội dung và mốc giờ riêng. Không giới hạn một ô ngoài POS; các ô đang làm không bị cắt bằng scroll nội bộ. Bố cục co giãn theo số việc; quá khả năng một màn hình vẫn giữ đầy đủ ô và số lượng, không tuyên bố số việc tùy ý đều vừa một khung. Giao hàng chưa kết nối, D01 không bắt đầu.

Mandatory read: workflow, revision 1, registry/filter/ticket/details/presence controller/JS, monitor view/CSS/JS, các page/detail/API nhập hàng/kiểm kê/chuyển kho/duyệt/in tem và entity/DTO, label JS call sites, tests/probe/fixture. Search targets: document ID cha/dòng; page and in-page task switching; tenant validation; lane grouping/caps/internal scrolling; ticket/tab lifetime; SSE/replay/fail.

Allowed source: `GaoApp.Web/Services/StoreMonitor/StoreActivityRegistry.cs`, `StoreActivityTicket.cs`, `StoreActivityFilter.cs`, `StoreActivityDetails.cs`, file mới `StoreActivityWorkContext.cs`; `GaoApp.Web/Areas/Admin/Controllers/StoreActivityController.cs` chỉ truyền context đã ký; `GaoApp.Web/wwwroot/Admin/js/store-activity.presence.js`, `store-monitor.page.js`, `GaoApp.Web/wwwroot/Admin/css/store-monitor.css`; `GaoApp.Web/wwwroot/Admin/js/printing/label-printing.js` chỉ nhận context đã ký khi đổi/đóng phiếu, không sửa nghiệp vụ in; `GaoApp.Tests/Observability/StoreActivityTests.cs`, `StoreActivityDetailsTests.cs`, `GaoApp.Tests/Security/StoreMonitorHttpTests.cs`; `GaoApp.Tests.Browser/Program.cs` chỉ nhánh monitor và `store-monitor.browser.cjs`.

Allowed docs/evidence: tài liệu này, `docs/store-monitor.md`; `Logs/store-monitor-upgrade/run-20261006-02/`, fixture output/bin/obj/TestResults. Locked mọi source khác, revision 1/evidence cũ, DB/schema/config/tiền/kho/quyền. Không commit/push/merge/deploy/đổi branch. User đã cho phép dữ liệu hiện tại phục vụ code; không cần writes GaoAppDb, dùng disposable SQL cho validation.

Identity: work key/document từ máy chủ, tenant-validated hoặc response của business action thành công; không nhận ID/tên phiếu do browser tự khai vào presence. Mã phiếu được ký trong ticket. Tách dòng/phiếu và loại chứng từ có ID trùng; chuyển phiếu reset thời điểm mở; rời một tab không xóa phiếu khác. POST errors/replay/rollback không phát event. Không đọc input values hoặc ghi chú/khách. Read-only context lookup giới hạn đúng Store, không sửa posting/audit hoặc kết quả nghiệp vụ; lỗi monitor không làm business action lỗi. Shared files chỉ root sửa; không có agent khác/concurrent delegation.

Plan: thêm identity và context ký; gắn events theo phiếu; dựng grid đa việc tất cả module và density khi đông; bổ sung regression; build/test/ảnh/manifest/SQL cleanup/handoff. Tests: Release solution + browser build; StoreActivityTests/DetailsTests/StoreMonitorHttpTests/EndpointSecurityCoverageTests đủ 4 lớp >0, 0 fail/skip; HTTP thật nhiều phiếu/cùng người/khác người/tenant/forged ID/leave; browser thật nhiều người nhập phiếu độc lập và 3 quầy; UI fixture >=3 việc mỗi module (cùng người 2 phiếu, kiểm kê/chuyển kho cùng ID), nội dung/animation/timer đúng từng ô, không mất ô/internal scroll, reconnect/motion/escape/responsive 1920/1366/1024/704/390; xem ảnh và sửa. Phân biệt fixture UI với giao dịch thật. Manifest chỉ allowlist và HEAD không đổi; SQL test DB tự dọn.

PASS criteria local: build và tests mandatory pass, UI visible đầy đủ các ô kiểm tra, 0 scope violation. Handoff: findings/change/test/evidence/risks/rollback; chỉ READY FOR COORDINATOR REVIEW sau local TEST PASS, không tự gán independent review/CI/TASK PASS. Không commit (commit message chưa áp dụng). Rollback bằng patch hoặc backup đầu revision sau đối chiếu, không reset working tree. Rủi ro: mật độ khi rất nhiều việc, generic page không có phiếu, label task đổi bằng XHR, lookup thất bại phải không ảnh hưởng nghiệp vụ.

## Kết quả

**Local TEST PASS — READY FOR COORDINATOR REVIEW.** Chưa có independent review/CI, không tự gán TASK PASS. Evidence riêng: `Logs/store-monitor-upgrade/run-20261006-02/`.

Mỗi khu vực đã kết nối hiện các công việc theo nhân viên + terminal + phiếu, thay vì gộp một người thành một ô và chỉ giữ một ô lịch sử ngoài POS. Hai phiếu của cùng người tách riêng; hai tab cùng người/cùng phiếu được nhóm chung. POS giữ cách thể hiện theo quầy hiện tại. Registry và ticket thêm work key/document tùy chọn, tương thích ticket cũ; controller chỉ dùng context đã ký. Máy chủ xác minh Store của chứng từ trước khi ký context cho trang chi tiết; danh sách không có chứng từ chỉ nhận context chung. Dòng kiểm kê/chuyển kho dùng ID phiếu cha, không lẫn với ID dòng hoặc loại chứng từ khác.

In tem nhận ticket qua header của GET chi tiết đã được cấp quyền. Client chỉ đổi context khi phiếu đã hiển thị, đổi tab nghiệp vụ hoặc đóng phiếu; response nền không tự đổi context. Chuyển phiếu đặt lại mốc giờ mở. Phần tích hợp này không thay đổi lệnh in, tính tiền, kho hay duyệt nghiệp vụ. Mọi lookup mới là read-only, lỗi monitor được bắt để không làm nghiệp vụ thành công trả lỗi.

Màn tổng tự đổi mật độ khi có nhiều việc. Các khu vực dùng lưới ngang nhau; từng ô giữ tên nhân viên, mã phiếu/quầy, nội dung lưu, mốc giờ và bộ đếm thời gian riêng. Giữ animation riêng cho nhập hàng, POS, kho, tem và duyệt; giao hàng vẫn báo chưa kết nối. Bỏ giới hạn/clipping và khung cuộn nội bộ của danh sách công việc. 15 công việc kiểm tra cùng lúc vừa màn 1920×1080, các khu vực ở hai hàng; nội dung nhiều hơn có thể cần cuộn toàn trang.

### Validation và bằng chứng

| Kiểm tra | Kết quả | Evidence |
|---|---|---|
| Release solution | PASS, 0 lỗi; build cuối incremental 0 warning | `build-solution-final.log` |
| Release browser runner | PASS, 0 lỗi; 23 warning test đã có | `build-browser-repaired.log` |
| StoreActivityTests | 11/11 | `tests/multiwork-final.trx` |
| StoreActivityDetailsTests | 7/7 | `tests/multiwork-final.trx` |
| StoreMonitorHttpTests | 2/2, Web/SQL thật | `tests/multiwork-final.trx` |
| EndpointSecurityCoverageTests | 2/2 | `tests/multiwork-final.trx` |
| Tổng test bắt buộc | 22/22, 0 fail, 0 skip; đủ 4 lớp >0 | `test-final.log`, `test-discovery.json` |
| JavaScript syntax | PASS cả 4 file JS thay đổi | `node --check` |
| Browser thật và UI fixture riêng | PASS, không có page error | `browser-layout-final.log`, `browser/browser-results.json` |
| Phạm vi source và HEAD | PASS, 17 file trong allowlist, 0 file ngoài phạm vi; HEAD giữ nguyên | `scope-check.json`, `source-changes.csv`, `head-before.txt`, `head-after.txt` |
| SQL cleanup | PASS, danh sách database tạm trước/sau giống nhau | `sql-before.txt`, `sql-after.txt` |

HTTP test tạo nhiều phiếu nhập qua endpoint thật và presence bằng ticket đã ký; xác nhận cùng người hai phiếu, khác người nhiều phiếu, rời một tab vẫn giữ phiếu khác. Form giả `workKey/document` bị bỏ qua. Phiếu cửa hàng khác không được ký context; trang shell kiểm kê của cửa hàng khác chỉ nhận context chung. Sửa số lượng kiểm kê bằng 0 vẫn dùng work key phiếu cha và chi tiết “Đã đếm 0”. Hai phiếu tem cùng Store có ticket riêng, chuyển phiếu thay context; GET tem Store khác không cấp header. Test bảo mật và các case sự kiện lỗi/replay/rollback tiếp tục pass.

Browser thực dùng Web/services/SQL disposable: cùng tài khoản ở ba quầy, thêm hàng/thanh toán/chốt/giữ đơn; ba nhân viên nhập bốn phiếu (một nhân viên hai phiếu); hai tab in tem của cùng nhân viên nhận signed context qua UI/XHR. Đóng một phiếu chỉ bỏ context của tab đó. Kiểm tra giờ lưu, tuổi sự kiện tăng, nội dung riêng, vị trí quầy ổn định, animation sau lưu, mất mạng và reconnect. Ảnh `office-live-three-counters.png` và `office-live-multiple-receipts.png` là thao tác thật trên fixture SQL, không dùng GaoAppDb hiện tại.

UI fixture riêng chỉ thay snapshot trong trang test, không đưa sự kiện giả vào registry/SQL: 3 công việc ở mỗi khu vực đã kết nối, tổng 15; cùng người hai phiếu và kiểm kê/chuyển kho cùng số ID vẫn tách. Ảnh `office-light.png`, `office-dark.png`, `office-1366.png`, `mobile.png` ghi rõ **DỮ LIỆU MÔ PHỎNG**. Browser kiểm tra đủ 15 ô, animation riêng của 5 nghiệp vụ, giao hàng không animation, tất cả ô vừa 1920×1080, không scroll nội bộ hoặc tràn ngang ở 1920/1366/1024/704/390; motion off/reduced motion, DOM và animation node ổn định, nội dung HTML được escape, sự kiện cũ còn nội dung nhưng không giả presence/hoạt động mới. Đã xem trực tiếp ảnh cuối nền sáng, nhiều phiếu nhập thật, 1366 và mobile. `office-metrics.json` xác nhận document height 1080 bằng viewport 1080.

Các lần kiểm tra ban đầu được giữ nguyên log: HTTP/browser dùng giá trị fixture `DirectReceiptReason` không hợp lệ, đã sửa dữ liệu test thành giá trị nghiệp vụ cho phép “Khác”; browser runner gặp CS8110 khi dùng local function trong EF expression, đã chuyển giá trị fixture thành biến ngoài lambda; UI 15 việc vượt chiều cao 1080, đã sửa mật độ/bố cục và chạy lại browser thật tới PASS. Không nới nghiệp vụ hoặc bỏ assertion để vượt test. Log cuối được phân biệt với log lỗi ban đầu.

Manifest: 3114 file trước, 3116 file sau (thêm WorkContext và báo cáo revision này), 17 file thay đổi đúng allowlist. Diff `label-printing.js` chỉ thêm nhận/chuyển/đóng signed monitor context; `Program.cs` chỉ thay nhánh `--store-monitor`. Các thay đổi dirty tree trước đó, revision 1 và evidence cũ được giữ nguyên. `verify-evidence.ps1` xuất SHA256 trước/sau, kiểm tra 4 lớp/22 test, HEAD và SQL inventory; `browser-artifacts.csv` ghi hash ảnh/result cuối.

Hai database tạm đã có trước task (`GaoApp_R2_InventoryPosting_7DB7C912679549C18B6D5BE610DA83A9`, `GaoApp_R2_InventoryPosting_D97ADC6A25BF48299F630D2183FDA18C`) vẫn nguyên; fixture của các lần chạy mới tự dọn. Không ghi dữ liệu business/schema/quyền/config vào GaoAppDb hiện tại, không commit/push/merge/deploy/đổi branch. D01 và phần kết nối giao hàng chưa bắt đầu trong task này.

### Giới hạn và bàn giao

Trang danh sách chưa chọn phiếu hiện ngữ cảnh chung, không đoán ID từ input. POS theo quầy/giỏ hiện tại; dùng chung tài khoản không cho biết số người vật lý. Số việc lớn hơn hoặc văn bản dài hơn fixture có thể cần cuộn toàn trang; không hứa mọi số lượng đều vừa 1080. Presence biểu thị tương tác màn hình; xác nhận thao tác vật lý/máy in chưa nằm trong test này. Registry vẫn là bộ nhớ tạm của một Web instance với các giới hạn sẵn có, chưa có pub/sub nhiều instance. Nguồn Web hiện tại cần chạy build mới để dùng bản nâng cấp.

Coordinator review tập trung tenant-bound work identity, parent ID của dòng, context in tem chuyển/đóng và lưới nhiều việc. Evidence/test đủ cho local validation; chưa thay thế independent review/CI hoặc gate máy in/thiết bị thật của D00. Rollback bằng diff/backup `source-backup/` đầu revision sau đối chiếu các chỉnh sửa tiếp theo; file mới được bỏ riêng nếu rollback, không reset dirty working tree. Không có hành động release/commit để rollback.
