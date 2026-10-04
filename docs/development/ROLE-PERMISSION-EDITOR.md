# Giao diện phân quyền vai trò

Trang `/Admin/RolePermissions` cho phép chọn vai trò thuộc cửa hàng hiện tại. Có thể mở trực tiếp với `?roleId=...`. Người có đủ quyền tạo vai trò dùng nút **Tạo vai trò**, nhập mã/tên, rồi chuyển thẳng đến bước chọn quyền.

## Thao tác

- Nhóm chức năng nằm bên trái. Bấm nhóm để thu hẹp danh sách.
- Tìm theo cụm từ tiếng Việt, có hoặc không dấu, hoặc mã quyền. Ví dụ `in tem`, `hoan tien`, `pos.order.view`.
- Bấm các thẻ **Đã chọn**, **Chưa chọn**, **Thay đổi chưa lưu** để lọc theo trạng thái.
- Bấm vào cả ô quyền để chọn/bỏ chọn. **Chọn mục này** hỗ trợ trạng thái chọn một phần.
- **Chọn tất cả / Bỏ chọn** chỉ tác động đến kết quả đang hiển thị. Các quyền bị ẩn bởi bộ lọc vẫn được giữ và gửi đầy đủ khi lưu.
- Viền màu vàng và nhãn **Thêm/Bỏ** đánh dấu thay đổi. **Hoàn tác** đưa tất cả quyền về trạng thái lúc tải trang.
- **Kiểm tra & lưu** hiển thị danh sách quyền thêm/bỏ và số người đang dùng vai trò. Nhấn **Lưu phân quyền** để gửi; Esc đóng hộp kiểm tra và giữ bản chỉnh sửa.
- Có nhắc khi rời trang hoặc đổi vai trò trong lúc còn thay đổi chưa lưu.

## Dữ liệu và bảo vệ

- Dùng tên tiếng Việt từ danh mục chuẩn cùng lớp `PermissionDisplayNames`. Mã/ID quyền không đổi; mã kỹ thuật mặc định ẩn và có thể bật khi cần tra cứu.
- Gán các quyền chức năng có sẵn cho vai trò; không tạo mã quyền tùy ý không gắn với chức năng ứng dụng.
- Giữ chính sách `security.role.permissions`, chống giả mạo yêu cầu và kiểm tra cửa hàng phía server. Dữ liệu form sai định dạng hoặc ID quyền không tồn tại không được áp dụng.
- Chỉ mục vai trò không có `roleId` hiển thị bước chọn vai trò, không tự mở hay chỉnh quyền của quản trị viên.
- Riêng nâng cấp giao diện này không thêm migration, không thay đổi quyền hoặc dữ liệu thật khi triển khai. Publish Web như thường lệ (cùng các thư viện phụ thuộc). Các migration từ tính năng khác vẫn phải theo quy trình cập nhật riêng.

## Kiểm thử

`RolePermissionEditorSqlServerTests` kiểm tra nhãn toàn bộ danh mục, tenant, quyền truy cập, chống giả mạo và từ chối dữ liệu sai.

`dotnet run --project GaoApp.Tests.Browser/PosOffline.Browser.csproj -- --role-permissions` dùng Chrome và SQL thử riêng để tạo vai trò, tìm/lọc/chọn, lưu thật, đối chiếu tập quyền trong database, hoàn tác, Esc, chuyển vai trò khi có thay đổi và các chiều rộng 360–1440px.
