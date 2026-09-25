# Tự đóng trang in

Các trang in bill POS, phiếu nhận/chốt ca, phiếu giảm giá, báo cáo ca và đơn đặt hàng tự đóng khi hộp thoại in kết thúc (bao gồm bấm Hủy). Màn hình bán hàng/quản lý phía trước được giữ nguyên. Bản xem trước chưa bấm in vẫn mở.

- `print.lifecycle.js` dùng `afterprint`, hoặc chuyển từ chế độ in về màn hình qua `matchMedia('print')`. Không đóng theo thời gian cố định hay ngay sau khi `print()` trả về, vì một số trình duyệt mở hộp thoại không chặn JavaScript.
- Hóa đơn có khung giấy bên trong: theo dõi sự kiện của khung giấy rồi đóng tab hóa đơn. Tab mở với `noopener` cũng dùng được. Bill ACB nhúng trong trang chỉ dọn khung in tạm.
- QZ Tray: đóng sau khi gửi lệnh thành công; lỗi kết nối/gửi lệnh giữ trang và nút in lại. Thành công gửi lệnh không phải xác nhận máy đã in ra giấy.
- In offline và in thử mẫu dùng cùng cơ chế; không đóng POS hoặc trang thiết kế mẫu. Yêu cầu đánh dấu phiếu ca đã in dùng `keepalive` và token chống giả mạo để không mất yêu cầu khi tab đóng.

Không cần migration. Publish kèm file JavaScript mới và các Razor view đã sửa; tải lại trình duyệt để nhận phiên bản mới.

Kiểm tra:

- `node GaoApp.Tests.Browser/print-lifecycle.browser.cjs`: bản in thực từ view/script, giả lập kết thúc hộp thoại và QZ để kiểm tra tự đóng, in lỗi/in lại, iframe, offline và giữ nguyên màn hình POS. Không gửi lệnh tới máy in thật.
- `PosOffline.Browser.dll --shift-admin`: ứng dụng và SQL thử nghiệm riêng, tạo/sửa/in phiếu nhận ca, nhận/chốt ca, tự đóng tab in nhận/chốt ca và giữ màn hình quản lý/nhân viên.
