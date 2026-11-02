# UI-18 — Báo cáo quản trị trên điện thoại

Ngày 27/10/2026. Đã triển khai toàn bộ UI-18a/b/c/d/e/f/g và qua tự động trên paired snapshot + nguồn UI-17/UI-18. Build mặc định workspace đã qua checkpoint; end-to-end workspace/P3 và thiết bị thật còn chờ. Tiếp UI-19a — danh sách và chỉnh chương trình khuyến mãi.

## Phạm vi giao diện

- **UI-18a:** bộ lọc kỳ, so sánh và phạm vi dùng các control, query và validation gốc; chọn nhanh, khoảng ngày, quầy bán và trạng thái khách hàng được bố trí lại trên điện thoại.
- **UI-18b:** tổng quan quản trị và nhánh chỉ có quyền bán hàng dùng mẫu navy/lam; KPI, biểu đồ, diễn giải và chi tiết giữ dữ liệu, cohort và drilldown của owner gốc.
- **UI-18c:** kết quả kinh doanh, chi tiết lợi nhuận và điều chỉnh giá vốn giữ phân biệt thiếu dữ liệu, tạm tính, giá trị âm và không khả dụng; adapter không tính lại số liệu.
- **UI-18d:** sản phẩm, nhóm sản phẩm, khách sỉ/lẻ và khách hàng có bộ lọc, thẻ dữ liệu có nhãn và phân trang phù hợp điện thoại. Bảng số liệu tương đương biểu đồ và details gốc vẫn là nguồn nội dung.
- **UI-18e:** thu chi, tồn kho, công nợ và các hộp thoại ngân quỹ được trình bày lại; số tồn vật lý, định giá, nguồn tiền, tuổi nợ và khoản chưa thanh toán giữ nghĩa gốc.
- **UI-18f:** nhập/sửa nháp, ghi nhận và hủy chi phí dùng biểu mẫu gốc. Hộp thoại có thân cuộn, đầu/cuối trong khung, cỡ trường 16px và vùng chạm từ 44px; giữ ngày ghi nhận, lý do, row version, validation và quyền thao tác.
- **UI-18g:** xuất CSV, rỗng, lỗi, đang tải/chậm và giới hạn dữ liệu tiếp tục do native handler quyết định. Không mở panel chưa tải hoặc gọi API để tạo thẻ trình bày.

Bảy view cha chỉ thêm hai import mỗi view, dùng ba cặp asset `reports-workspace.mobile`, `reports-operations.mobile` và `report-detail.mobile`. Partial `_ManagementReportWorkspace` không sửa. CSS chỉ áp dụng `screen` dưới 768px; adapter trả lại thuộc tính và vị trí node gốc trước in và khi về desktop. Mọi trường, form, trạng thái disabled/hidden và details mở/đóng vẫn thuộc native owner.

## Quyền, menu và cấu hình

Menu vẫn lấy từ `AdminMenuItems` của cửa hàng theo quyền, trạng thái và thứ tự đã cấu hình. UI-18 không tạo menu, ghim, seeder, migration, schema, endpoint hoặc quyền mới. Adapter không ghi control value, lưu cấu hình, gửi yêu cầu mạng, chuyển tiếp submit/click hoặc thực hiện lệnh tài chính.

Báo cáo quản trị/lợi nhuận/vận hành cần `Report.Profit.View`; xuất thêm `Report.Profit.Export`. Tổng quan bán hàng cần `Report.Sales.View`, chỉ dùng nhánh quản trị khi có `Report.Profit.View`. Tạo/sửa chi phí cần thêm `Report.Expenses.Manage`; ghi nhận/hủy cần `Report.Expenses.Confirm`. Lệnh thu chi, số dư đầu kỳ, đảo phiếu và cập nhật hạn trả cần `Report.Treasury.Manage`. Giữ nguyên kiểm tra controller/service và phạm vi cửa hàng.

## Ranh giới nguồn và build

<!-- UI18_SOURCE_RESULTS_START -->
Source review cuối22 qua: snapshot 3643; scope bảo vệ 3629, trong đó 3620 file trùng byte tại thời điểm ghi và 9 drift P3 có bản quan sát/hash/diff. Ngoài observational scope có 3585 file được kiểm tra nghiêm; unknown drift vẫn làm gate thất bại. 22 nguồn UI-18 (7 view, 6 asset mobile, 2 native JS và 7 nguồn kiểm thử); 7 cha imports-only/BOM và shared partial nguyên vẹn. Ba CSS chỉ screen dưới 768px; 3 adapter, 2 native JS và 6 CJS qua syntax. Baseline 560:407 không đổi/153 đã đổi/0 mất; mới 7 view và 2 native asset khác baseline. Giữ nguyên 42 verification và mọi nguồn/báo cáo/evidence UI-17.

Sửa lỗi terminalId bị mất khi select ban đầu chưa có option: giữ query đến options có thẩm quyền rồi gán trước quality/export/render; không tạo option giả, chọn Tất cả/quầy khác vẫn có hiệu lực. Chỉ 5 replacement của native Management JS được phép thay.

Sửa lỗi giờ chi tiết Sales: CompletedAtLocal từ DTO đã là giờ địa phương nhưng formatter cũ coi là UTC rồi cộng thêm 7 giờ. Một formatter local rõ ràng và 4 callsite orders/discounts/returns/void được sửa, đúng 5 replacement; mọi byte khác/BOM/newline giữ nguyên. Dữ liệu UTC/local, DTO/service/repository, chính sách kỳ, phép tính tài chính và bố cục desktop không đổi; chỉ nội dung giờ được hiển thị đúng.

Hai legacy browser script đổi đúng một click mỗi file từ tọa độ overlay lỗi thời sang native [data-nav-close]; các assertion/timeouts/financial flows còn lại nguyên byte. Không sửa menu sản phẩm; có proof before/after/diff và nguồn UI-02.

Paired build 0 cảnh báo/0 lỗi; 6 gzip mobile +2 gzip native giải nén khớp bytes nguồn cuối22. UI-18 end-to-end xác minh paired snapshot trước P3 + 19 nguồn UI-17 + 22 nguồn UI-18 (7 view, 6 asset mobile, 2 native JS sửa giữ terminal/giờ local, 7 nguồn kiểm thử). Build mặc định của workspace hiện tại đã qua tại checkpoint riêng: 1 cảnh báo/0 lỗi; initial CS0029/CS1662 đã được xử lý ngoài UI-18. Probe runtime riêng của workspace hiện tại thất bại ở EF PendingModelChangesWarning trước financial/UI assertions; nằm ngoài counts UI-18. End-to-end toàn bộ workspace và InvoiceNotifications P3 đồng thời chưa được xác minh. Paired build dùng 12 bản compile gốc bất biến trước P3 (7 đã dùng ở UI-17 và 5 privacy/audit/security/startup), supplement Category.CS và Test SDK Main gốc. Actual Before-CoreCompile TSV, SDK/content, 6 gzip mobile và 2 gzip native được kiểm riêng; không suppress cảnh báo EF migration, không ghi đè file hiện tại ngoài UI-18.

Drift hiện tại chỉ là quan sát, không bị khóa hoặc nhận là phần việc UI-18. Số current drift khác 12 compile copies trước P3. Giữ log default build CS0029/CS1662 lịch sử đã xử lý, checkpoint mới 1 xUnit2031/0 lỗi, stale gzip trước SDK regenerate và probe current EF riêng.
<!-- UI18_SOURCE_RESULTS_END -->

## Kiểm thử và đánh giá

<!-- UI18_RUNTIME_RESULTS_START -->
- **150/150 test .NET duy nhất qua**, 0 lỗi/0 bỏ qua cuối; 312 lượt thực thi. Giữ 4 lượt lỗi trước đã có đúng testcase cuối qua, không cộng rerun vào unique. Các vòng regression, subset, diagnostic và full được phân loại bằng TRX.
- **16/16 Node VM qua:** 8 terminal scope chạy toàn native script; 8 giờ local chạy formatter và 4 renderer thật qua 4 host timezone, trên LIVE source mặc định. Không mượn kết quả candidate.
- **Ba probe gốc qua:** management-reports, operations-reports và management-report-loading qua fact gốc. Hai CLI dùng fixture SQL viết riêng; giữ 4 log lỗi trước theo hash. Lỗi testhost IPC trước testcase được lưu riêng và không cộng vào unique/execution; chạy lại với quyền phù hợp qua, không tăng timeout.
- **39 trạng thái/280 lượt điện thoại/80 desktop/40 in/33 lượt hộp thoại (11 loại)** gồm 40 lượt thực thi: giữ full39 raw (273 phone/78 desktop/39 in); focused Sales cuối22 thêm 7 phone/2 desktop/1 in và thay case sales-viewer-native-overview. Full và focused đều mandatoryMatrixExecuted=true, dùng standard compression. Không tràn trang, ID trùng hoặc đổi field/form/value/disabled/readonly/validation/details/hidden vì trình bày; desktop768/1440 và in khớp computed CSS/hình học.
- Full20 đã qua39/273/78/39/33 trước khi phát hiện giờ local được lưu nguyên archive pre-local-time-correction-pass, chỉ là lịch sử. Full22 raw gồm39 trạng thái được giữ nguyên. Sau 5 rule CSS chỉ ảnh hưởng Sales fallback, 38 case không đổi được giữ theo proof delta nguồn; case Sales được thay bằng focused Sales trên nguồn cuối22, đủ7 phone/2 desktop/1 in và tương phản chữ tối thiểu 6.91:1. Không nhận là đã chạy lại toàn39 sau thay đổi tương phản, không lấy kết quả20 làm nghiệm thu cuối.
- Diagnostic cuối kiểm 39 native flow +SQL với mandatoryMatrixExecuted=false; không tính vào matrix. Cả diagnostic22 và full22 có đủ4 dateTimeProofs, zero mismatches: cell local so với DTO AtLocal và UTC gốc. Orders có hàng SQL thật; các segment native rỗng ghi rõ và nhánh renderer có VM coverage.
- Hai terminal khác nhau: doanh thu native quầy 252000, toàn cửa hàng 280000; request/select/URL/nav/quay lại giữ scope. Cảnh báo chi phí toàn cửa hàng và operatingProfit không phân bổ vẫn đúng. Financial payload/scalar rows/row versions, menu/quyền/cấu hình không đổi; fixture 4 đơn, tồn 139, payment 0, chi phí ghi nhận 3000. Có 10 CSV gốc và 7 GET thiếu quyền403.
- 11 native dialog chỉ soạn rồi hủy, không submit tài chính hoặc gọi provider ngoài fixture. Giữ các lỗi CSV assertion/scope retention/response race/stale gzip và lịch sử sửa giờ; native loading probe vẫn kiểm overview compact, timeout/retry, HTTP trace, non-JSON/late response. Giữ toàn bộ cửa sổ/tab Chrome đang mở.
<!-- UI18_RUNTIME_RESULTS_END -->

<!-- UI18_VISUAL_RESULTS_START -->
Root đã xem 15 ảnh cuối. Đã xem 15 ảnh thực của mã UI-18: đủ 7 view cha, các thẻ dữ liệu và hộp chi phí/sổ quỹ. Bố cục không tràn ngang; số liệu, nhãn thiếu nguồn, tuổi nợ, trạng thái và nút theo quyền được giữ. Giờ hoàn tất đơn hiển thị đúng một lần theo Việt Nam. Thẻ KPI Sales-only đã được sửa tương phản và ảnh kiểm tra riêng thay thế đúng case đó trong ma trận 39; 38 case khác không chịu tác động của 5 rule CSS được giới hạn ở thẻ primary của root Sales-only. Chưa nghiệm thu Android/iOS thật, bàn phím thực, safe-area/zoom hoặc in phần cứng.

Còn Android/iOS thật, bàn phím/safe-area/zoom/xoay/một tay và in phần cứng. UI-18 end-to-end xác minh paired snapshot trước P3 + 19 nguồn UI-17 + 22 nguồn UI-18 (7 view, 6 asset mobile, 2 native JS sửa giữ terminal/giờ local, 7 nguồn kiểm thử). Build mặc định của workspace hiện tại đã qua tại checkpoint riêng: 1 cảnh báo/0 lỗi; initial CS0029/CS1662 đã được xử lý ngoài UI-18. Probe runtime riêng của workspace hiện tại thất bại ở EF PendingModelChangesWarning trước financial/UI assertions; nằm ngoài counts UI-18. End-to-end toàn bộ workspace và InvoiceNotifications P3 đồng thời chưa được xác minh. Paired build dùng 12 bản compile gốc bất biến trước P3 (7 đã dùng ở UI-17 và 5 privacy/audit/security/startup), supplement Category.CS và Test SDK Main gốc. Actual Before-CoreCompile TSV, SDK/content, 6 gzip mobile và 2 gzip native được kiểm riêng; không suppress cảnh báo EF migration, không ghi đè file hiện tại ngoài UI-18.
<!-- UI18_VISUAL_RESULTS_END -->

## Bằng chứng

Script và snapshot tại `.artifacts/ui-18`. [Nguồn](mobile-ui-20261025/evidence/ui-18/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-18/baseline-delta.json), [asset nén](mobile-ui-20261025/evidence/ui-18/asset-bundle.json), [paired build](mobile-ui-20261025/evidence/ui-18/isolated-build-proof.json), [checkpoint workspace](mobile-ui-20261025/evidence/ui-18/current-workspace-checkpoint.json), [runtime](mobile-ui-20261025/evidence/ui-18/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-18/acceptance-matrix.json), [ảnh](mobile-ui-20261025/evidence/ui-18/visual-inspection.json). TRX/ảnh/SQL tại `TestResults/mobile-ui-18-20261027`. Tiếp **UI-19a — danh sách và chỉnh chương trình khuyến mãi**.
