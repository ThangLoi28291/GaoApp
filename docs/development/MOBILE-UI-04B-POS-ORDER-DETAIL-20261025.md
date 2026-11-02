# UI-04b — Chi tiết đơn POS trên điện thoại

Ngày 25/10/2026. Áp dụng mẫu T02 đã duyệt vào `/admin/pos/order-detail/{orderId}`. Tiếp theo là **UI-04c**. Các bước trước và phần nghiệm thu thiết bị thật giữ trạng thái riêng.

## Giao diện đã thực hiện

Chỉ dưới 768 px: tóm tắt mã đơn, trạng thái, khách hàng và tổng tiền ở đầu; ba thẻ **Hàng hóa / Thanh toán / Lịch sử** chia phần nội dung. Dòng hàng chuyển thành thẻ, giữ tên, đơn vị, SKU/barcode, lượng, giá gốc, giảm giá và thành tiền từ Razor hiện có. Tổng tiền không được tính lại trong JavaScript.

Thanh Quay lại / In hóa đơn dùng hai liên kết gốc, nằm trên menu cửa hàng và ẩn khi mở hộp thoại/bàn phím. Các thao tác hậu mãi, hủy/hoàn và tiện ích cũ nằm trong “Thao tác đơn hàng”; giữ nguyên điều kiện Razor, địa chỉ, `data-csp-action` và handler. Không tạo luồng thu tiền mới. Hồ sơ giao hàng và phần HKD được mở riêng; máy chủ vẫn quyết định có trả dữ liệu HKD hay không.

Lịch sử thanh toán giữ modal và dữ liệu gốc, đổi thống kê thành hai cột, từng khoản thanh toán thành thẻ trên điện thoại. Bốn modal cũ giữ nguyên ID, submit, token, kiểm tra điều kiện, tải lịch sử và Bootstrap focus. Tab hỗ trợ phím trái/phải, Home/End; motion ngắn và tắt theo `prefers-reduced-motion`; có nhánh forced colors.

Từ 768 px, bố cục cũ được giữ. Adapter chuyển chính node gốc và khôi phục vị trí theo thứ tự ngược; không clone form/nút, không gọi API, lưu storage, sửa giá trị hoặc thêm quyền. Wrapper tiện ích dùng `display: contents` để không tạo hộp trên desktop. Các quy tắc trình bày còn lại trong media phone. CSS/JS detail cũ, controller/service/DTO, menu, layout/viewport chung và Chrome launcher không thay đổi.

## Kiểm thử đã chạy

- Build Release Web/Razor, Tests và browser runner: **0 lỗi, 0 cảnh báo**.
- **7/7** bài hồi quy: năm bài Razor/giá/chứng từ đơn giao và hai bài CSP.
- Chrome headless: **17 nhóm kiểm tra**, gồm 10 viewport 1600/1440/1366/1024/768/767/430/390/360/320 px, không tràn ngang; tóm tắt/tab ở trên thanh hành động; ba tab, bàn phím, nhóm tiền cọc/voucher/giảm giá, đơn trống/khách lẻ, đơn giao nguồn và bốn modal gốc. Hộp thoại async được chờ tải xong. Không có lỗi JS/CSP hoặc lệnh tài chính.
- Máy tính 1440 px: ảnh giống từng byte khi so với cùng màn hình đã bỏ CSS mobile và tháo wrapper không tạo hộp. Đối chiếu source xác nhận mọi biểu thức Razor, điều kiện, nội dung, ID và action cũ giữ nguyên. Resize khôi phục DOM và identity node; chỉ chuẩn hóa thuộc tính `style=""` vô hiệu mà thư viện cũ có thể để lại trên hidden CSRF input.
- Quyền qua Web/SQL thật: có/thiếu `Pos.Order.View`; tài khoản chỉ xem không nhận HTML HKD; cửa hàng khác nhận lỗi “Không tìm thấy đơn hàng” với mã **400 theo service hiện có**. Không đổi hợp đồng lỗi.
- Snapshot SQL trước/sau của Orders, OrderLines, OrderPayments và AdminMenuItems khớp. **34 file bảo vệ giữ hash**. Baseline 560 file: **524 không đổi, 36 đổi, 0 mất**, cộng dồn các giai đoạn; baseline gốc không sửa.

Chỉ chạy Web/database GUID dùng một lần và Chrome headless/profile riêng của fixture; tắt launcher Chrome thường trong môi trường test. Không đổi database cửa hàng, tạo menu/migration ứng dụng hoặc đóng cửa sổ/tab Chrome của người dùng.

## Đánh giá và giới hạn

Đã xem ảnh và sửa hai điểm: thu gọn khoảng trắng ở tóm tắt để hàng hóa xuất hiện sớm hơn, đổi lịch sử thanh toán khỏi bảng ngang/thống kê một cột sang thẻ. Tổng tiền nổi bật; chi tiết ít dùng mở riêng; các phần đã thử không mất dữ liệu hoặc thao tác khi đổi kích thước.

Đây là kiểm thử Chrome mô phỏng. Bàn phím ảo, safe area và phóng chữ trên Android/iOS thật còn nghiệm thu. Chưa thử toàn bộ tập allocation/hóa đơn HKD, mã voucher, đơn trả/thanh toán một phần và các tổ hợp dữ liệu lớn. Chưa xác nhận ghi giao dịch hủy/hoàn/trả: UI-04c/04d tiếp tục kiểm tra ngữ cảnh và nghiệp vụ gốc. Trong fixture, tên khách ở form hậu mãi hiện dùng fallback “Khách lẻ” từ payload eligibility cũ dù phần chi tiết có khách; ghi nhận để đối chiếu payload/renderer trong UI-04c, không sửa hợp đồng này ở UI-04b.

## Bằng chứng

- [Source và hash bảo vệ](mobile-ui-20261025/evidence/ui-04b/static-review.json), [runtime review](mobile-ui-20261025/evidence/ui-04b/runtime-review.json), [ma trận 12 ca](mobile-ui-20261025/evidence/ui-04b/acceptance-matrix.json).
- [Kết quả browser](../../TestResults/mobile-ui-04b-20261025/browser-final/result.json), [TRX hồi quy](../../TestResults/mobile-ui-04b-20261025/regression/regression.trx).
- [Ảnh điện thoại 390 px](../../TestResults/mobile-ui-04b-20261025/browser-final/detail-phone-390.png), [320 px](../../TestResults/mobile-ui-04b-20261025/browser-final/detail-phone-320.png), [desktop](../../TestResults/mobile-ui-04b-20261025/browser-final/detail-desktop-1440.png), [lịch sử thanh toán](../../TestResults/mobile-ui-04b-20261025/browser-final/detail-payment-history-390.png).
- Mã trình bày: `POSOrderDetail/Index.cshtml`, `pos-order-detail.mobile.css`, `pos.order.detail.mobile.js`. Test độc lập: `PosOrderDetailBrowserFixture.cs`, nhánh `--pos-order-detail` và `pos-order-detail.browser.cjs`.
