# Áp dụng animation đã duyệt — 06/10/2026

## Contract revision 3 — READY

Task ID SM-ANIMATION-03, Level B, implementation. User đã duyệt bản mô phỏng `store-animation-review.html` và yêu cầu áp dụng hiệu ứng vào màn thật. Contract riêng, giữ nguyên revision 1/2 và evidence cũ. Source/working/base branch `chore/source-checkpoint-20260925`, base commit/expected parent/HEAD `418bb23aefff35cff228fc8e1cbb72b4ee8bc40b`; giữ dirty tree hiện hữu. Không commit/push/PR/merge/đổi branch/deploy; commit message chưa áp dụng.

Mục tiêu: chuyển đúng 5 chuyển động đã duyệt vào ô công việc hiện tại: phiếu nhập có dòng ghi dữ liệu và bút, POS tia quét, kho băng chuyền/kiện hàng, máy in đưa tem ra, phiếu duyệt đóng dấu. Mỗi ô giữ dữ liệu thật/người/phiếu/giờ riêng, không đổi lưới nhiều việc. Lưu thành công hiện dấu xác nhận ngắn; chờ thao tác dừng chuyển động. Khi sự kiện lưu mới hơn tín hiệu thao tác của ô, ưu tiên trạng thái vừa lưu; thao tác mới sau lần lưu trở lại đang làm. Không đưa đồng hồ giả, auto-cycle, nút phát lại hoặc panel duyệt của mô phỏng vào màn vận hành. Giao hàng giữ chưa kết nối. Không mở D01.

Allowed modified files tuyệt đối:

- `GaoApp.Web/wwwroot/Admin/css/store-monitor.css`
- `GaoApp.Web/wwwroot/Admin/js/store-monitor.page.js`
- `GaoApp.Tests.Browser/store-monitor.browser.cjs`
- `docs/store-monitor.md`
- Tài liệu revision này.

Locked: mọi source khác, View/controller/backend/registry/ticket/presence/printing JS/auth/config/migration/schema/DB business, revision 1/2, visualization đã duyệt. Evidence/runtime được phép: `Logs/store-monitor-upgrade/run-20261006-03/`, fixture output/TestResults/bin/obj. Shared files chỉ chat này sửa, không delegation/concurrent agents. Trạng thái/input/output frontend từ snapshot đã có; permission/SSE/tenant boundary không đổi.

Mandatory read đã thực hiện: `docs/development/GAOAPP-DEVELOPMENT-WORKFLOW.md`, báo cáo revision 2, CSS/JS monitor, View Index, browser probe/Program/csproj, registry và bản mô phỏng đã duyệt. Fixture source được đọc trước validation. Search targets: markup/keyframes, mode working/saved/idle, EditedAtUtc/event timestamp, DOM patch giữ animation, motion off/reduced motion/offline, density/viewport/tenant fixture. Không đọc input values hoặc thêm API.

Plan: backup/manifest/preflight; chuyển art và keyframes đúng kích thước ô, màu phù hợp nền sáng/tối; ưu tiên save khi mới hơn edit; bổ sung browser regression theo thời gian/ô khác; build/test/browser/ảnh/scope/SQL cleanup; implementation handoff. Không cần migration/data/legal-owner query vì task chỉ frontend. SQL validation dùng fixture disposable, không GaoAppDb hiện tại.

Validation khóa: `dotnet build GaoApp.sln -c Release --no-restore`; `dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj -c Release --no-restore`; `node --check` 2 JS thay đổi; `dotnet test GaoApp.Tests/GaoApp.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~StoreActivityTests|FullyQualifiedName~StoreActivityDetailsTests|FullyQualifiedName~StoreMonitorHttpTests|FullyQualifiedName~EndpointSecurityCoverageTests"` đủ 4 lớp, 22 cases, 0 fail/skip; browser runner Release `--store-monitor`. Browser kiểm tra operations Web/SQL thật đã có + UI fixture 15 việc/3 mỗi khu vực, chuyển động đúng 5 art, save/edit/idle riêng từng ô, xác nhận hiện ngắn không lặp, idle không animation, bounds art không cắt, snapshot không restart DOM/animation, escaped metadata, offline/reconnect, light/dark, motion off/reduced motion, responsive 1920/1366/1024/704/390, 15 việc vừa 1920×1080, giao hàng tĩnh. Xem ảnh và sửa nếu cần. Required CI chưa thực hiện ở task local này; local TEST PASS không thay independent review/CI/TASK PASS.

PASS criteria local: commands exit 0, 22 tests/4 lớp, browser PASS, ảnh đúng mẫu duyệt, 0 scope violation, HEAD không đổi, SQL inventory trước/sau bằng nhau. Rủi ro: art bị lệch/cắt khi thu nhỏ, animation restart theo heartbeat, dấu check lặp, trạng thái mới/cũ nhầm, dark contrast, mất fit 1080. Rollback: đối chiếu và revert riêng diff revision này từ `source-backup/`, giữ thay đổi trước task; không reset/stash. Handoff gồm source đọc, diff, commands/counts/evidence, limitations, rollback, READY FOR COORDINATOR REVIEW; không tự tuyên bố review/CI/TASK PASS.

## Kết quả

**Local TEST PASS — READY FOR COORDINATOR REVIEW.** Không phải independent review/CI/TASK PASS. Evidence riêng `Logs/store-monitor-upgrade/run-20261006-03/`.

### Source verification và thay đổi

Đã đọc workflow, báo cáo revision 2, CSS/JS monitor, View Index, browser probe/Program/csproj, registry (Presence/Snapshot), FullApplicationFixture (startup/auth/cleanup), bản mô phỏng đã duyệt. Search xác minh mode, timestamps, keyframes, DOM patch, responsive density và Web DLL fixture. Backend và workflow business giữ nguyên.

- `store-monitor.page.js`: markup 5 art có wrapper 110×90 được căn giữa, chung dấu xác nhận; chọn mode theo thời điểm lưu và tương tác. Lưu thành công thay thế tín hiệu gõ trước đó; chỉ tương tác mới hơn lần lưu mới chạy lại hiệu ứng. Hết 12 giây vừa lưu thì chờ tĩnh, không hồi lại tín hiệu gõ cũ. Một save tiếp theo phát lại check riêng của ô, không thay node/animation của công việc khác.
- `store-monitor.css`: chuyển hình và nhịp đã duyệt — giấy/bút/dòng dữ liệu 2,8 giây, tia quét 2,2 giây, kiện hàng 3,6 giây/băng chuyền 1,4 giây, tem ra 3 giây, dấu duyệt 3,4 giây. Dấu xác nhận 0,55 giây, chạy một lần; hình chờ tĩnh. Art scale 0,6 theo khung 66×54, density tiếp tục co lại như trước; màu hộp/tem/giấy tương thích nền sáng/tối. Không thay lưới hoặc nội dung nghiệp vụ.
- `store-monitor.browser.cjs`: bổ sung đo transform/opacity thực sự thay đổi của cả 5 art, bounds đủ 15 ô, save trước/sau edit, hai save liên tiếp, hết thời gian xác nhận không hồi hoạt động cũ, chờ không animation và giữ nguyên phiếu/animation bên cạnh. Fixture UI xác định tín hiệu edit mới hơn sự kiện cũ, tách rõ các case timestamp trong bài transition.
- `docs/store-monitor.md` và báo cáo này: cập nhật hành vi, validation, giới hạn và bàn giao.

Không thêm panel phóng to, nút phát lại, auto-cycle hoặc dữ liệu mô phỏng vào màn vận hành; đây là các công cụ duyệt trong mẫu. Không thêm thư viện/asset/API, không sửa quyền/presence/SSE/controller/printing nghiệp vụ, không bắt đầu giao hàng.

### Validation

| Command | Kết quả / Evidence |
|---|---|
| `dotnet build GaoApp.sln -c Release --no-restore` | exit 0, incremental 0 warning/0 error; `build-solution.log` |
| `dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj -c Release --no-restore` | exit 0, incremental 0 warning/0 error; `build-browser.log` |
| `node --check GaoApp.Web/wwwroot/Admin/js/store-monitor.page.js` và `node --check GaoApp.Tests.Browser/store-monitor.browser.cjs` | exit 0; chạy lại sau sửa JS cuối |
| `dotnet test GaoApp.Tests/GaoApp.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~StoreActivityTests\|FullyQualifiedName~StoreActivityDetailsTests\|FullyQualifiedName~StoreMonitorHttpTests\|FullyQualifiedName~EndpointSecurityCoverageTests" --logger "trx;LogFileName=animation-final.trx" --results-directory Logs/store-monitor-upgrade/run-20261006-03/tests` | exit 0, 22/22, 0 fail/skip, 50 giây; `test-final.log`, `tests/animation-final.trx` |
| `$env:GAO_MONITOR_TEST_OUTPUT = <repo>/Logs/store-monitor-upgrade/run-20261006-03/browser`; `dotnet GaoApp.Tests.Browser/bin/Release/net8.0/PosOffline.Browser.dll --store-monitor` | exit 0/PASS cả lần đầu và bản cuối; `browser.log`, `browser-final.log`, `browser/browser-results.json` |
| `& Logs/store-monitor-upgrade/run-20261006-03/verify-evidence.ps1` | exit 0, phạm vi 5 file, 0 ngoài allowlist, HEAD/SQL inventory không đổi; `scope-check.json`, manifest/hash artifacts |

Test discovery đủ 4 lớp >0: StoreActivityTests 11, StoreActivityDetailsTests 7, StoreMonitorHttpTests 2, EndpointSecurityCoverageTests 2. Không sửa C# hoặc test C# trong revision này; lần chỉnh JS cuối chỉ làm chờ tĩnh sau save expiry, được syntax/browser cuối xác nhận. Không có build/test failure; một lần gửi patch lỗi cú pháp orchestration chưa thực thi, được gửi lại đúng nội dung, không để lại file nửa chừng.

Browser thật kiểm tra Web/services/SQL/auth: ba quầy trên một tài khoản với thêm hàng/thanh toán/chốt/giữ đơn; ba nhân viên nhập bốn phiếu (một người hai phiếu); hai tab tem có signed context từ XHR, đóng một không ảnh hưởng tab kia; giờ lưu/tuổi sự kiện, offline/reconnect, nội dung lưu và trạng thái. SQL chỉ disposable fixture, không GaoAppDb hiện tại. `office-live-three-counters.png` và `office-live-multiple-receipts.png` là dữ liệu fixture nghiệp vụ thật.

UI fixture riêng có 15 công việc/3 mỗi module, không đưa sự kiện giả vào registry/SQL. Browser đo 5 bộ giá trị transform/opacity/position trước/sau (`animationMotionSamples`), không chỉ kiểm tra khai báo tên animation. Xác nhận art căn giữa/không cắt, save mới hơn typing dừng bút và hiện check 1 lần, save thứ hai phát lại check, hết 12 giây chờ tĩnh, tương tác mới hơn save chạy lại; phiếu khác của cùng người giữ nguyên DOM và animation instance. Nhịp snapshot không khởi động lại animation. Metadata HTML được escape, sự kiện cũ vẫn hiện nội dung nhưng không có chuyển động giả.

Light/dark, hiệu ứng off/reduced motion, giao hàng tĩnh và responsive 1920/1366/1024/704/390 đều PASS, không tràn ngang hoặc khung cuộn nội bộ cắt các công việc. `office-metrics.json` vẫn xác nhận document height 1080 = viewport 1080 tại 1920×1080, mỗi hàng module cao 413 px. Ảnh `office-light.png`, `office-dark.png`, `office-approved-animations.png`, `office-1366.png`, `mobile.png` ghi rõ dữ liệu mô phỏng. Đã xem ảnh cuối nền sáng, nền tối, nhập nhiều phiếu thật và mobile, kiểm tra hình/giờ/nội dung/ô đầy đủ. Không tuyên bố mọi số lượng/nội dung tùy ý đều vừa một màn hình.

### Phạm vi, cleanup và handoff

Manifest trước 3116 file, sau 3117 (thêm báo cáo này), 5 file thay đổi đúng allowlist, 0 file ngoài phạm vi. HEAD/base/expected parent và branch không đổi. Revision 1/2, visualization đã duyệt và dirty changes trước task được giữ nguyên. `source-before.csv`, `source-after.csv`, `source-changes.csv`, `browser-artifacts.csv` lưu SHA256. Không commit/push/PR/merge/đổi branch/deploy, migration/schema hay writes business/config/quyền vào GaoAppDb hiện tại.

SQL inventory trước/sau giống nhau: hai database cũ `GaoApp_R2_InventoryPosting_7DB7C912679549C18B6D5BE610DA83A9` và `GaoApp_R2_InventoryPosting_D97ADC6A25BF48299F630D2183FDA18C` còn nguyên, fixture mới tự dọn. Không xóa database/service có sẵn. Màu/nhịp 5 hiệu ứng và trạng thái dựa trên dữ liệu thật đã được local validation; chưa có independent review/CI/thiết bị vật lý. Cần Web chạy bản build mới để dùng toàn bộ thay đổi; chưa restart/deploy Web hiện tại.

Giới hạn giữ nguyên: presence là tương tác phần mềm, không chứng minh thao tác vật lý; gửi lệnh in không khẳng định máy in đã in; giao hàng chưa kết nối; một Web instance/bộ nhớ tạm; các giới hạn registry và khả năng cuộn toàn trang khi rất đông công việc. Reviewer tập trung timestamp ordering, check replay riêng từng ô, giữ animation qua snapshot và dark/compact bounds. Rollback đối chiếu riêng 3 source + docs với `source-backup/` đầu revision và bỏ báo cáo mới nếu cần, giữ các chỉnh sửa sau đó/dirty tree trước task; không reset toàn repository. Bàn giao **READY FOR COORDINATOR REVIEW**.
