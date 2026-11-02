# UI-05e — Kiểm tra chấp nhận nhập hàng

Ngày 25/10/2026. Hoàn tất phần kiểm thử tự động UI-05e; tiếp theo **UI-06a — POS: phiên giỏ, header/ca/quầy và tìm/quét/chọn hàng**. UI-05a–e giữ trạng thái chờ nghiệm thu Android/iOS thật.

## Lỗi đã sửa

Ba nút tăng/giảm trên điện thoại chặn lượng tối thiểu ở 1, nên **1,125 − 1 thành 1**, làm mất phần lẻ. Thẻ hàng, sheet sửa lượng và popup nhập nhanh nay giữ **0,125**, dùng mức tối thiểu **0,001** và ba chữ số thập phân theo miền lượng máy chủ đã có. Nút giảm tại 0,001 giữ nguyên mức này. Áp dụng dưới 768px; desktop giữ mức 1 và đúng cách tính gốc, gồm cả popup với đầu vào nhiều chữ số thập phân.

Sửa đoạn bước lượng trong ba owner quantity/app/mobile và thêm một rule CSS phone để ẩn khung quét trống trên phiếu chỉ xem chưa có lần nhận. Khi có xác nhận, khung này vẫn hiển thị dữ liệu. Phần còn lại của các module khớp snapshot; giữ nguyên command ID/payload, retry, xác nhận máy chủ, camera, quy đổi và quyền. Không thay Razor, CSS khác, bố cục/màu desktop, menu, controller/service/DTO, API, migration hoặc hàng đợi offline.

## Kết quả

- Build Release **0 lỗi/cảnh báo**; **50/50** hồi quy qua, không skip: receiving/provisional, barcode/quyền, completion, quantity projection, photo, cancellation và CSP.
- **28 nhóm browser**, **10 viewport** (320–1600px), kiểm tra thêm 390×440/320×568. DOM/node/form trở lại desktop, ảnh desktop 1440px bằng bản trước sửa; popup desktop giữ 1,125 − 1 = 1 và 0,12345 + 1 = 1,12345.
- Thẻ/sheet nhận lượng dưới 1, dấu phẩy và mức 0,001. Trong sheet, 0, số âm hoặc quá ba số thập phân bị chặn trước khi ghi. Hai dòng cùng nhãn Lốc có hệ số ×4/×6 vẫn tác động đúng dòng.
- CODE128 dùng decoder thật với video tổng hợp. Đọc mã chỉ mở popup; lặp khung hình/nhấn lại lúc đang ghi không tự nhận thêm. Mất phản hồi sau ghi dùng lại command cũ; quét lại có chủ ý tạo command mới. Ba lần nhận 0,125 + 0,125 + 0,001 tạo đúng tổng **0,251 Hộp**.
- Offline trước ghi giữ dữ liệu và command; có mạng lại không tự gửi. Người dùng Lưu lại mới gửi lại đúng lần nhận. Hàng tạm ×12 cũng sửa bằng thẻ/sheet xuống 0,125 và lưu lại đúng.
- Tải lại/mở lại giữ dữ liệu từ máy chủ, đúng hai tab; không thêm lịch sử riêng. Camera bị từ chối/đóng khi đang xin quyền dừng track; reduced motion, forced colors, chữ 150% và điều hướng tab bằng bàn phím qua; không âm thanh hay lỗi JS/CSP.
- Phiếu chờ duyệt không có nút nhập/sửa/quét; F4 không mở form. Ẩn khung quét trống nếu chưa có lần nhận; giữ khung xác nhận khi có dữ liệu. POST có **CSRF hợp lệ** bị máy chủ từ chối bằng 409. Phiếu cửa hàng khác trả đúng lỗi không tìm thấy của cửa hàng hiện tại, không lộ state; tài khoản chỉ View không qua gate Create của nhận hàng.
- Chạy lại **17 nhóm UI-05d** nguyên bản về command/ack; probe tương tác, cuộn và feedback/reload gốc đều qua. Cuộn dịch tối đa **0px**. Bản trước sửa tái hiện đúng lỗi 1,125 − 1 = 1 trong thư mục before; đây là lỗi được kỳ vọng của bản cũ.

SQL cuối: phiếu Draft có bốn dòng hàng đã biết. Lốc ×4 là **0,125 / 0,5 Hộp**, Lốc ×6 là **0,125 / 0,75 Hộp**; Thùng ×24 giữ 2. Dòng Hộp có đúng ba lần nhận, tổng **0,251**. Hàng tạm còn Unresolved, lượng **0,125 Thùng ×12**. Danh mục biến thể/đơn vị/quy cách/barcode, tồn, posting/công nợ, menu và phiếu khác giữ snapshot trước/sau. Không thao tác duyệt sản phẩm vào danh mục.

**95 file bảo vệ giữ hash.** Baseline 560 file không sửa: **516 không đổi, 44 đổi cộng dồn, 0 mất**. Chỉ dùng SQL GUID/Web và Chrome headless/profile của fixture; tắt launcher Chrome thường, không dùng database cửa hàng hoặc đóng/đổi cửa sổ Chrome đang mở.

## Nghiệm thu thiết bị còn mở

| Nội dung | Tự động đã kiểm tra | Android Chrome / iOS Safari thật |
| --- | --- | --- |
| Màn hình, chạm, hai tab, đọc lượng/quy cách | Viewport, vùng chạm ≥44px, không tràn ngang | Chờ kiểm tra safe area, thanh trình duyệt và xoay máy |
| Bàn phím nhập lượng/tìm mã | Bàn phím browser và giảm chiều cao viewport | Chờ IME thật, dấu phẩy, đóng/mở bàn phím và nút lưu |
| Camera, quyền, torch, dừng khi đóng | Video tổng hợp + ZXing thật; từ chối quyền/đóng mô phỏng | Chờ camera sau, ánh sáng, lấy nét, torch, quay lại từ nền |
| Ảnh bao bì | UI-05c đã qua native chooser với ảnh tổng hợp; owner không đổi | Chờ chụp/chọn ảnh thật, dung lượng và quay lại form |
| Mạng và thử lại | Abort sau ghi, offline trước ghi, retry và SQL thật | Chờ chuyển Wi-Fi/di động, khóa màn hình và mở lại |

Các kiểm tra viewport/camera mô phỏng không thay nghiệm thu thiết bị. UI-02/UI-03 menu/ghim/F5-Stop vẫn giữ trạng thái riêng. UI-05 chỉ hoàn tất phần tự động, chưa đánh dấu hoàn tất toàn bộ.

## Bằng chứng

- [Runtime](mobile-ui-20261025/evidence/ui-05e/runtime-review.json), [source](mobile-ui-20261025/evidence/ui-05e/static-review.json), [ma trận](mobile-ui-20261025/evidence/ui-05e/acceptance-matrix.json), [baseline delta](mobile-ui-20261025/evidence/ui-05e/baseline-delta.json).
- [50 hồi quy](../../TestResults/mobile-ui-05e-20261025/regression/regression.trx), [28 nhóm browser](../../TestResults/mobile-ui-05e-20261025/browser/result.json), [SQL](../../TestResults/mobile-ui-05e-20261025/browser/sql-checks.json), [17 nhóm ack](../../TestResults/mobile-ui-05e-20261025/ack-regression/result.json).
- [Lượng lẻ/cùng tên đơn vị](../../TestResults/mobile-ui-05e-20261025/browser/fractional-same-unit-390.png), [chiều cao nhập liệu](../../TestResults/mobile-ui-05e-20261025/browser/keyboard-height-390x440.png), [sau tải lại](../../TestResults/mobile-ui-05e-20261025/browser/confirmed-fractions-390.png), [chỉ xem](../../TestResults/mobile-ui-05e-20261025/browser/readonly-390.png), [desktop](../../TestResults/mobile-ui-05e-20261025/browser/desktop-current.png).
- [Tương tác](../../.artifacts/ui-05e/interaction.log), [cuộn](../../.artifacts/ui-05e/scroll.log), [reload](../../.artifacts/ui-05e/feedback-reload.log), [lỗi trước sửa](../../.artifacts/ui-05e/before.log).
