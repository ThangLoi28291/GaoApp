# UI-03a · Khung ứng dụng điện thoại

Ngày 25/10/2026. **Đã viết mã và đối chiếu tĩnh, chờ người dùng nghiệm thu trong Visual Studio.** Không chạy app, browser, build, bộ test hoặc migration trong lượt này. Việc chuyển sang UI-03a không tự đánh dấu UI-02c–f đã đạt nghiệm thu runtime.

## Phần đã áp dụng

Ba layout `_CommonMasterLayout`, `_ContentNavbarLayout`, `_POSPrimeLayout` có hợp đồng chung về chủ sở hữu header và dock. Giao diện mới chỉ áp dụng **dưới 768 px**. Desktop/tablet giữ các khai báo CSS và vị trí control hiện tại.

| Ngữ cảnh | Header trên điện thoại | Chủ sở hữu vùng đáy |
|---|---|---|
| Trang quản trị thông thường | `_MobileAppHeader`: Menu, tên cửa hàng, tiêu đề trang và tài khoản hiện có | Thanh ghim + Menu từ UI-02; không tạo đích mới |
| Trang quản trị đặt `isMenu=false` | Cùng header, không tự thêm nút Menu | Không tự tạo thanh ghim |
| POS Prime | Giữ `_POSPrimeHeader`, account ở slot hiện có | POS giữ checkout dock; ghim vẫn ở drawer, không thành thanh đáy chung |
| POS legacy | Giữ `_CockpitHeader`, thêm nút Menu chỉ hiện trên điện thoại; account chuyển vào header đó | POS giữ các vùng thanh toán/tác vụ; ẩn thanh ghim chung để tránh tranh vùng đáy |
| Chi tiết nhập hàng | Giữ `_ReceivingApp` với Menu/tiêu đề/Thao tác; bổ sung slot cho account | Nhập hàng giữ dock nhận hàng và hai tab Hàng nhập/Thông tin |

Header quản trị dùng nền trắng, viền nhạt, nút Menu 44 px, tiêu đề navy và nền nội dung theo token v2. Header sticky theo cuộn document; không tạo một vùng scroll mới cho toàn bộ nghiệp vụ. Tên cửa hàng rút gọn một dòng, tiêu đề tối đa hai dòng; giá trị đầy đủ vẫn ở DOM/title và nội dung trang hiện có.

Các trang quản trị có vùng nội dung rộng theo màn hình, khoảng đệm gọn và hàng heading có thể xuống dòng. Các trang chủ động dùng layout `d-flex/p-0` giữ kiểu nội dung đó. Heading/form/bộ lọc/bảng của từng module vẫn được giữ, kể cả heading riêng trong body; việc chuyển đầy đủ các màn thuộc UI-04 trở đi.

## Một control, không dựng lại tài khoản

`gaoapp.mobile-shell.js` chạy sau script tài khoản và menu hiện có. Script chuyển **nguyên node account/tiện ích**, dùng marker để trả về đúng vị trí desktop khi vượt breakpoint. Không clone control, không tạo form đăng nhập/đổi mật khẩu thứ hai, không sao chép dữ liệu form hay storage.

Account chỉ được chuyển khi script tài khoản đã khởi tạo và hiện control. Nếu chưa khởi tạo, vị trí cũ không bị ép ẩn. Khi đổi breakpoint lúc popup tài khoản đang mở, adapter gọi nút tài khoản hiện có để handler của nó đóng popup, rồi chuyển node/khôi phục focus. Drawer hồ sơ/mật khẩu và Bootstrap vẫn giữ chủ sở hữu hiện tại, không bị dựng lại.

Tên cửa hàng trong header lấy bằng `textContent` từ profile đã render bởi `EmployeeAccountService`, không thêm query hoặc dựng dữ liệu cửa hàng giả. Nếu không có profile, header giữ nhãn thương hiệu; không tự suy đoán cửa hàng/tài khoản.

Toolbar cũ chỉ ẩn trên điện thoại sau khi account đã được chuyển, đồng thời giữ các tiện ích còn ở vị trí cũ. Nút Menu nổi cũ được ẩn vì header hiện tại đã có nút Menu. Tablet/desktop không áp dụng các quy tắc này.

## Tích hợp và bảo toàn

- `ViewData["MobileShellOwner"]` được khai báo tại POS/Index và WarehouseReceiving/Detail; các layout chuẩn hóa thành `admin`, `pos-legacy`, `pos-prime`, `receiving`. `MobileDockOwner` là `navigation`, `pos`, `receiving` hoặc `none`. Đây là metadata trình bày, không quyết định quyền hoặc dữ liệu nghiệp vụ.
- `_CommonMasterLayout` nạp một lần `gaoapp.mobile-shell.css` sau `PageStyles`, và `gaoapp.mobile-shell.js` sau script điều hướng chung. Các section, thư viện và thứ tự loader cũ còn nguyên.
- Phần CSS ngoài media chỉ ẩn **các control mới**. Mọi rule thay giao diện nằm trong `max-width: 767.98px`; không thay Bootstrap/token toàn cục.
- Nút Menu dùng lại `.layout-menu-toggle`/`layout-menu`; POS Prime tiếp tục dùng Bootstrap `posPrimeAdminDrawer`. `aria-expanded` của nút Menu mới đồng bộ với trạng thái hiện có.
- Không có URL chức năng mới trong header, không tạo/sửa menu bằng seed/migration/dữ liệu. Menu, quyền và cấu hình cửa hàng vẫn do nguồn hiện có cung cấp.
- Không sửa service/controller, payload lưu, CSRF, giỏ hàng, số tiền, command/retry, scanner, dialog số lượng, adapter POS/nhập hàng hay handler tài khoản.

Phối hợp bàn phím ảo/visual viewport, safe area đầy đủ, trợ lý/toast và lớp phủ thuộc **UI-03b**. UI-03a mới đưa hợp đồng header/dock và nền bố cục vào các layout, chưa xác nhận những ca đó trên thiết bị thật. Riêng breakpoint nhận hàng cũ 768 px vẫn được giữ; cần đối chiếu ranh giới 768 px ở UI-03b thay vì tự sửa module trong bước này.

## Bằng chứng đã có

[static-review.json](mobile-ui-20261025/evidence/ui-03a/static-review.json) so với `.artifacts/ui-03a/before/`:

- `node --check` đạt cho `gaoapp.mobile-shell.js`.
- **19 file bảo vệ giữ hash**, gồm CSS/JS tài khoản và giao dịch, nguồn điều hướng/menu cùng baseline.
- **8 file Razor thay đổi được đối chiếu toàn phần**: ngoài metadata, header/slot/control mới và hai import CSS/JS đã khai báo, source còn lại giống đầu lượt. ID tĩnh cũ không đổi, các section và đoạn nghiệp vụ không bị bỏ.
- CSS mới chỉ ẩn control mới ở ngoài breakpoint; toàn bộ thay đổi thị giác thuộc điện thoại. Adapter không có network/storage/clone/submit nghiệp vụ.
- [Baseline delta](mobile-ui-20261025/evidence/ui-03a/baseline-delta.json): 560 file cũ, **540 không đổi, 20 đổi, 0 mất**; tính cả các bước trước. Có thêm 3 file mới cho header/CSS/JS, không thay baseline gốc.

Đây là bằng chứng source, không phải kết quả build Razor hoặc kiểm tra giao diện runtime. Không ghi nhận ca nào đã chạy trên app ở lượt này.

## Bạn chạy Visual Studio để nghiệm thu

Chạy Web bằng **Visual Studio → F5**. Thử 320/390/430 px, rồi 768/1024/1366 px:

1. `/admin`, Quản lý menu, Ẩn/hiện menu và `/admin/pos/orders-page`: có một header ứng dụng trên điện thoại; Menu mở đúng cây cửa hàng, tên cửa hàng đúng profile, tài khoản hoạt động.
2. Cuộn trang dài: header giữ vị trí, nội dung/form/bộ lọc vẫn thao tác được, thanh ghim đúng cấu hình. Tiêu đề dài không đẩy header phủ hết màn hình.
3. Mở trang có `header` section và trang chỉ có body: phần heading/action hiện có không bị mất; không có lỗi section Razor. Trang chủ động `isMenu=false` không nhận nút Menu giả.
4. `/admin/pos` (Prime) và `/admin/pos/legacy`: không thêm header quản trị thứ hai; Menu/tài khoản/giữ đơn/thanh toán dùng handler cũ, thanh ghim không đè dock.
5. Một phiếu `/admin/warehouse-receiving`: account xuất hiện cạnh thao tác đầu màn; Menu, sheet, dock, hai tab và dialog số lượng vẫn dùng được.
6. Bật popup tài khoản rồi đổi điện thoại ↔ desktop: control trở về đúng slot, không bị nhân đôi, popup cũ đóng hợp lý; kiểm tra tiếp khi drawer hồ sơ/mật khẩu đang mở và dữ liệu đang nhập còn được giữ.
7. Trên desktop/tablet: header và nút Menu mới không hiển thị, tài khoản giữ vị trí cũ, các bố cục/bảng/dock không bị đổi.
8. Đổi cửa hàng/đăng nhập tài khoản khác theo luồng hiện có: request mới lấy đúng profile và menu; không có menu hay dữ liệu mẫu tự bổ sung.

Chưa cần migration mới. Sau phản hồi bước này, tiếp tục **UI-03b: safe area, bàn phím, chiều cao trình duyệt, cuộn/focus và tránh chồng trợ lý/toast/dock**.
