# Nâng cấp màn hình giám sát 06/10/2026

## Contract revision 1 — READY

- Task: STORE MONITOR UPGRADE, thực hiện trước bước giao hàng tiếp theo theo yêu cầu user.
- Phạm vi: nền sáng mặc định; các khu vực nhập hàng/POS/kho/in tem/duyệt có animation theo trạng thái thật; mỗi quầy/người có nội dung, chứng từ, giờ cập nhật và thời gian trôi qua riêng. Giao hàng giữ trạng thái chưa kết nối.
- Branch: `chore/source-checkpoint-20260925`; HEAD/base/expected parent: `418bb23aefff35cff228fc8e1cbb72b4ee8bc40b`. Dùng working tree hiện có; bảo toàn thay đổi đang có.
- Level: B cho giao diện, kiểm tra tenant/security của luồng giám sát khi bổ sung metadata; không sửa nền tiền/kho/schema.
- Nguồn đọc: view/CSS/JS monitor, registry/catalog/filter/ticket/presence controller và JS, endpoint POS/StockDocuments/ReceiptIntake/LabelPrinting, DTO trả về, test activity/security và browser runner, development workflow, tài liệu giám sát.
- Allowed source: `GaoApp.Web/Areas/Admin/Views/StoreMonitor/Index.cshtml`, `GaoApp.Web/wwwroot/Admin/css/store-monitor.css`, `GaoApp.Web/wwwroot/Admin/js/store-monitor.page.js`, `GaoApp.Web/Services/StoreMonitor/StoreActivityRegistry.cs`, `StoreActivityCatalog.cs`, `StoreActivityFilter.cs`, file mới `StoreActivityDetails.cs` cùng thư mục; `GaoApp.Tests/Observability/StoreActivityTests.cs`, file test details cùng thư mục; `GaoApp.Tests/Security/StoreMonitorHttpTests.cs`; `GaoApp.Tests.Browser/Program.cs` chỉ nhánh `--store-monitor`, `store-monitor.browser.cjs`.
- Allowed docs/evidence: tài liệu này, `docs/store-monitor.md`, dòng trạng thái/pointer trong delivery roadmap; `Logs/store-monitor-upgrade/run-20261006-01/`, outputs browser monitor và bin/obj/runtime fixture. Lưu source manifest trước/sau và backup browser evidence có sẵn.
- Locked: mọi source khác, migrations/config/authorization, nghiệp vụ tiền/kho/POS/nhập/tem, dữ liệu và quyền đã cấp. User cho phép dùng toàn bộ GaoAppDb phục vụ code; task này không cần sửa nghiệp vụ trên database đó. Không commit/push/merge/deploy/đổi branch.

## Nghiệp vụ và validation

Không gom ba quầy cùng tài khoản thành một người ở một khu vực. Nhóm presence theo người/module/quầy, chỉ gom các tab của cùng nhóm. Phân biệt đang mở, đang tương tác, đã lưu và mất kết nối. Thời gian từ giờ máy chủ; thời gian mở màn hình không coi là thời gian thực hiện công việc vật lý. Sự kiện sau lưu có animation riêng dù người đã rời trang, không làm thay đổi trạng thái chứng từ.

Metadata chỉ lấy từ response thành công và các trường nghiệp vụ cho phép; không đọc nội dung gõ hoặc phát cả request/response. Giữ nguyên ticket/antiforgery/quyền/tenant, không phát event khi rollback/replay/lỗi. Chứng từ dùng ID cha; không nhầm ID dòng hàng với ID phiếu. Giữ giới hạn registry/history và cơ chế ngắt khi mất mạng/thu hồi quyền.

Build Release solution và browser runner. Chạy StoreActivityTests, tests details mới, StoreMonitorHttpTests, EndpointSecurityCoverageTests: discovery >0 từng lớp, 0 fail/skip bắt buộc. Browser probe mở Web/SQL thật, ba quầy cùng tài khoản thao tác khác nhau, kiểm tra từng quầy/metadata/mốc giờ/animation; xem tất cả khu vực, nền sáng mặc định kể cả preference tối cũ, reconnect/reduced-motion, responsive 1920/1366/1024/704/390. Snapshot UI có thể dùng dữ liệu tổng hợp để kiểm tra các trạng thái khu vực, phải phân biệt với bài Web/SQL thật. Xem ảnh và sửa nếu bố cục/độ rõ chưa đạt. Không chạy toàn bộ suite tiền/kho khi source nghiệp vụ không đổi.

Kết quả local là TEST PASS khi đủ các checks; không tự kết luận review độc lập/CI/TASK PASS. Không tiếp tục D01 trong task này. Rollback chỉ patch task hoặc khôi phục các file task từ backup đầu task sau đối chiếu; không reset/xóa working tree hay dữ liệu có sẵn.

## Kết quả

**TEST PASS local**, chưa gán REVIEWED/CI PASS/TASK PASS. Evidence: `Logs/store-monitor-upgrade/run-20261006-01/`.

### Thay đổi đã hoàn thành

- Nền sáng mặc định, preference theme v2; vẫn có lựa chọn nền tối, tắt animation, toàn màn hình và reduced motion.
- POS chia ô riêng theo mã quầy, thứ tự quầy ổn định kể cả sau rời trang. Presence không gộp cùng tài khoản ở các quầy hoặc các khu vực khác nhau.
- Nhập hàng, kho, in tem và duyệt đều có người/quầy, thao tác đã lưu, mã chứng từ, metadata cho phép, giờ lưu và bộ đếm thời gian. Thời điểm mở màn hình được ghi riêng, không gọi là thời gian thực hiện công việc.
- Animation theo khu vực: ghi phiếu/gói dữ liệu, tia quét mã, băng chuyền kiện hàng, tem ra máy in, dấu duyệt. Sự kiện vừa lưu có animation 12 giây ngay cả khi không còn presence. Cập nhật snapshot giữ DOM/animation liên tục, metadata được escape.
- Filter hỗ trợ cả ObjectResult và JsonResult thành công, bỏ qua duplicate của lệnh in. Metadata không phát cả request/response, thông tin khách hay ghi chú. Phân biệt ID phiếu với ID dòng/lệnh in và đơn đang giữ với giỏ nháp mới.
- Bố cục đầy đủ khu vực vừa khung 1920×1080; màn hình nhỏ tự xếp lại. Giao hàng giữ placeholder chưa kết nối. Không sửa nghiệp vụ/schema/tiền/kho/phân quyền.

### Validation và bằng chứng

| Check | Kết quả | Bằng chứng |
| --- | --- | --- |
| Build Release solution | PASS, 0 lỗi. Lần build đầy đủ có 26 cảnh báo hiện hữu; lần cuối incremental 0 cảnh báo | `build-tests.log`, `build-final.log` |
| Build Release browser runner | PASS, 0 lỗi | `build-browser.log` |
| C# selected tests | **15/15 PASS, 0 fail/skip**: registry/catalog 6, metadata 6, HTTP tenant/session/replay/rollback 1, endpoint security 2; đủ cả 4 lớp, discovery >0 | `tests/monitor-final.trx`, `test-final.log`, `test-discovery.json` |
| Chrome + Web/SQL thật | PASS: cùng tài khoản mở 3 quầy, thêm hàng/chốt/giữ đơn riêng từng quầy, mã đơn/chi tiết/giờ/age; receiving presence có animation; animation sau lưu khi rời trang; mất mạng/nối lại | `browser-final.log`, `browser/browser-results.json`, `browser/office-live-three-counters.png` |
| UI fixture tách riêng | PASS: 5 khu vực có animation khác nhau, 3 ô POS; giữ node animation khi snapshot đổi, escape nội dung, nội dung cũ không giả presence; delivery static | cùng log/JSON; `browser/office-light.png` có nhãn **DỮ LIỆU MÔ PHỎNG** |
| Visual/responsive | Đã xem và sửa bố cục; đủ khu vực trong 1920×1080, không tràn ngang 1366/1024/704/390; nền sáng/tối, motion off và reduced motion PASS | `browser/office-light.png`, `office-dark.png`, `office-1366.png`, `mobile.png` |
| SQL fixture cleanup | PASS, trước/sau giữ nguyên 2 database thử nghiệm đã có; không còn database mới do task tạo | `sql-before.txt`, `sql-after.txt` |
| Scope/HEAD | PASS: baseline 3111 file, sau 3114; 15 file thay đổi đều trong allowlist, 0 ngoài scope; HEAD không đổi | `source-before.csv`, `source-after.csv`, `source-changes.csv`, `scope-check.json`, `head-before.txt`, `head-after.txt` |

Hai lần browser probe đều pass; lần cuối đã kiểm tra vị trí quầy ổn định, khung văn phòng, animation không bị reset và metadata escape. Ảnh all-module là kiểm tra giao diện bằng snapshot tổng hợp; không được dùng làm bằng chứng giao dịch thật của cả năm module hay máy in vật lý. Giao dịch thật trong browser là POS; receiving kiểm tra presence thật. Các thao tác/metadata module còn lại được kiểm tra ở registry/catalog và metadata tests theo whitelist.

Không khởi động lại/phát hành bản đang chạy ngoài fixture, không commit/push/đổi branch, không viết dữ liệu nghiệp vụ vào GaoAppDb hiện tại. Khi chạy Web bằng build mới, trang `/admin/store-monitor` dùng giao diện mới. D00 vẫn giữ cổng thiết bị/in bill ở tiệm; D01–D12 chưa bắt đầu.
