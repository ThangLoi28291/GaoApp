# UI-06c — Thanh toán trên điện thoại

Ngày 26/10/2026. Đã triển khai và qua kiểm tra tự động; còn nghiệm thu điện thoại thật. Bước tiếp UI-06d: QR, giao dịch ngân hàng và khôi phục phiên.

## Giao diện

Trên /admin/pos, /admin/pos/v3 và /admin/pos/legacy ở phone <768px, thanh toán dùng toàn chiều cao vùng nhìn thấy: header có nút đóng 44px; body cuộn riêng; footer có trạng thái/lỗi, nút ghi nhận và tác vụ gốc. Tổng, đã thu, còn thiếu, phương thức, số nhập, tiền nhanh và tiền thừa được chia thành các khối navy/blue. Input số tiền lớn, các input khác tối thiểu 16px, nút tối thiểu 44px. Phím tắt không chiếm nhãn trên phone; hướng dẫn tiền mặt/thẻ dùng lời cho thu ngân, khôi phục nguyên văn desktop.

Prime tiếp tục dời nút ghi nhận bằng adapter hiện có. Legacy dời chính btnAddPayment vào footer, giữ node/listener/disabled state; resize về ≥768px trả về đúng vị trí. Adapter chỉ xử lý trình bày và visualViewport legacy; không tạo button/form/handler thu tiền hay tranh owner Prime/Bootstrap. Màn hỏi in/tiền thừa vẫn dùng feedback gốc, chỉ chỉnh style phone. QR/history và xác nhận ngân hàng giữ nguyên, thuộc UI-06d tiếp theo.

## Bảo toàn nghiệp vụ

Index chỉ thêm stylesheet/script. PosPayment thêm đúng một lời gọi renderPaymentPreviewSafe sau khi khôi phục input của lần thu chờ: preview khớp số tiền khôi phục, không còn giữ trạng thái đủ tiền trước đó. Không thêm phép tính hoặc đổi xử lý thanh toán; phần còn lại của module khớp snapshot. Render/app/keyboard/Prime/offline/camera/cart/order/receipt/controller/service/DTO và quyền/menu/cấu hình không sửa. Không thêm API, lưu trữ, queue, migration hoặc tính tiền thứ hai. Xem lỗi, thu nhiều lần, tiền thừa, khóa khi bận, pending collection identity, chốt tiền mặt, lựa chọn in và đơn kế tiếp vẫn do owner cũ xử lý.

Đóng trước khi thu không sửa giỏ hoặc ledger. Mở lại mặc định số còn thiếu đã lưu theo thiết kế gốc; riêng lần thu chưa rõ kết quả khôi phục số tiền/mã để đối chiếu an toàn. Đây không phải bản lưu riêng của form điện thoại.

## Kết quả kiểm tra

- Build Release: 0 lỗi, 0 cảnh báo. 59/59 hồi quy POS/checkout/thu tiền/SQL đồng thời/CSP qua.
- 7 nhóm browser trên Razor/HTTP/SQL thật: 5 so sánh mọi computed property/box của payment và feedback ở Prime 768/1024/1440, legacy 768/1440; tất cả khớp khi bật/tắt CSS mới.
- Prime và legacy, mỗi bản 7 viewport: 320×568, 360×780, 390×844, 430×932, 600×900, 767×900, 390×440. Tổng 14 lượt; modal/footer trong màn, body cuộn, nút/input đạt cỡ chạm, không tràn ngang; cùng node ghi nhận và khôi phục desktop đúng. Hai visualViewport 360px mô phỏng bàn phím giữ footer khả dụng.
- Chọn phương thức, thiếu mã thẻ, giữ số tiền/focus, card 31.000 chậm mạng + Enter lặp, đóng/mở ledger, server commit cash 20.000 nhưng phản hồi 409 yêu cầu thử lại, khôi phục cùng mã; cash 100.000 chốt phần thiếu 80.000 và hiện trả 20.000. Không in theo cấu hình khách thử. Kiểm quyền thiếu POS, thiếu công nợ/cọc/giao hàng và cửa hàng khác.
- SQL: đúng 3 payment/3 định danh riêng: thẻ 31.000, tiền mặt 20.000, tiền mặt 100.000. Tổng đơn 131.000, thu 151.000, trả 20.000, còn thiếu 0; tồn từ 100 xuống 99; ca ghi nhận tiền mặt ròng 100.000 và phi tiền mặt 31.000. Biên nhận HTTP khớp. Menu/cấu hình/giá/tùy chọn in của khách/store khác giữ nguyên.
- Probe nhập tiền gốc không sửa qua: định dạng/chèn/xóa/dán, số tiền lẻ VND bị chặn, mã tham chiếu thẻ, Enter/QR chờ và chuyển khoản dư (HTTP/QR mô phỏng).
- Probe feedback cũ lệch khai báo và còn kỳ vọng màn chọn route hóa đơn đã được bỏ trước bước này; đã cập nhật bộ thử để dùng đúng khai báo/hàm owner hiện hành. 7 nhóm qua: tự động hóa đơn, busy guard, tiền thừa không chặn giỏ kế tiếp, lỗi/thử lại, tùy chọn Không in, popup bị chặn và dedup hoàn tất. HTTP/popup/in đều mô phỏng; không sửa luồng production.
- 206 file bảo vệ khớp snapshot; baseline bất biến 560 file, 513 không đổi, 47 đã đổi, 0 mất. Asset mới ghi riêng trong sourceFiles.

## Giới hạn và bằng chứng

Bộ thử HTTP chặn service worker riêng để tiêm lỗi; lần thu đã commit được thay phản hồi thành 409 thử lại đúng hợp đồng. 503 thuộc chính sách prepared-offline journal cũ, vẫn giữ nguyên; không ép về lỗi form để làm test qua. Chỉ dùng SQL GUID/Web và Chrome headless/profile riêng, tắt launcher thường, chặn transport máy in; không gọi ngân hàng, không chạm database cấu hình và không đóng Chrome đang dùng.

Còn Android/iOS thật: bàn phím mềm, safe area, thao tác một tay. QR/ngân hàng thật thuộc UI-06d; offline/journal/đổi tài khoản và giao hàng thuộc UI-06e. In/camera thật và nghiệm thu các bước trước còn riêng.

[Runtime](mobile-ui-20261025/evidence/ui-06c/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-06c/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-06c/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-06c/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-06c-20261026; log/snapshot: .artifacts/ui-06c.
