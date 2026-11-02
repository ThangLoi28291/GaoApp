# UI-02c: mục ghim điện thoại từ menu cửa hàng

Ngày 25/10/2026. Trạng thái: đã viết mã, chờ người dùng build và tự nghiệm thu bằng Visual Studio. Chưa chạy ứng dụng, build, test, trình duyệt hoặc migration trong lượt này, theo yêu cầu người dùng.

**Cập nhật sau yêu cầu sửa lỗi:** đã cài SDK/runtime đúng, build Web thành công và cập nhật `GaoAppDb` local lên đủ 81 migration; schema khớp, bảng ghim chưa có dữ liệu mặc định. Không mở ứng dụng. Xem [báo cáo sửa môi trường và migration](MOBILE-UI-02C-ENVIRONMENT-FIX-20261025.md). Các ghi chú “chưa build/migration” bên dưới mô tả lần bàn giao mã ban đầu; không còn áp dụng cho database local này.

## Phạm vi đã chốt

- Chỉ nâng giao diện điện thoại theo mẫu v2; giữ giao diện máy tính đang có.
- Nguồn chức năng là `AdminMenuItems` qua `AdminMenuService.GetForRenderAsync`; cùng quyền, trạng thái và thiết lập ẩn/hiện của cửa hàng.
- Không tạo menu chức năng, thêm mục giả, sửa tên/icon/đường dẫn/thứ tự menu máy tính hoặc cấp quyền.
- Không tự khởi chạy mỗi lần sửa xong; người dùng chạy Visual Studio để kiểm tra.

## Mã đã bổ sung

`AdminMenuPinSettings` lưu một bản cấu hình mỗi cửa hàng: JSON chứa tối đa bốn ID theo thứ tự. Không sao chép tên, URL, icon hay quyền. Không có mục ghim mặc định. Migration `20261025090000_AddAdminMenuPinSettings` chỉ tạo bảng và chỉ mục duy nhất theo StoreId; chưa áp dụng.

API đọc cấu hình lọc lại bằng menu đủ điều kiện của người đăng nhập. Trên điện thoại, JavaScript tiếp tục đối chiếu từng ID với liên kết đã render trong partial menu thật; không tự xây đường dẫn chức năng. Mục mất quyền, bị ẩn, tắt, xóa hoặc thuộc cửa hàng khác bị bỏ qua; không tự điền chức năng thay thế. Máy tính/tablet không gọi API mục ghim.

Chỉ quản trị viên cửa hàng/Host Admin có quyền `security.role.permissions` được chỉnh ghim. Controller và service đều kiểm tra; StoreId/UserId lấy từ ngữ cảnh đăng nhập, không lấy từ body. POST có chống CSRF. Lưu dùng guard quản trị hiện có, transaction, rowversion, version cây menu và audit ID trước/sau; xung đột trả 409. Không sửa RolePermissions, UserInStore, session stamp hoặc các dòng AdminMenuItems.

Màn cấu hình có tìm không dấu, chọn/bỏ, giới hạn bốn, đổi thứ tự, bỏ mục không còn khả dụng, xem trước theo nhóm/nhân viên bằng dịch vụ ẩn/hiện hiện có và giữ lựa chọn khi lỗi lưu. Thanh ghim chỉ hiện dưới 768 CSS px. Tên dài rút gọn có nhãn đầy đủ; nút Menu luôn có. Trong POS, mục ghim nằm trong drawer quản trị để không chồng thanh thanh toán. Chế độ nhập hàng `wrd-app-mode` đang sở hữu dock riêng nên ẩn thanh ghim chung; phối hợp điều hướng riêng của module tiếp tục ở UI-02e/UI-03.

Đường dẫn cấu hình: `/admin/menu-pins`. Quản trị viên có thể mở **Menu → Chỉnh mục ghim** trên điện thoại. Đây là thao tác chỉnh trình bày menu, không phải bản ghi chức năng mới. Không thêm lối vào trên giao diện máy tính.

## Tự kiểm tra trên Visual Studio

1. Mở solution, build trên SDK/runtime dự án yêu cầu. Cập nhật database **môi trường thử** bằng quy trình migration hiện có để có bảng `AdminMenuPinSettings`; bước này chưa tự chạy. Migration không thêm dữ liệu menu.
2. Chạy `GaoApp.Web` bằng Visual Studio; đăng nhập cửa hàng bằng ADMIN có quyền quản lý menu. Dùng trang thật của cửa hàng; trang demo vẫn giữ dữ liệu mẫu cũ.
3. Đặt chiều rộng 390 px, mở Menu → Chỉnh mục ghim hoặc `/admin/menu-pins`. Chọn 1–4 mục, đổi thứ tự, xem trước theo nhóm/nhân viên rồi lưu.
4. Quay lại trang nghiệp vụ và tải lại: thanh dưới cùng hiện đúng thứ tự đã ghim cùng nút Menu. Thử 0/1/4 mục và tiêu đề dài. Mục thứ năm phải bị chặn; vẫn bỏ chọn được khi đã đủ bốn.
5. Đăng nhập tài khoản ít quyền; thử ẩn menu ở nhóm hoặc nhân viên, tắt menu và ẩn nhóm cha. Tải lại trang: mục không đủ điều kiện không xuất hiện, không có mục thay thế tự động. Xem trước phải khớp. Thử cửa hàng khác: cấu hình không lẫn nhau.
6. Mở cấu hình ở hai tab: lưu tab thứ nhất, lưu tab cũ phải báo đã đổi và yêu cầu tải lại. Thử mất kết nối khi lưu: báo lỗi và giữ các mục đang chọn.
7. Mở menu POS trên điện thoại: ghim ở trong drawer, không chồng thanh thanh toán. Đổi về 768/1440 px: thanh ghim và nút chỉnh ghim ẩn; sidebar, bảng và toolbar máy tính giữ giao diện đang có.

Đã bổ sung `AdminMenuPinRulesTests` và `AdminMenuPinSqlServerTests` cho giới hạn/ID hợp lệ, scope cửa hàng, quyền, CSRF, đồng thời, ẩn nhóm cha, stamp và dữ liệu menu không đổi. Các test này **chưa chạy**. Có thể chạy nhóm `AdminMenuPin` trong Test Explorer; bài SQL dùng database thử riêng của fixture.

## Còn chờ nghiệm thu

Build solution, các test mới, xác thực/SQL thật, kích thước 320/390/768/1440 và Safari/Chrome trên điện thoại thật. Chưa đánh dấu UI-02c hoàn thành nghiệm thu. UI-02d/e/f và UI-03 chưa triển khai trong lượt này.
