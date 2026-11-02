# UI-16 — Hóa đơn và tích hợp hóa đơn trên điện thoại

Ngày 27/10/2026. Đã triển khai và hoàn tất kiểm tra tự động UI-16a/b/c/d/e/f. Còn nghiệm thu thiết bị thật và các điểm chờ lịch sử. Bước tiếp theo: UI-17a — tra cứu ACB ở trang riêng và nhúng.

## Giao diện đã triển khai

- **UI-16a:** danh sách, dashboard và chi tiết hóa đơn. Bộ lọc bổ sung thu gọn trên điện thoại; chỉ số và bảng gọn trong khung; điều hướng nội trang đến tổng quan, người mua, dòng từ đơn và dòng thêm tay. Giá trị và trạng thái do owner hiện có cung cấp.
- **UI-16b:** modal dòng thủ công, nháp từ đơn và lịch sử sửa. Đổi cách trình bày các bảng/modal có sẵn; giữ trường, validation, tổng tiền và lệnh ghi gốc. Biểu mẫu dài có thân cuộn, footer và nút đóng trong khung.
- **UI-16c:** biểu mẫu thay thế/điều chỉnh hóa đơn và tồn chứng từ từ hóa đơn đầu vào. Giữ điều kiện hóa đơn đã phát hành, loại điều chỉnh, chứng từ, validation và quyền ADMIN gốc. Bảng HTML gốc của tồn chứng từ hiển thị theo thẻ có nhãn; luồng đối chiếu/bổ sung chứng từ vẫn xuất hiện theo kiểm tra quyền hiện có. Tồn chứng từ và tồn vật lý được giữ riêng theo nghiệp vụ nguồn.
- **UI-16d:** dashboard Viettel, cấu hình nhà cung cấp, biểu mẫu tạo/sửa, payload đã làm sạch và màn đồng bộ. Điều hướng nội trang biểu mẫu dùng các nhóm cấu hình có sẵn; mật khẩu, token, nội dung đã che, điều kiện kết nối và các lệnh giữ owner gốc.
- **UI-16e:** hóa đơn tự động. Tab, bộ lọc, danh sách hàng đợi/lịch sử/sự cố và cấu hình dùng dữ liệu, quyền và điều kiện gốc. Lý do chờ/lỗi hiển thị cùng dữ liệu nguồn; không phát hành để kiểm tra bố cục.
- **UI-16f:** danh sách, chi tiết và xem trước dọn nhật ký tích hợp. Nội dung request/response vẫn được sanitizer gốc che và Razor escape; khối mã có thể cuộn. Không thêm đường tắt phát hành, thử lại hoặc dọn log.

Mười bốn view cha nạp ba cặp `invoice-workspace.mobile`, `invoice-operations.mobile` và `invoice-integration.mobile`. CSS chỉ áp dụng cho `screen` dưới 768px. Trường nhập 16px, vùng chạm từ 44px, màu navy/lam, safe-area, reduced motion; modal/dialog có phần thân cuộn và nút đóng/footer trong khung. Adapter ghi nhận thuộc tính gốc, khôi phục khi về desktop hoặc trước in và chặn observer dựng lại trong lúc in. Các bộ lọc được đưa về vị trí gốc, điều hướng nội trang điện thoại được bỏ trước in.

Nút xanh lá đang disabled trong nhóm tác vụ Viettel trên điện thoại dùng chữ xanh đậm trên nền xanh nhạt để đọc rõ. Thuộc tính disabled và điều kiện cho phép tác vụ giữ nguyên; chỉnh màu không bật lệnh.

## Hai sửa hiển thị/serialize đã được tách rõ

**Mười ba view cha chỉ thêm import.** Riêng `InvoiceDashboard/Index` còn sửa đúng một dòng hiển thị khoảng ngày: biểu thức `@d.FromDate:dd/MM/yyyy` / `@d.ToDate:dd/MM/yyyy` được đổi thành gọi `ToString("dd/MM/yyyy")`. Ngày nguồn, bộ lọc và khoảng truy vấn không đổi; ngày hiển thị đúng ở cả điện thoại và máy tính, bố cục desktop giữ nguyên.

Trong 11 partial của Invoice, 10 file giữ nguyên từng byte. `_ManualLinesTable.cshtml` chỉ sửa ba `data-manual-quantity`, `data-manual-unit-price`, `data-manual-vat-rate` sang `InvariantCulture`. Bằng chứng trước sửa ghi nhận chuỗi vi-VN `1,25`, `8000,00`, `8,00` được JavaScript gốc đặt vào input number rồi thành rỗng, dẫn đến `Number(input.value || 0)` bằng 0. Serialize bất biến giúp editor gốc đọc đúng số nguồn. Không đổi giá trị trường, JavaScript nghiệp vụ, giá, số lượng, thuế suất, công thức hoặc chính sách. Không mô tả toàn bộ 15 file Razor là imports-only.

## Quyền và dữ liệu

Menu tiếp tục lấy từ `AdminMenuItems` của cửa hàng theo quyền, trạng thái và thứ tự đã cấu hình. Không thêm menu/ghim, seeder, migration, schema, endpoint hoặc quyền mặc định. Controller/service/repository/DTO, JavaScript/CSS nghiệp vụ gốc và cấu hình cửa hàng giữ nguyên.

`InvoiceCorrection/Create` yêu cầu role ADMIN; quyền wildcard không thay thế role. `InvoiceInputStock` dùng `Inventory.Transaction.View`. `AutoInvoice/Index` yêu cầu `System.AutoInvoice.Operate`; tab settings yêu cầu thêm `System.AutoInvoice.Settings`. Nhật ký dùng `System.Integration.View`; dọn log yêu cầu thêm `System.Integration.Manage`. Điều kiện ẩn/disabled, antiforgery, RowVersion, khóa hóa đơn/kho, phát hành, giá/thuế, thông tin nhà cung cấp và masking giữ nguồn.

Adapter chỉ sở hữu trình bày; không fetch/storage, clone form, ghi giá trị trường, tính số tiền/thuế/tồn hoặc chuyển tiếp submit/click. Các liên kết nội trang trỏ đến chính phần có sẵn; không tạo lệnh hay liên kết chức năng giả. GET trang Cleanup dùng `DryRun=true`; cả nút xem trước và thực thi vẫn là POST gốc và không nằm trong thao tác ghi nghiệm thu trình duyệt.

## Kiểm tra nguồn và tài nguyên

<!-- UI16_SOURCE_RESULTS_START -->
Release build 0 cảnh báo/0 lỗi; sáu asset gzip khớp từng byte nguồn. Snapshot 3.566 đường dẫn, 3.548 file bảo vệ, 24 nguồn nghiệm thu. Mười ba view cha imports-only; một view thêm một dòng định dạng ngày; một partial sửa ba serialize số. Mười partial Invoice còn lại giữ nguyên. Baseline 560 file: 425 không đổi / 135 đã đổi / 0 mất. Ba CSS đều chỉ screen dưới 768px; ba adapter và test JavaScript qua kiểm tra cú pháp. Source hash được kiểm tra lại trước khi chốt.
<!-- UI16_SOURCE_RESULTS_END -->

## Kiểm thử và đánh giá

<!-- UI16_RUNTIME_RESULTS_START -->
- **727/727 test .NET duy nhất qua**, 0 lỗi/0 bỏ qua; 731 lượt thực thi. Gộp theo testId và tên đầy đủ; exact TestRun copy không cộng trùng. 4 lượt lỗi lịch sử được lưu, đúng case có lượt cuối cùng qua.
- **31 trạng thái, 217 lượt điện thoại, 62 đối chiếu desktop, 31 đối chiếu in và 21 lượt hộp thoại (7 loại).** Phone 320×568,360×780,390×844,430×932,600×900,767×900,390×440; desktop 768/1440px. Không tràn ngang, ID trùng, field/form drift hoặc ghi do resize/in; computed CSS/hình học desktop và in khớp khi tắt stylesheet mới.
- Native browser mở tìm/preview đơn để lập nháp rồi hủy, editor dòng 1,25 × 8.000/VAT 8% đọc đúng tổng 10.800; modal dòng thủ công/người mua chỉ đổi bản nháp rồi hủy. Hóa đơn đã phát hành giữ khóa; biểu mẫu correction không gửi. Tám tab AutoInvoice giữ paused, không toggle/run/recheck/save.
- Tồn chứng từ có đầu kỳ 20 − đã phát hành 2 = còn 18; lượng sẵn có trừ held theo quy tắc nguồn. Hai preview dòng tồn chứng từ/sổ biến động mở và đóng; tồn vật lý InventoryBalance 100 là dữ liệu riêng và giữ nguyên. Backfill không xác nhận.
- Log đã che/escape, không lộ credential canary; provider Edit trả password rỗng. Payload local/issued dùng GET thuần, PDF remote vẫn lazy; không provider network, phát hành, email, retry/reprocess, sync, lưu settings hoặc cleanup POST. Đọc với quyền hạn chế được giữ; các route quản trị/correction bị máy chủ từ chối đúng quyền.
- API native chỉ chuẩn bị draft thuộc fixture trước fingerprint. Sau thao tác đọc/bản nháp/hủy, toàn bộ hóa đơn, khóa, đơn, tồn chứng từ/vật lý, cấu hình nhà cung cấp, credentials, raw log, menu và quyền giữ fingerprint. Browser chỉ cho đúng heartbeat registry gốc ngoài GET/HEAD/OPTIONS. SQL/Web GUID và Chrome headless/profile mới thuộc fixture; Chrome đang mở được giữ nguyên.
- CLI invoice-input-stock gốc qua đủ bốn marker: lượt đầu Razor/API SQL thật trên fixture rỗng, các kiểm tra phân trang/dữ liệu nhiều dòng/preview/filter/holds/retry sau đó dùng response tổng hợp bị chặn bắt theo CLI gốc. SQL 220k riêng qua test Large_legacy_import_is_consumed_once_and_both_tabs_reuse_the_snapshot, gồm giới hạn cold <15s/warm <1s của test và không phát sinh InventoryTransactions; không suy diễn thời gian đo chính xác.
- Lượt acceptance đầu thất bại tại FullApplicationFixture vì Web startup timeout, trước khi vào assertion giao diện; TRX và acceptance-startup-timeout.log được lưu nguyên. Root ghi nhận quá tải do nhiều Web fixture khởi động đồng thời, dừng đúng runner sở hữu và chạy lại tuần tự. Lượt broad đầu bị dừng chưa sinh TRX hoàn tất: chỉ ghi sự kiện, không gán số test/pass/fail hay cộng vào executions. Các lượt có TRX vẫn được merge theo testId/tên, giữ lỗi lịch sử và kết quả cuối cùng.
- Fixture chuẩn bị draft qua API gốc và gọi GET ViettelPayload thuần trước fingerprint để owner gốc bảo vệ mật khẩu legacy thành gdp:v1; xác nhận storage đã protected và HTML không có canary. Mật khẩu đã protected trước ranh giới chỉ đọc, raw log chứa canary vẫn giữ nguyên trong storage. Fingerprint sau đó tiếp tục bao toàn bộ RowVersion và UserInStores. Ba lượt lỗi route-fixture/log-assertion/legacy-credential-fixture được lưu TRX/log riêng; sửa đúng URL test, đọc redacted:true theo contract gốc và chuẩn bị credential trước boundary, không nới kiểm tra dữ liệu hay đổi nghiệp vụ.
<!-- UI16_RUNTIME_RESULTS_END -->

<!-- UI16_VISUAL_RESULTS_START -->
Root đã xem 22 ảnh cuối. Đã xem 22 ảnh cuối bao đủ 14 view cha cùng dòng hàng, editor và modal native. Màu navy/lam, nhóm bộ lọc, thẻ có nhãn và hành động đọc rõ; trường dài xuống dòng, footer/nút đóng nằm trong khung. Dòng 1,25 × 8.000, VAT 8% và tổng 10.800 giữ nguồn; tồn chứng từ 18 được tách rõ với tồn vật lý 100 trong nghiệm thu SQL. Nhãn hóa đơn đã phát hành tăng tương phản trên điện thoại và vẫn disabled. Nhật ký dùng JSON redacted:true, provider Edit không trả mật khẩu; ảnh không có credential canary. Thanh Menu chỉ có mục nguồn của fixture; không tạo ghim/menu giả. AutoInvoice đang tạm ngưng, dữ liệu hàng đợi rỗng được ghi nhận, không nghiệm thu phát hành thật. Ảnh full-page có thể ghi thanh Menu cố định giữa ảnh dài; viewport và cuộn được kiểm tra riêng. Chưa thay kiểm tra Android/iOS và thiết bị thật.

Android/iOS thật, bàn phím, safe-area, zoom, xoay màn hình, một tay, camera/máy quét và in phần cứng vẫn chờ nghiệm thu. Mô phỏng viewport/in không thay thiết bị thật. Các điểm chờ lịch sử UI-14/UI-15 giữ nguyên.
<!-- UI16_VISUAL_RESULTS_END -->

## Bằng chứng

Snapshot và script tại `.artifacts/ui-16`. Kết quả sau khi chạy: [nguồn](mobile-ui-20261025/evidence/ui-16/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-16/baseline-delta.json), [asset nén](mobile-ui-20261025/evidence/ui-16/asset-bundle.json). [runtime](mobile-ui-20261025/evidence/ui-16/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-16/acceptance-matrix.json), [rà ảnh](mobile-ui-20261025/evidence/ui-16/visual-inspection.json). TRX/ảnh/SQL tại TestResults/mobile-ui-16-20261027. Tiếp **UI-17a — tra cứu ACB ở trang riêng và nhúng**.
