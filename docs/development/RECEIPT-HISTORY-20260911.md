# Lịch sử phiếu nhập dễ đọc

Trang `/admin/stock-documents/{id}` hiển thị lịch sử theo ngày, mới nhất trước; mỗi lần hiển thị 12 thao tác và có nút xem thêm. Có tìm kiếm không dấu theo nhân viên, tên hàng đã lưu và nội dung; lọc thông tin phiếu / hàng nhập / duyệt phiếu / hóa đơn; đổi thứ tự và tải lại.

- Đặt tên tiếng Việt cho đủ 45 giá trị `PurchaseReceiptAuditEventType` hiện tại.
- Ưu tiên số lượng, trạng thái, tên hàng, quy đổi và giá trị trước → sau. Chi tiết dùng bảng với nhãn tiếng Việt. Dữ liệu gốc nằm trong phần kỹ thuật đóng mặc định.
- Bản ghi chỉ có snapshot sau thao tác hiển thị “Ghi nhận”, không suy đoán giá trị cũ từ sự kiện khác. “Không được lưu” khác với giá trị null (“Chưa có”). Các mã tham chiếu không có tên lịch sử vẫn hiển thị “Mã #…” để không thay bằng tên hiện tại gây hiểu nhầm.
- `OccurredAtUtc` không kèm múi giờ từ SQL được hiểu là UTC và hiển thị theo múi giờ trình duyệt.
- JSON không hợp lệ / loại sự kiện mới vẫn xem được dữ liệu gốc. Tất cả nội dung lịch sử được chèn bằng `textContent`.
- API, quyền `System.AuditLog.View` và dữ liệu kiểm toán chỉ đọc giữ nguyên. Không cần migration. Bấm “Tải lại” để lấy các thao tác vừa phát sinh trên phiếu.

## Kiểm tra

Build Web Release theo artifacts path `.artifacts/employee-account` để tránh khóa file ứng dụng đang chạy.

Browser probe: `dotnet .artifacts/employee-account/bin/PosOffline.Browser/release/PosOffline.Browser.dll --receipt-history`.

Probe dùng cơ sở dữ liệu SQL dùng một lần, seeding lịch sử mẫu với thời gian cố định, kiểm tra 45 tên thao tác, múi giờ, trước/sau và snapshot thiếu giá trị cũ, tìm kiếm/lọc/thứ tự/xem thêm, JSON lỗi và nội dung HTML độc hại, tải lỗi rồi thử lại, bố cục điện thoại, quyền xem trang và API.

Kết quả: `Logs/receipt-history-build.log`, `Logs/receipt-history-browser-verified.log`. Ảnh: `TestResults/receipt-history/browser/`.
