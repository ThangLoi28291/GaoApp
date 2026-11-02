# UI-02a–b · Áp dụng chuẩn v2 vào menu thật

Ngày 25/10/2026. Sau khi người dùng duyệt mẫu điện thoại v2, đã chuyển thành phần menu dùng trong Admin và POS sang chuẩn mới. Phạm vi lần này là UI-02a–b; cấu hình ghim và các màn quản lý menu thuộc các tiểu bước tiếp theo.

## Thay đổi trong ứng dụng

- `_VerticalMenu.cshtml` vẫn gọi **một lần** `AdminMenuService.GetForRenderAsync(storeId, userId, ct)` và nhận cây đã lọc theo cửa hàng, quyền, trạng thái và thiết lập ẩn/hiện. Thiếu store/user hợp lệ thì không đọc menu và không dùng dữ liệu của cửa hàng khác.
- `AdminNavigation.Build` chuyển cây đủ điều kiện thành model trình bày; giữ thứ tự cấu hình, không thêm mục, không sửa DTO nguồn; giữ quy tắc mẫu phiếu chỉ dành cho quản trị viên ở mọi cấp. Nhóm trống sau lọc được loại bỏ.
- Partial `_NavigationItems` render đệ quy thay giới hạn hai cấp trước. Mục hiện hành có `aria-current`, các nhóm tổ tiên mở sẵn. URL local xét path và query cấu hình; controller/action xét cả area. Không đánh dấu URL bên ngoài là trang hiện tại.
- Điện thoại dưới 768 px có menu đầy màn hình, header/nút đóng, tìm kiếm không dấu, thẻ nhóm và điểm nhấn xanh lam. Tablet giữ drawer; desktop giữ sidebar. Tên dài được xuống dòng; logo, nhãn và điểm mở menu cũ được bảo toàn.
- Tìm theo nhóm hiển thị các mục con; tìm mục sâu giữ các nhóm tổ tiên. Đặt lại khôi phục trạng thái mở nhóm trước tìm. Không có kết quả có phản hồi và nút đặt lại.
- Admin menu khóa phần nội dung phía sau, giữ Tab trong menu; Escape xóa truy vấn trước, Escape tiếp theo đóng và trả focus về nút mở. Menu POS dùng focus và scroll lock do Bootstrap offcanvas quản lý; cùng thành phần chỉ bổ sung tìm kiếm/nhóm.
- Khi trống, hiển thị lý do; người có policy quản lý menu thấy đường dẫn đến Quản lý menu. Không tạo mục menu giả để lấp chỗ trống.
- Token navy/lam được tách dùng chung giữa demo đã duyệt và menu thật. CSS/JS mới chỉ có hiệu lực trong phạm vi menu opt-in; không thay nội dung nghiệp vụ của các trang chứa menu.

Giữ nguyên `AdminMenuService`, `MenuVisibilityRules`, repository, controller/service nghiệp vụ, cấu hình `AdminMenuItems`, seeder và migration. Không có thao tác ghi database hoặc cấp quyền. Đây chưa phải tiểu bước ghim bốn mục.

## Bản xem và bằng chứng

[Xem partial thật với cấu hình thử](http://127.0.0.1:5206/admin/ui-foundation/navigation), [kiểm tra trong drawer POS](http://127.0.0.1:5206/admin/ui-foundation/navigation?surface=pos). Hai URL này thuộc công cụ `GaoApp.UiPreview`, **không phải chức năng/route nghiệp vụ mới trong Web**. Trong Web, phần thay đổi nằm ở menu của các trang dùng layout hiện hữu.

Công cụ compile trực tiếp partial, model, `_CommonMasterLayout`, `_ContentNavbarLayout`, `_POSPrimeLayout` từ source Web. Khi chụp ảnh, nó render partial thật trong khung Admin/POS cô lập với service cấu hình mẫu. Không khởi động ứng dụng đầy đủ, không kết nối database; phương thức ghi trong fixture đều ném lỗi và máy chủ chỉ nhận GET/HEAD.

| Kiểm tra | Kết quả và giới hạn |
|---|---|
| Build phạm vi UI | Qua; Razor/layout/model thật được compile; 0 warning, 0 error |
| Projection contract | 16 kiểm tra qua: cây sâu, tổ tiên active, URL/query/path, area, thứ tự, menu đã lọc/trống, hạn chế mẫu phiếu, duplicate/cycle và DTO không bị sửa |
| Route mẫu cũ | Metadata đăng nhập, policy và GET Index/Compare/Mobile vẫn qua |
| JavaScript | `node --check` script menu mới qua |
| Tìm/đặt lại | `san pham` tìm đúng mục ở cấp ba; truy vấn không khớp hiện trạng thái rỗng; đặt lại khôi phục nhóm mở |
| Bàn phím Admin | Tab/Shift+Tab vòng trong menu; Escape xóa tìm rồi đóng; trả focus đúng nút mở và bỏ inert khỏi nội dung |
| Các fixture | Trống, một mục, cây đã lọc, thiếu tenant/user, tên chứa ký tự HTML; số lần đọc menu 1 khi đủ scope và 0 khi thiếu; không thêm điểm đến |
| Menu trống | Không có link quản lý cho người thiếu policy; fixture có policy thấy đúng `/Admin/AdminMenus` |
| Drawer POS | Header/nút đóng trong khung; menu nằm dưới header, cùng độ rộng drawer; tìm được; Escape đóng và trả focus về nút mở |
| Viewport | 320/390/768/1440 px; tên dài; input 16 px; vùng chạm từ 44 px; không tràn ngang. Mô phỏng chiều cao nhỏ không thay kiểm tra bàn phím ảo thật |
| Bảo toàn | Chỉ hai file baseline đổi: `_VerticalMenu` và `_CommonMasterLayout`; các Razor/assets nghiệp vụ khác trong baseline giữ nguyên |

Ảnh: [điện thoại](mobile-ui-20261025/evidence/ui-02ab/menu-390.jpg), [tìm mục sâu](mobile-ui-20261025/evidence/ui-02ab/search-deep-390.jpg), [320 px](mobile-ui-20261025/evidence/ui-02ab/search-320.jpg), [tablet](mobile-ui-20261025/evidence/ui-02ab/menu-768.jpg), [desktop](mobile-ui-20261025/evidence/ui-02ab/menu-1440.jpg), [POS](mobile-ui-20261025/evidence/ui-02ab/pos-drawer-390.jpg).

Dữ liệu: [thao tác](mobile-ui-20261025/evidence/ui-02ab/interaction-checks.json), [viewport](mobile-ui-20261025/evidence/ui-02ab/viewport-matrix.json), [kết quả/hash/console](mobile-ui-20261025/evidence/ui-02ab/result.json). Hợp đồng tích hợp: [UI-01c/e](MOBILE-UI-INTEGRATION-CONTRACT-20261025.md).

## Phần còn lại

SDK/runtime máy vẫn là 10.0.401/10.0.12, repo yêu cầu 10.0.402/10.0.13. Không sửa pin hoặc hạ phiên bản ứng dụng. Build được nêu là build công cụ UI độc lập với runtime đang cài; **chưa xác nhận build toàn Web, HTTP authorization thực, SQL và dữ liệu tài khoản thật**. Các bộ kiểm tra menu visibility có thao tác ghi chưa được chạy trên database thật. Cần kiểm tra các phần này trong UI-02f/UI-24 trên môi trường đúng baseline.

Bước tiếp theo **UI-02c**: lựa chọn tối đa bốn mục ghim từ menu có sẵn, lưu tham chiếu ID theo cửa hàng; tiếp tục lọc lại quyền/trạng thái/ẩn hiện khi render. Không tự ghim chức năng hoặc tạo menu mặc định. Sau đó UI-02d nâng màn Quản lý menu và ẩn/hiện, UI-02e xử lý các lối tắt POS, UI-02f nghiệm thu dữ liệu và quyền.
