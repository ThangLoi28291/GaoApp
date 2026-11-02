# UI-06d — QR và đối chiếu chuyển khoản trên điện thoại

Ngày 26/10/2026. Đã triển khai và qua kiểm tra tự động; còn nghiệm thu điện thoại/ngân hàng thật. Bước tiếp UI-06e: offline, journal, phiên làm việc và giao hàng/màn khách.

## Giao diện

Áp dụng /admin/pos, /admin/pos/v3 và /admin/pos/legacy dưới 768px theo mẫu navy/blue đã duyệt. QR dùng toàn vùng nhìn thấy: header/nút đóng 44px, nội dung cuộn riêng, footer chứa chính nhóm tác vụ gốc. Hiển thị số tiền lớn, ảnh QR vuông giữ nguyên dữ liệu và vùng trắng, tài khoản/ngân hàng/chủ tài khoản/nội dung chuyển rõ ràng. History có nút tải/mở lại đủ cỡ chạm, dòng lịch sử và trạng thái chia khối. Không thêm thao tác xác nhận hoặc tạo menu.

Màn đối chiếu ngân hàng nhấn số tiền nhận và tên người chuyển từ bằng chứng ngân hàng; mã giao dịch/đơn/giờ/nội dung đầy đủ, chuỗi dài tự xuống dòng. Body cuộn độc lập; lỗi và hai quyết định gốc luôn ở footer. Ẩn nhãn phím tắt trên phone; vẫn giữ owner bàn phím hiện hữu. Motion dùng spinner hiện có và tôn trọng reduced motion.

Vùng paymentInlineErrorBox trước đây ở phía sau popup QR được dời nguyên node vào QR khi mở trên phone, trả về khi đóng hoặc chuyển desktop. Thu ngân thấy lỗi và thử lại mà không cần đóng QR. Các node tác vụ/ID/listener/disabled/focus được giữ, ≥768px trả đúng vị trí; không thêm handler thu tiền hoặc phép tính.

## Bảo toàn nghiệp vụ

Index chỉ thêm CSS/script. PosPayment chỉ đổi lookup canonical paymentInlineErrorBox từ paymentModalEl.querySelector sang document.getElementById: khi node dời vào QR, owner tái sử dụng đúng node thay vì tạo trùng ID. Phần còn lại module khớp snapshot. PosQrHistory, PosBankTransferReview, PosBankNotifications, PosAcb, PosOffline, Prime, render/app/controller/service/DTO/quyền/menu/config/migration không sửa. Adapter chỉ xử lý vị trí node và visualViewport; Bootstrap tiếp tục quản modal. Chế độ chờ/cảnh báo/thủ công/ACB/read-only vẫn theo server, không tự xác nhận đã nhận tiền từ việc tạo/hiển thị QR. Nút Kiểm tra ACB động do owner gốc quản lý.

Đóng QR, mở lại hoặc reload lấy cùng số tiền/ảnh/mã đã lưu. Tạo tiếp khi có QR chờ mở lại đúng QR đó, không thay bằng số nhập mới. Nút thủ công vẫn chịu thời gian chờ server. Hủy QR và xem QR đã kết thúc không tạo thanh toán. Quyền/store và menu AdminMenuItems giữ nguyên, không seeder/migration/menu mới.

## Kiểm tra và đánh giá

- Build Release: 0 lỗi/cảnh báo; 394/394 hồi quy POS/QR/history/ACB/notification/CSP/SQL chống thu trùng qua.
- 7 nhóm browser trên Razor/HTTP/LocalDB thật; Prime/legacy mỗi bản QR và review ở 320×568, 360×780, 390×844, 430×932, 600×900, 767×900, 390×440: 28 lượt. Không tràn ngang; body/footer trong màn; nút đóng/tác vụ tối thiểu 44px. Bốn visualViewport 360px mô phỏng giữ body và footer khả dụng.
- 5 đối chiếu mọi computed property và box của payment/history/QR/review ở Prime 768/1024/1440 và legacy 768/1440 khớp khi bật/tắt CSS mới. Resize trả nguyên node và vị trí desktop, không còn footer phone.
- QR local do server thật tạo: 31.000 vẫn đúng qua mở/đóng/reload và yêu cầu tạo mới 50.000; thử lại trả về QR chờ cũ. Thời gian chờ được tạo trong fixture SQL riêng; QR mới chưa đủ thời gian không có nút xác nhận thủ công.
- Máy chủ đã commit xác nhận 31.000 nhưng probe thay phản hồi bằng 409 thử lại; lỗi hiện trong QR. Thử lại cùng QR, giữ F9 lặp trong request chậm: đúng một khoản 31.000. Kiểm thêm đúng một ID vùng lỗi, nội dung 409 hiện tại, lỗi đã ẩn sau thành công và trên QR đã hủy. SQL đơn 131.000 vẫn Draft, còn thiếu 100.000; tồn 100 không giảm. QR 20.000 đã hủy và QR 100.000 chờ đều PaymentId null. QR đã hủy chỉ xem, không nút xác nhận/hủy/ACB.
- Fingerprint menu/store/quầy/giá/tùy chọn khách và dữ liệu store khác không đổi. POS thiếu quyền bị chặn, QR khác cửa hàng không trả dữ liệu. Endpoint saved-QR có hành vi cũ: InvalidOperationException “Không tìm thấy đơn hàng” được middleware map thành HTTP 500; probe kiểm không lộ QR/tài khoản/đơn. Bước UI này giữ nguyên mapping server, ghi nhận để xử lý riêng.
- Probe bank review gốc qua: người chuyển từ ngân hàng, Enter xác nhận một lần với automatic:false, Esc bỏ qua không ghi tiền, khớp POS tự động và dữ liệu chỉ textContent. Probe recovery gốc: 10 tình huống arm lỗi/tắt, 500/429/network, retry giới hạn, wait thay đổi, review bị ẩn ngoài owner, không tự retry 403/409.
- Probe pending QR cũ cập nhật fixture để khai báo đã được cấp manual review; trước đó thiếu module cấp quyền nhưng vẫn kỳ vọng nút manual hiện. Đây là chỉnh dữ liệu thử theo owner hiện hành; thời gian server thật được kiểm riêng, production không đổi.
- 211 file bảo vệ khớp snapshot. Baseline 560 file vẫn bất biến: 513 không đổi, 47 đã đổi, 0 mất; hai asset mới ghi trong sourceFiles. Đã mở và rà ảnh QR, tài khoản, bank summary/evidence/error, QR read-only, Prime/legacy và desktop; bố cục đạt kiểm tra tự động.

## Giới hạn và bằng chứng

Chỉ SQL GUID/Web và Chrome headless/profile riêng; tắt launcher thường, chặn external traffic/transport in. Không dùng database cấu hình, không đóng Chrome đang mở; không chuyển tiền ngân hàng/in/camera thật. Màn bằng chứng trong probe real-app dùng callbacks giả lập; probe notification HTTP và hồi quy SQL kiểm owner tài chính riêng. Chặn service worker riêng ở profile tiêm lỗi; production offline/journal giữ nguyên.

Còn Android/iOS thật: safe area, vùng nhìn khi trình duyệt thay đổi, thao tác một tay và quét QR từ điện thoại khác. Còn thiết bị notification/ngân hàng thật và nghiệm thu cũ. UI-06e tiếp tục offline/journal/khóa tab/đổi tài khoản/giao hàng/màn khách.

[Runtime](mobile-ui-20261025/evidence/ui-06d/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-06d/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-06d/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-06d/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-06d-20261026; snapshot/log: .artifacts/ui-06d.
