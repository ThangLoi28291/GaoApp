# POS: xác nhận và lịch sử quét

Phạm vi được người dùng xác nhận: `/admin/pos` (bán hàng), áp dụng cả giao diện Prime và toolbar dùng chung. Không sửa màn nhập hàng trong lượt này.

## Hành vi

- Mỗi lần quét hoặc chọn sản phẩm thành công tạo một lượt riêng. Cùng hàng nhưng khác đơn vị vẫn là hai dòng độc lập.
- Dòng vừa cộng được đưa lên đầu. Giữ vị trí cuộn khi thay và sắp xếp dòng; không cuộn cả trang tới sản phẩm.
- Dòng sáng xanh 1,5 giây và nhãn `Vừa quét +…`; tôn trọng thiết lập giảm chuyển động.
- Thẻ ngay dưới ô quét giữ ảnh, tên biến thể, mã, quy cách, số lượng vừa cộng và trước → sau. Chỉ bấm ảnh mới mở ảnh lớn, có nút đóng. Không âm thanh, không toast/popup thành công riêng.
- Lịch sử 10 lượt mở bên phải trên desktop, từ dưới lên trên mobile. Drawer giữ focus và chặn các phím thanh toán/giữ đơn khi đang mở.
- Sửa số lượng bằng tay không thêm lượt quét. Thẻ vẫn giữ số lượng lịch sử và bổ sung số hiện tại nếu đã thay đổi. Dòng bị xóa được ghi rõ.
- Trước phản hồi là `Đang ghi nhận…`. Lưu offline thành công hiển thị `Đã lưu tại quầy · Chờ đồng bộ`; nhật ký offline hiện có chịu trách nhiệm đồng bộ và chống lặp.
- Chỉ đề nghị thử lại khi nhận được lỗi từ chối xác định. Timeout hoặc trạng thái cần kiểm tra nhật ký không tự gửi một lệnh cộng khác.

## Dữ liệu và giới hạn

Lịch sử là dữ liệu hỗ trợ đối chiếu trên tab đang dùng, lưu trong `sessionStorage`, tách theo cửa hàng/quầy/nhân viên/giỏ. Giữ được khi F5 hoặc mở lại giỏ trong cùng tab; không phải nhật ký kiểm toán trên máy chủ. Giới hạn 20 giỏ gần đây, mỗi giỏ 10 lượt. Khi mã giỏ offline được đổi thành mã máy chủ, lịch sử được chuyển theo đúng giỏ. Lỗi lưu lịch sử không làm thất bại giao dịch bán hàng.

Phản hồi POS cho dòng mới trước đây có thể thiếu navigation `Variant`, dẫn tới tên sản phẩm cha và thiếu ảnh ngay lần quét đầu. `POSService` dùng lại biến thể đã tải để tính giá quy cách trong cùng request khi ánh xạ tên/ảnh, không thêm truy vấn hay gắn graph không tracking vào EF.

Sửa thêm `PosCommon.normalizeApiError` để giữ `statusCode` từ `PosError`; lỗi HTTP 400/403 không bị phân loại thành lỗi mạng không rõ kết quả.

## Kiểm tra chuyên biệt

- `GaoApp.Tests.Browser/pos-scan-feedback.browser.cjs`, chạy bằng `--pos-scan-feedback`: Razor/JS thật, Chrome và SQL thử nghiệm tự tạo/tự dọn; ảnh thử nghiệm phục vụ có kiểm soát. Kiểm tra quét A/B/A, quy cách, số lượng, thẻ/ảnh, drawer, F5, mobile 320/390, cuộn, lỗi từ chối, mất phản hồi sau commit, lịch sử 10 lượt, chọn tay, giữ đơn, giỏ offline và ánh xạ mã.
- Các lớp UI POS được chọn bằng `--filter`; không chạy toàn bộ suite.
- Hai file Node `pos-barcode-races.test.cjs` và `pos-action-locks.test.cjs`.
- Một test SQL `PosOfflineSqlServerTests.Retried_cart_mutations_and_checkout_commit_once_with_durable_responses`.

Log: `Logs/pos-scan-feedback-*.log`. Ảnh kiểm tra: `TestResults/pos-scan-feedback/`.

Kết quả cuối: build thành công; kiểm tra Chrome/SQL của luồng quét PASS; 45 test UI, 16 test Node và 1 test SQL hồi quy đều PASS. Đã xem ảnh desktop, mobile 320/390 và lịch sử hai hướng mở.

Không Run All, không triển khai host, không cần migration. Kiểm tra Chrome mô phỏng phím máy quét; không điều khiển máy quét vật lý.
