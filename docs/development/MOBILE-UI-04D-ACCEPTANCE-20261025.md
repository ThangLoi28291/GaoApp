# UI-04d — Kiểm thử và đánh giá danh sách/chi tiết đơn POS

Ngày 25/10/2026. Hoàn tất phần kiểm thử tự động của UI-04; bước triển khai tiếp theo **UI-05a — danh sách phiếu nhập tại cửa hàng**. Nghiệm thu Android/iOS thật vẫn tách riêng.

## Lỗi đã sửa trên điện thoại

1. Mã voucher dài làm trang 390 px rộng tới 465 px. Bổ sung ngắt dòng ở khối voucher trong media dưới 768 px; thử đủ ba mã dài ở 320/390 px.
2. Form hậu mãi đọc `alreadyRefunded` trong khi API eligibility trả `refundedTotal`, có thể hiện 0 dù đơn đã hoàn. Chỉ thêm marker hiển thị; adapter phone lấy text “Đã hoàn” từ receipt đúng ID đơn đã render, không tính lại tiền hoặc sửa payload/form. Khi đổi về desktop, khôi phục text cũ như UI-04c. Bài test dùng hai lần hoàn 10 + 5, xác nhận phone hiện 15 và desktop giữ cách hiển thị cũ.
3. Lịch sử hậu mãi có dữ liệu thật bị các thẻ thống kê một cột đẩy danh sách xuống quá thấp. Chỉ trên phone, thống kê dùng hai cột và phần giải thích lặp được ẩn; giữ đủ số tiền, số phiếu và các bản ghi. Test xác nhận bản ghi đầu thấy được phía trên footer modal.

Không đổi giao diện máy tính, controller/service, quyền, cơ chế ghi giao dịch, phép tính tiền, menu/layout/viewport chung hoặc Chrome launcher. View và renderer chỉ thêm mỗi file một marker; CSS mới đều trong media phone. Adapter vẫn chỉ chuyển node và text trình bày, không gọi API/storage, đọc/sửa giá trị form hoặc tính tiền.

## Dữ liệu và tình huống đã chạy

Fixture SQL tạo chín trường hợp đọc: công nợ/thanh toán một phần có cọc; khách lẻ; ba voucher có đầy đủ mã; 50 dòng hàng với số lượng lẻ và tổng 3 tỷ; đơn nháp trống; đơn phân bổ hai HKD/hai kho/hai hóa đơn con; đơn giao chờ chốt; đơn giao đã hủy; đơn có hai lần hoàn. Dữ liệu tên/ghi chú chứa ký tự HTML được hiển thị dạng text, không chạy mã.

Các bản lưu thử được đọc qua API và Razor thật, so tổng tiền giữa API, phần tóm tắt và chi tiết giá, kiểm tra dữ liệu trên 320/390 px rồi trở về desktop 1440 px. Với công nợ/cọc, modal thanh toán giữ đúng tổng 2.350.000, đã trả 650.000, cọc 100.000 và còn thiếu 1.700.000. Hồ sơ giao nguồn giữ giá trị dự kiến và không hiện thu tiền/trả hàng như hóa đơn bán đã chốt.

Khối HKD có đủ allocation và hóa đơn SQL: người đủ quyền thấy hai hóa đơn; chỉ có quyền đối soát thấy khối với “Chỉ xem” và không có link mở hóa đơn; chỉ xem đơn không nhận markup HKD. Chín đơn đều bị chặn khi truy cập từ cửa hàng khác. Các nhóm quyền, menu hiện có và cấu hình database thử không bị adapter thay đổi.

Đã thử thêm phóng chữ 200%, forced colors và reduced motion bằng Chrome; giữ bộ lọc khi quay lại danh sách; lỗi 403 tải lịch sử đọc được và đóng được; lỗi 503/đóng trong lúc tải, dữ liệu nhập khi resize và bảy tổ hợp quyền được chạy lại từ UI-04c. Đây là mô phỏng trình duyệt, không thay nghiệm thu thiết bị thật hoặc bfcache được bảo đảm kích hoạt.

## Kiểm chứng ghi giao dịch

Một đơn riêng được tạo bằng API POS thật: 2 sản phẩm, đã thu 40. Browser mở form cũ, chọn chỉ hoàn tiền 10, nhập lý do và nhấp đúp Lưu. Chỉ có **một POST**, có CSRF, trả thành công; trang tự tải lại và lịch sử có một phiếu. SQL xác nhận đúng một phiếu hoàn/một khoản hoàn 10, số đã thu vẫn 40, còn có thể hoàn 30 và tổng hoàn của ca chỉ tăng 10.

Snapshot trước/sau giữ nguyên các đơn đọc, dòng bán, khoản thu, lịch sử cũ, voucher, allocation, hóa đơn, số dư tồn và menu. Chỉ đơn/phiếu hoàn chuyên biệt được phép thay đổi. Browser không gọi endpoint hoàn ngoài đơn fixture này. Không giả lập phản hồi thành công của lệnh ghi.

## Kết quả

- Hai build Release Web/Razor/Tests/runner: **0 lỗi, 0 cảnh báo**; build sau có hai marker hiển thị mới.
- **25/25** hồi quy: 4 trả/hoàn SQL, 7 đặt cọc SQL, 2 quyền/tích điểm SQL, 4 đối soát HKD, 5 Razor đơn giao, 1 danh sách đơn giao hợp nhất và 2 CSP.
- Chạy lại **7/7 Razor/CSP trên build cuối** sau thêm marker hiển thị cũng qua; đây là kiểm tra lặp, không tính thành 7 bài khác.
- Probe dữ liệu UI-04d: **16 nhóm**, chín trường hợp SQL, quyền HKD/cửa hàng, chữ lớn/màu tương phản, lịch sử, Back và lệnh hoàn thật.
- Probe chi tiết/ngữ cảnh UI-04b/c: **26 nhóm**, 10 viewport, bảy tổ hợp quyền, ảnh desktop 1440 px khớp, node/thứ tự/text desktop khôi phục.
- Probe danh sách UI-04a: qua 10 viewport và toàn bộ lọc/lookup/phân trang/URL/preview/print/ACB/lỗi/thử lại/escaping/stale response hiện có.
- Không có lỗi JS/CSP ở ba probe. Hai probe hồi quy không có lệnh tài chính; probe UI-04d chỉ có một lệnh hoàn fixture được kiểm chứng ở trên.
- **45 file bảo vệ giữ hash**, ba đối chiếu source. Baseline 560 file: **523 không đổi, 37 đổi, 0 mất**, cộng dồn; không sửa baseline gốc.

Đã xem ảnh voucher/HKD khi mở đầy đủ, tiền cọc/công nợ, lịch sử nhiều lần hoàn, số đã hoàn và kết quả ghi. Các lỗi tràn ngang và số đã hoàn được kiểm tra lại; lịch sử gọn hơn và thấy bản ghi đầu sớm hơn.

Tất cả Web/SQL GUID/Chrome headless đều thuộc fixture, launcher Chrome thường tắt trong môi trường test. Không đổi database cửa hàng, tạo menu/migration ứng dụng hoặc đóng các cửa sổ/tab Chrome đang mở.

## Phần nghiệm thu còn riêng

Android/iOS thật: bàn phím ảo, safe area, thao tác chạm và chữ lớn; không có thiết bị thật kết nối trong lượt này. Browser chỉ thực hiện ghi hoàn tiền không kèm nhận hàng; nghiệp vụ cọc, trả/nhập kho và lặp được kiểm tra qua bộ SQL, chưa thay việc nghiệm thu toàn luồng trên máy thật. Bộ ma trận menu/ghim và F5/Stop UI-02/UI-03 vẫn giữ trạng thái riêng. Chưa đánh dấu toàn bộ dự án hoặc nghiệm thu thiết bị đã hoàn tất.

## Bằng chứng

- [Runtime review](mobile-ui-20261025/evidence/ui-04d/runtime-review.json), [source review](mobile-ui-20261025/evidence/ui-04d/static-review.json), [ma trận nghiệm thu](mobile-ui-20261025/evidence/ui-04d/acceptance-matrix.json).
- [25 bài hồi quy](../../TestResults/mobile-ui-04d-20261025/regression/regression.trx), [probe dữ liệu](../../TestResults/mobile-ui-04d-20261025/acceptance-final/result.json), [probe chi tiết](../../TestResults/mobile-ui-04d-20261025/contextual-final/result.json), [probe danh sách](../../TestResults/mobile-ui-04d-20261025/orders/result.json).
- [Voucher dài 320 px](../../TestResults/mobile-ui-04d-20261025/acceptance-final/vouchers-expanded-320.png), [HKD 320 px](../../TestResults/mobile-ui-04d-20261025/acceptance-final/hkd-expanded-320.png), [cọc/công nợ](../../TestResults/mobile-ui-04d-20261025/acceptance-final/partial-credit-payments-320.png), [đã hoàn 15](../../TestResults/mobile-ui-04d-20261025/acceptance-final/refunded-summary-390.png), [hoàn thật qua browser](../../TestResults/mobile-ui-04d-20261025/acceptance-final/successful-browser-refund-390.png).
