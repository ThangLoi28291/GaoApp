# Quy ước menu của dự án

- Người dùng tự tạo và sắp xếp menu qua chức năng Quản lý menu.
- Sidebar chỉ hiển thị các mục từ cấu hình `AdminMenuItems` của cửa hàng, theo quyền, trạng thái và thứ tự đã cấu hình. Không gắn cố định mục menu chức năng vào layout/sidebar hay thêm mục menu giả vào màn hình ẩn/hiện menu.
- Khi nâng cấp hoặc thêm chức năng, không tự tạo menu mới bằng seeder, migration, script hay thao tác trên dữ liệu. Cung cấp đường dẫn chức năng để người dùng tự tạo menu khi cần.
- Việc khai báo quyền truy cập cho chức năng độc lập với việc tạo menu. Giữ các kiểm tra quyền ở controller/service.
