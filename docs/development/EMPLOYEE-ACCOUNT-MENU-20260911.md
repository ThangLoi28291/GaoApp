# Menu tài khoản nhân viên — thanh công cụ trên cùng

Đã triển khai avatar tròn ở góc trên bên phải theo mẫu giao diện được chọn. Menu xổ xuống hiển thị tên/vai trò, hồ sơ của tôi, tự đổi mật khẩu, lối tắt POS theo quyền, trợ giúp và đăng xuất.

## Giao diện

- Desktop/tablet/điện thoại: chỉ có cụm icon/avatar ở góc phải, cùng hàng với phần tiêu đề trang. Không có tên cửa hàng hay thanh nền trắng chiếm một hàng riêng. Trang không khai báo section header dùng cụm icon nổi theo luồng ở bên phải nội dung. Menu tài khoản vẫn hiển thị cửa hàng và căn mép phải với avatar.
- `_AdminToolbar.cshtml` có vùng `data-admin-utility-slot` phía trước avatar để bổ sung thông báo và các chức năng khác. Vùng này ẩn khi chưa có chức năng, không hiển thị biểu tượng thông báo/số đếm giả.
- Trong POS Prime: avatar nằm cuối nhóm công cụ của thanh trên cùng. Không cần mở menu quản trị để vào tài khoản.
- Menu mặc định đóng; hỗ trợ click bên ngoài, Esc, Enter/Space, phím mũi tên, Home/End và điều hướng bàn phím.
- Hồ sơ chỉ đọc: tên đăng nhập, họ tên, email, cửa hàng, vai trò, chức vụ, điện thoại, ngày vào làm. Không đưa ghi chú quản lý vào hồ sơ cá nhân.
- Trợ giúp có hướng dẫn cập nhật hồ sơ, quên mật khẩu và kết thúc sử dụng/ca bán hàng. Không đặt thông tin liên hệ giả.
- Chưa triển khai các mục mở rộng: tải avatar, danh sách thiết bị, chuyển cửa hàng, chọn giao diện sáng/tối.

## Đổi mật khẩu

`POST /admin/account/change-password`

- Yêu cầu cookie đăng nhập hợp lệ, đúng cửa hàng hiện tại và antiforgery token.
- Gửi form với `CurrentPassword`, `NewPassword`, `ConfirmPassword`; không có tham số chọn nhân viên/cửa hàng.
- Antiforgery token gửi qua trường `__RequestVerificationToken` hoặc header `RequestVerificationToken`.
- Dùng FluentValidation đang hoạt động trong ứng dụng: mật khẩu mới 12–128 ký tự, xác nhận khớp. Không trim mật khẩu.
- Xác minh mật khẩu hiện tại, không cho dùng lại cùng mật khẩu; hash bằng dịch vụ PasswordHasher có sẵn.
- Giới hạn thử mật khẩu dùng chung ngân sách tài khoản với đăng nhập, mặc định 10 lần/phút, độc lập IP/cửa hàng.
- Rowversion bảo đảm hai yêu cầu đã xác minh cùng mật khẩu cũ không cùng ghi đè thành công.
- Mật khẩu thuộc tài khoản toàn hệ thống. Sau khi lưu, session stamp cũ mất hiệu lực ở mọi cửa hàng; ngắt các kết nối POS của người dùng trên tiến trình hiện tại và đăng xuất cookie hiện tại. Các máy chủ khác vẫn kiểm tra session stamp trước khi xử lý/gửi dữ liệu POS theo cơ chế có sẵn.
- Ghi nhật ký kết quả đổi mật khẩu; không đưa mật khẩu hoặc hash vào thông báo/nhật ký.
- Form xóa dữ liệu mật khẩu khi đóng và không lưu vào localStorage. Khi quay lại từ bộ nhớ lịch sử trình duyệt, tải lại trang để kiểm tra phiên.

Phản hồi:

| HTTP | Ý nghĩa |
| --- | --- |
| 200 | Đã đổi mật khẩu, trả `message` và `redirectUrl` về đăng nhập |
| 400 | Thông tin không hợp lệ hoặc thiếu/sai CSRF; lỗi nhập liệu có `errors` theo tên trường |
| 401 | Phiên không còn hợp lệ |
| 409 | Tài khoản vừa thay đổi đồng thời, cần đăng nhập lại |
| 429 | Đạt giới hạn số lần thử, có header `Retry-After` |

Không có thay đổi schema hoặc migration, không thay quyền và không dùng luồng reset mật khẩu của quản trị viên.

## Kiểm tra

- Lần thu gọn thành cụm icon: build thành công, 4/4 kiểm thử HTTP tài khoản và điều hướng responsive đã qua (`employee-account-compact.trx`). Đã xem trên desktop và màn hình 320px: bỏ thanh nền trắng/tên cửa hàng, avatar cùng hàng tiêu đề, menu không tràn chiều ngang.
- Lần chuyển lên thanh công cụ: build thành công và 21/21 kiểm thử HTTP tài khoản, điều hướng responsive, POS Prime responsive đã qua (`employee-account-toolbar.trx`). Kiểm tra trực quan trên desktop và màn hình 320px: avatar ở góc phải, menu căn phải nằm trong màn hình, mở hồ sơ bằng bàn phím và bố cục POS không đẩy avatar xuống hàng công cụ phụ.
- Build Release ở `.artifacts/employee-account`, tách khỏi thư mục Visual Studio đang dùng.
- 32/32 kiểm thử tài khoản, cookie/session stamp, cạnh tranh SQL, audit và layout liên quan đã qua.
- 2/2 kiểm tra danh mục phân quyền đã qua. Danh mục được cập nhật cho self-service và 5 action mã vạch có sẵn vốn kiểm tra quyền theo loại phiếu bên trong từng action; không thay đổi hành vi hay quyền của API mã vạch.
- Kiểm thử HTTP sử dụng ứng dụng và SQL LocalDB thật với tài khoản/database riêng: thiếu CSRF, chưa đăng nhập, sai mật khẩu cũ, độ dài, xác nhận, dùng lại mật khẩu, chèn UserId/RoleId của người khác, nhiều phiên/nhiều cửa hàng và giới hạn số lần thử.
- Kiểm tra trình duyệt trên HTML do Razor thật render và CSS/JS của ứng dụng: menu, hồ sơ, form mật khẩu, báo lỗi xác nhận, hiện/ẩn, bàn phím và màn hình nhỏ 320–390px. Không sửa mật khẩu tài khoản vận hành khi kiểm tra.
- Kiểm tra bổ sung trang POS trả HTTP 200 và chỉ render một menu tài khoản. Máy chủ xem giao diện chỉ phục vụ HTML/CSS/JS, không kết nối nghiệp vụ POS; luồng dữ liệu/password được kiểm tra bằng máy chủ HTTP + SQL riêng ở trên.
- Kết quả kiểm thử: `TestResults/employee-account/*.trx`.

Visual Studio tiếp tục quản lý ứng dụng chạy tại localhost. Máy chủ dùng cho kiểm thử chạy riêng và được dừng sau khi hoàn tất; không để tiến trình thử nghiệm giữ DLL trong `bin/Release/net8.0`.
