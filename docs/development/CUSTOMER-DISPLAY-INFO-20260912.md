# Nhân viên tại quầy và Wi-Fi trên màn hình khách — 2026-09-12

Yêu cầu: bổ sung tên nhân viên đăng nhập tại máy/quầy và tên, mật khẩu Wi-Fi dành cho khách trên `/admin/pos/customer-display`.

## Sử dụng

- Đầu màn hình hiển thị họ tên nhân viên phục vụ và tên quầy của phiên đăng nhập. Nếu chưa có họ tên thì dùng tên đăng nhập. Không lấy người tạo đơn hoặc người mở ca; vẫn hiển thị khi chưa có đơn.
- Vào **Màn hình khách** (`/admin/displaypromotion`), nhập **Tên Wi-Fi**, **Mật khẩu Wi-Fi** rồi bấm **Lưu Wi-Fi**. Các quầy trong cùng cửa hàng nhận cập nhật trực tiếp. Để trống mật khẩu cho mạng mở; xóa cả hai ô để ẩn mục Wi-Fi. Giữ nguyên chữ hoa/thường và khoảng trắng đã nhập.
- Thông tin nhân viên và Wi-Fi nằm ngoài vùng giỏ, quảng cáo và lớp thanh toán, nên tiếp tục hiện khi chờ khách, xem đơn, quét QR và nhận thông báo thanh toán.

## Mã và dữ liệu

`CustomerDisplayService` lấy nhân viên từ tài khoản hiện tại và quan hệ nhân viên/cửa hàng; quầy lấy từ ngữ cảnh thiết bị hiện có. `/admin/pos/customer-display/info` chỉ phục vụ người có quyền xem POS, không nhận ID nhân viên/quầy/cửa hàng do client chọn và không cache. Trang render thông tin ban đầu từ server, cập nhật lại khi nhận sự kiện Wi-Fi, quay lại tab hoặc theo nhịp kiểm tra 15 giây. Nếu phiên chuyển sang quầy/cửa hàng khác, tải lại toàn màn hình để đồng bộ nhóm SignalR và giỏ.

Wi-Fi lưu trong hai cột nullable `Stores.GuestWifiName` / `GuestWifiPassword` (128 ký tự). Lưu cấu hình dùng quyền quản lý màn hình khách hiện có `catalog.displaypromotion.manage`, antiforgery và RowVersion của cửa hàng để tránh ghi đè thay đổi đồng thời. Không thêm quyền mới vào database. Trường thông tin được render bằng Razor encoding và `textContent`.

## Triển khai

**Áp dụng migration `20260912120000_AddCustomerDisplayWifi` bằng quy trình Migrator hiện có trước khi chạy bản Web mới trên host.** Production không tự chạy migration khi chỉ copy Web. Migration chỉ thêm hai cột nullable; cửa hàng chưa nhập Wi-Fi sẽ ẩn phần Wi-Fi. Cập nhật cùng bản Web, Razor, JavaScript và CSS rồi reload màn hình khách. Không cần xóa dữ liệu trình duyệt.

Chưa áp dụng migration vào database bán hàng, chưa publish/restart host.

## Kiểm tra

Build dùng `Logs/pos-regression-20260912-artifacts`, tách output của ứng dụng đang chạy. `CustomerDisplayInfoSqlServerTests` kiểm tra nâng cấp database cũ, hai nhân viên/hai quầy, thay nhân viên cùng quầy, tách cửa hàng, quyền sửa, antiforgery, phiên bản cũ, chuỗi Wi-Fi nguyên vẹn, mạng mở và xóa cấu hình. Browser probe `--customer-display` chạy Web/SQL tạm, lưu Wi-Fi qua giao diện thật và kiểm tra cập nhật SignalR; đối chiếu tên nhân viên bất kể tên người tạo đơn, bố cục giỏ/quảng cáo và QR tại nhiều kích thước.

Kết quả chạy được lưu tại `TestResults/customer-display/customer-display-info.trx` và ảnh tại `TestResults/customer-display/browser`.

Đã đạt: build Release (0 lỗi; 10 warning có sẵn), 5/5 kiểm thử .NET/SQL/metadata quyền/migration, `node --check` và kiểm tra diff. Chrome probe đạt trọn luồng: lưu Wi-Fi từ form thật, cập nhật/xóa trực tiếp, không thực thi HTML trong mật khẩu, luôn giữ tên nhân viên của phiên dù đơn có người tạo khác; giỏ/quảng cáo tại 7 kích thước, QR tại 8 kích thước, giữ nguyên phát video và tổng tiền. Đã xem ảnh 1440×900, QR 800×600 và màn chờ 390×844: nhân viên, tên quầy, Wi-Fi và nội dung chính đều nằm trong vùng nhìn thấy.

## Làm mới trang quản lý màn hình khách

Theo ảnh phản hồi tiếp theo, chỉnh `/admin/displaypromotion` với tiêu đề trang trong khu vực header chung, màu xanh đồng bộ màn hình khách, Wi-Fi có bản xem trước khi nhập và các ô cùng hàng trên desktop. Danh sách kết hợp ảnh với tên/mô tả, nhãn loại bằng tiếng Việt, thời lượng, thứ tự và trạng thái bật/ẩn; không đưa đường dẫn tệp vào danh sách. Có trạng thái rỗng, bố cục thẻ trên màn hình nhỏ và hộp sửa nội dung cùng phong cách. Số lượng đang bật/tạm ẩn và nhãn Wi-Fi cập nhật sau khi lưu thành công.

Tách nội dung trang vào `_Workspace.cshtml`, CSS/JavaScript riêng `display-promotion.css` và `display-promotion.js`, có hash phiên bản. Lần chỉnh giao diện này không thêm migration hoặc thay API.

Xác minh bằng Chrome với cửa hàng/SQL dùng một lần: lưu Wi-Fi, mở hộp sửa, sửa/lưu, thêm/xóa và bật/tắt thành công; không có pageerror. Chụp và kiểm tra ở độ rộng 1600, 1024, 768, 390; không tràn ngang, hai ô Wi-Fi thẳng hàng ở desktop. Đã xem ảnh desktop, mobile và modal. Ảnh dùng dữ liệu thử nằm tại `TestResults/display-promotion/browser/page-1600.png`, `page-390.png`, `edit-modal.png`. Build Razor đạt, `node --check` và `git diff --check` đạt. Chưa publish/restart ứng dụng thật.
