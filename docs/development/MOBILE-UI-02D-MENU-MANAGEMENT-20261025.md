# UI-02d: Quản lý menu và Ẩn/hiện menu trên điện thoại

Ngày 25/10/2026. Đã viết mã; chờ người dùng chạy Visual Studio tự nghiệm thu. Lượt này không chạy app, trình duyệt, .NET build, bộ test hoặc migration; không thao tác dữ liệu cửa hàng. Chỉ kiểm tra cú pháp JavaScript và đối chiếu source.

## Phần đã đổi

Điện thoại dưới 768 CSS px dùng cùng navy/lam, nền sáng, thẻ bo góc, input 16 px và nút chạm 44–48 px của mẫu v2. CSS mới nằm riêng trong `gaoapp.menu-management.mobile.css`; các quy tắc trình bày mới giới hạn ở breakpoint điện thoại. Quy tắc ngoài breakpoint chỉ ẩn những control mới dành riêng cho mobile.

### Quản lý menu

- Header gọn, lối vào quản lý theo nhóm/nhân viên, tải lại và thêm menu theo các route hiện có.
- Chỉ số thành thẻ ngang; tìm kiếm không dấu, bộ lọc bottom sheet, phân trang và trạng thái rỗng tiếp tục dùng handler hiện tại.
- Thẻ menu có tên, menu cha, đích đến, điều kiện quyền, thứ tự và trạng thái; xem nhanh/xóa dùng modal có sẵn, trình bày thành sheet trên điện thoại.
- Bổ sung các cấp sâu trên điện thoại từ chính cây `GetForAdminAsync` trả về. Lọc có giữ đường cha và thu gọn theo tổ tiên; không giới hạn điện thoại ở hai cấp hiện có trong bảng desktop. Tablet/desktop tiếp tục dùng bộ dữ liệu hiển thị trước lượt này.
- Menu hệ thống hoặc còn con tiếp tục khóa thao tác xóa trong UI; controller/service vẫn quyết định cuối cùng. Các form xóa có antiforgery; không gọi API ghi mới.

### Thêm và sửa menu

- Vẫn mở màn riêng, dùng nguyên form, tên trường, validation, action và antiforgery hiện có.
- Trên điện thoại, chuyển cùng block xác minh quản trị xuống sau thông tin và điều kiện quyền; không tạo trường mật khẩu/lý do thứ hai. Đổi về desktop trả block về vị trí cũ.
- Thanh Lưu/Hủy đặt trên thanh ghim chung, có khoảng trống cuối trang. Ẩn ví dụ kỹ thuật phụ trên điện thoại.
- Lưu trên điện thoại mở bản xem lại tên, vị trí, đích đến, thứ tự, biểu tượng, trạng thái và điều kiện quyền; xác nhận mới gửi form thật. Không sao chép mật khẩu hoặc lý do sang bản xem lại/storage.
- Bổ sung lựa chọn menu cha sâu từ options của service hiện có. Giữ parent hiện tại và icon đang dùng kể cả icon cũ không nằm trong danh sách gợi ý; không tự đổi cấu hình khi mở form. Khi đổi kích thước, giữ lựa chọn đã chỉnh, kể cả lựa chọn sâu từ điện thoại.

### Ẩn/hiện theo nhóm hoặc nhân viên

- Điện thoại bắt đầu ở danh sách đối tượng, không tự chọn nhóm đầu tiên. Ba bước: Chọn đối tượng → Chỉnh menu → Xem trước; dùng cùng DOM và state, không dựng workspace hoặc checkbox nghiệp vụ thứ hai.
- Cây có tìm không dấu, thu/mở nhóm, checkbox hỗn hợp và nhãn đang hiện/ẩn/không khả dụng. Tìm kiếm mở đường đến kết quả; mục chưa có quyền vẫn bị khóa.
- Bản xem lại trước lưu nêu đúng đối tượng, phạm vi ảnh hưởng, kế thừa/cấu hình cá nhân, số mục trước/sau và các mục đổi trạng thái. Sau đó vẫn gửi cùng request `Type/Id/Version/HiddenIds/Reset` và token hiện có.
- Dùng lại menu nhóm, khôi phục mặc định, tải lại, giữ thay đổi chưa lưu và thông báo lỗi tiếp tục dùng logic cũ. Lỗi lưu không thay state thành thành công hoặc làm mất lựa chọn. Nút lưu giữ submit guard hiện có.
- Máy tính vẫn tự chọn đối tượng đầu tiên và hiển thị các vùng cạnh nhau; không thêm bước xác nhận mới vào thao tác lưu trên máy tính.

## Bảo toàn quyền và desktop

Không đổi controller, DTO nghiệp vụ, service menu, service ẩn/hiện, policy, xác minh mật khẩu, rate limit, cách xác định StoreId/UserId hoặc schema. Không tạo menu bằng seed/migration/script; không thêm mục giả vào cây Ẩn/hiện. ID menu, đối tượng và quyền lấy từ dữ liệu controller/service đang cung cấp.

Đối chiếu snapshot đầu lượt xác nhận:

- Header và toàn bộ block bảng desktop `AdminMenus/Index` giữ nguyên source.
- Nội dung form gốc Create/Edit giữ nguyên source; phần mới chỉ là CSS, review và metadata lựa chọn sâu từ cùng options.
- Hai file CSS gốc `admin-menu-index.css`, `admin-menu-visibility.css` giữ nguyên hash.
- Hai controller và hai service liên quan giữ nguyên hash.
- Ba file JS qua kiểm tra cú pháp `node --check`. Chưa build Razor/.NET hoặc chạy test/app, nên đây là bằng chứng tĩnh, không phải xác nhận giao diện đã qua nghiệm thu.

Bằng chứng: [static-review.json](mobile-ui-20261025/evidence/ui-02d/static-review.json). Baseline `view-inventory.json` không sửa; snapshot trước lượt nằm trong `.artifacts/ui-02d/before/`.

## Bạn tự kiểm tra bằng Visual Studio

1. Mở `GaoApp.sln`, chọn `GaoApp.Web`, chạy F5. Không cần migration mới cho UI-02d. Dùng tài khoản có quyền quản lý menu của đúng cửa hàng; thêm/sửa/xóa vẫn theo kiểm tra quản trị hiện hành.
2. Mở `/Admin/AdminMenus`, đặt viewport 390 px. Kiểm tra header, thẻ chỉ số, tìm tên không dấu, lọc/tải lại/phân trang và xem nhanh. Thử 320 px, tên/URL dài, cây trên hai cấp, thu gọn cha và tìm mục con sâu.
3. Tạo hoặc sửa một mục thử trong cửa hàng thử: nhập đủ thông tin, chọn menu cha, quyền và thứ tự; Lưu phải hiện bản xem lại. Quay lại chỉnh phải giữ dữ liệu; xác nhận mới gửi. Kiểm tra lỗi validation, lý do/mật khẩu xác minh theo yêu cầu của menu có điều kiện quyền. Thử quay lại trang bằng nút Back của trình duyệt.
4. Với menu có parent sâu hoặc icon cũ, mở form và lưu thử trên môi trường thử: những giá trị này phải được giữ nếu không chỉnh. Xoay/đổi chiều rộng khi đang nhập không được mất lựa chọn.
5. Mở `/Admin/AdminMenuVisibility`. Điện thoại phải bắt đầu bằng bước chọn đối tượng. Chọn nhóm/nhân viên, tìm và chỉnh cây, đổi sang Xem trước rồi bấm Xem lại và lưu. Kiểm tra phạm vi ảnh hưởng, quay lại chỉnh và xác nhận.
6. Thử nhân viên dùng menu nhóm, cấu hình cá nhân, nút Dùng nhóm/Mặc định, mục thiếu quyền, ẩn nhóm cha, danh sách trống và tên dài. Đổi đối tượng hoặc tải lại khi chưa lưu phải giữ bước xác nhận bỏ thay đổi hiện có.
7. Hai tab chỉnh cùng đối tượng: lưu tab thứ nhất rồi lưu tab cũ phải báo xung đột; không báo thành công giả. Thử mất mạng để kiểm tra giữ lựa chọn. Đăng nhập cửa hàng khác: menu/đối tượng không lẫn nhau; tài khoản thiếu quyền vẫn bị server từ chối.
8. Đổi về 768/1440 px: kiểm tra giao diện máy tính/tablet đang có, bảng và form; không hiện thanh bước hay review dành cho điện thoại. Kiểm tra thanh ghim, menu toàn màn hình, bàn phím và safe area không che thao tác.

UI-02d giữ trạng thái **đã viết mã, chờ tự nghiệm thu**. UI-02e/f và UI-03 chưa được làm trong lượt này.
