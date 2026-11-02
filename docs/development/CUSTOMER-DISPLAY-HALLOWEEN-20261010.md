# Màn hình khách hàng — Đêm hội bí ngô

Đã gắn mẫu người dùng duyệt vào `/admin/pos/customer-display`. Tiêu đề dùng Baloo 2 bo tròn; nội dung đơn hàng, số tiền, tài khoản và bill giữ font đọc rõ. Tông tím ấm, cam bí ngô và nền kem.

Màn chờ mặc định có bí ngô vẫy tay, dơi với hai tư thế vỗ cánh, đèn lồng đung đưa, kẹo chuyển động và sao sáng nhẹ. Khi sản phẩm thay đổi có vệt sáng quét qua ảnh. Phần thanh toán có bí ngô chỉ tay; bill có bí ngô chào khách và giấy màu chạy một lần theo orderId. Các lớp trang trí không nằm trên QR hoặc giấy bill. Hiệu ứng chạy tự động và tôn trọng giảm chuyển động; tạm dừng khi tab bị ẩn.

Quảng cáo do cửa hàng cấu hình tiếp tục dùng player hiện có. `has-festival-idle` chỉ áp dụng khi dùng cảnh mặc định; khi có quảng cáo, quay lại bố cục quảng cáo hiện có. SignalR, các lệnh thanh toán, receipt đã chốt, giữ bill khi reset, nhân viên phiên đăng nhập và Wi-Fi không đổi. Không tạo menu hoặc dữ liệu cấu hình mới.

CSS/JS riêng: `pos-customer-display-halloween.css` và `pos.customer-display-halloween.js`. Artwork WebP đã duyệt được lưu trong `wwwroot/Admin/img/customer-display-halloween/`. Font được lưu cục bộ cùng OFL, nguồn chính thức: https://github.com/google/fonts/tree/main/ofl/baloo2 . Không phụ thuộc mạng để tải font/artwork lúc khách xem.

## Kiểm tra

- Build Razor/Web thành công, 0 lỗi; NU1900 do môi trường không truy cập dịch vụ kiểm tra lỗ hổng NuGet.
- `node --test GaoApp.Tests/Ui/customer-display-receipt.test.cjs`: 3 kiểm thử qua.
- `customer-display-halloween.browser.cjs`: kiểm tra view Razor thật với API/SignalR thử, không khởi động dịch vụ DB hoặc gửi thanh toán. Màn chờ/giỏ/QR/bill, dữ liệu tiền, nhân viên/Wi-Fi, quảng cáo giữ DOM khi đổi giỏ, QR giữ nguyên ảnh, giữ bill khi reset, font/artwork cục bộ và giảm chuyển động đều qua. Kích thước: 1920×1080, 1440×900, 1024×768, 800×600, 768×1024, 390×844, 320×740.
- Đã xem ảnh kiểm tra máy tính và điện thoại. Sửa QR trên màn hình ngắn 320px để nội dung chuyển khoản hiển thị đầy đủ.

Harness và ảnh/report nằm trong `Logs/customer-display-halloween/`. Đây là kiểm tra trình bày bằng endpoint thử; chưa chạy lại giao dịch SQL/SignalR thật hoặc triển khai lên máy chủ.
