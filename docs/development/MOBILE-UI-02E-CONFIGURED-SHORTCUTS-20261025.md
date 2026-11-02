# UI-02e · Điều hướng POS và lối tắt theo menu cửa hàng

Ngày 25/10/2026. Trạng thái: **đã viết mã, chờ người dùng chạy Visual Studio nghiệm thu**. Không chạy app, browser, build, bộ test hay migration trong lượt này.

## Kết quả

`_POSNavTabs` và các lối tắt vận hành dưới đây dùng cùng cây menu đã lọc với sidebar. Chỉ mục có trong `AdminMenuItems` của cửa hàng hiện tại, còn hoạt động, còn quyền và không bị ẩn mới có đường đi tắt. Không thêm menu bằng code hoặc dữ liệu, không lấy menu cửa hàng khác khi cây trống.

Không thiết kế lại desktop: giữ CSS, khung trang, dạng nút/dropdown và các vùng nghiệp vụ hiện tại. Nội dung điều hướng (tên, biểu tượng, số mục, nhóm và thứ tự) nay phản ánh cấu hình thực tế; mục không được phép sẽ biến mất trên các lối tắt. Đây là thay đổi nguồn điều hướng, không phải cam kết ảnh chụp desktop giống hệt khi cấu hình/quyền khác nhau.

| Vùng | Cách lấy nội dung |
|---|---|
| `_POSNavTabs` tại 8 trang ca/đơn/dashboard/yêu cầu | Lọc các đích POS/ca từ cây menu thật; giữ nhóm, thứ tự và nhiều tầng. Không còn danh sách các chức năng POS viết cố định |
| Menu “thêm” của POS Prime và legacy | Cùng cây POS, cộng mẫu hóa đơn đã cấu hình và đủ quyền quản trị; không hiện dropdown rỗng |
| Lối tắt trong POS Dashboard | Các đích POS đủ điều kiện, bỏ đích hiện tại và popup màn hình khách |
| Trang chủ `/admin` | Tối đa 4 ID đã ghim tại UI-02c, đúng thứ tự; đối chiếu lại cây hiện tại. Không tự chọn 4 chức năng thay thế. Chưa ghim thì có nút mở menu |
| Lối đến POS trong menu tài khoản | Cần `CanUsePos` và mục bán hàng đã cấu hình, còn hiển thị; dùng tên, icon và URL của mục đó |
| Logo sidebar và chân drawer POS | Chỉ dẫn tới trang chủ đã cấu hình và đủ quyền; không còn đường mặc định đi vòng menu ẩn |
| Chi tiết nhập hàng trên điện thoại | Nút đầu màn mở menu cửa hàng. Lối về danh sách trong sheet chỉ xuất hiện khi mục danh sách đủ điều kiện; nút Menu đóng sheet xong mới mở sidebar |

Không sửa bàn phím tắt POS, giữ đơn, giỏ, thanh toán, context/money sheet, dock nhận hàng, dialog số lượng, retry hay lưu chứng từ. Mẫu demo `Commerce/_BottomNavigation` và `UiFoundation` vẫn là fixture.

## Nguồn dữ liệu và quyền

`GaoApp.Web/Services/AdminNavigationProvider.cs` là dịch vụ **scoped theo request**, đăng ký tại Web `Program.cs`. Các partial dùng chung một snapshot, không cache menu giữa người dùng/cửa hàng hoặc giữa các request.

1. Đọc tenant và user hiện tại; thiếu định danh thì trả cây rỗng.
2. Dùng `IAdminMenuService.GetForRenderAsync(storeId, userId)`, giữ nguyên lọc trạng thái, quyền cấu hình, ẩn/hiện, cây cha và thứ tự của service hiện có.
3. Dùng lại `AdminNavigation.Build`, không sửa DTO hoặc thêm đích. Quy tắc riêng mẫu hóa đơn dành cho quản trị viên được giữ.
4. Đối chiếu thêm policy đọc đơn POS, xem ca và xem trang chủ; hai trang phiếu nhận ca/duyệt ca còn cần quản trị viên cửa hàng. Việc này tránh menu thiếu/sai `PermissionCode` tạo lối tắt gây 403. Controller/service vẫn giữ toàn bộ kiểm tra quyền thực tế.
5. Nhận diện workspace bằng tiền tố URL có ranh giới đoạn đường dẫn và `PathBase`. Các chuỗi route trong provider chỉ phân loại đích đã tồn tại; không được xuất thành link dự phòng. URL/query, tên và icon xuất ra đều lấy từ node cấu hình.

Mục ghim trang chủ đọc lại qua `IAdminMenuPinService.GetConfiguredIdsAsync`, tối đa 4 và giao với snapshot hiện tại. Sidebar, thanh ghim điện thoại và lối tắt không cấp quyền mới, không tạo dữ liệu mới. Không có migration mới.

## Tích hợp sự kiện

`gaoapp.configured-shortcuts.js` nạp một lần từ `_CommonMasterLayout`, sau menu chung. Popup màn hình khách dùng `data-configured-customer-display` và `data-url` đã chiếu từ menu; bỏ fallback URL trong JavaScript. Giữ kích thước/tên cửa sổ và thông báo chặn popup hiện tại.

Selector cũ `btnOpenCustomerDisplay` được thay bằng thuộc tính data để hỗ trợ mục động và tránh ID trùng khi người dùng cấu hình nhiều mục. Binder cũ `bindCustomerDisplay` trong `pos.cockpit.js` đã bỏ; cả Prime, legacy và `_POSNavTabs` dùng duy nhất handler chung. Hai contract test nguồn liên quan đã cập nhật, **chưa chạy**.

Với nhận hàng, `main.js` vẫn sở hữu `.layout-menu-toggle`; handler mới chỉ chờ `hidden.bs.modal` rồi bấm nút menu đầu màn. Sidebar hiện có tiếp tục giữ focus, Escape và khóa cuộn. Bootstrap tiếp tục sở hữu drawer của POS. Không có thanh menu mới đè lên dock giao dịch.

## Phạm vi rà soát còn phân biệt

- `_ContentNavbarLayout` thực tế dùng `_AdminToolbar`; toolbar không có danh sách chức năng cố định. `_AdminLayout` và cặp template `_Navbar`/`_NavbarPartial` còn trong source nhưng không được layout/view hiện tại gọi; không thay template không dùng trong bước này.
- Navigation bên trong workspace báo cáo, liên kết cấu hình ACB, liên kết chứng từ/in tem và các nút tác vụ của từng module thuộc bước nâng module tương ứng; không biến các tác vụ đang xử lý chứng từ thành mục menu chung.
- Không coi UI-02f (nghiệm thu quyền/cửa hàng/cây dài trên runtime) đã hoàn thành chỉ vì đối chiếu source.

## Kiểm tra đã làm

[Bằng chứng tĩnh](mobile-ui-20261025/evidence/ui-02e/static-review.json) đối chiếu với snapshot đầu lượt tại `.artifacts/ui-02e/before/`:

- `node --check` đạt cho JS lối tắt mới và `pos.cockpit.js`.
- 12 file bảo vệ giữ nguyên hash: CSS POS/nhập hàng, adapter POS Prime, JS nhận hàng, controller POS/ca quản trị, service menu/ẩn hiện và baseline kiểm kê.
- Hai header POS giữ nguyên toàn bộ ngoài khối menu “thêm”; JS cockpit giữ nguyên ngoài binder popup bị chuyển chủ sở hữu.
- Dock và dialog số lượng nhập hàng giữ nguyên source. Class desktop của nút/menu “thêm” được giữ.
- Các view lối tắt đã chuyển không còn `href`/`data-url` chức năng `/admin/...` viết cố định. Snapshot chỉ đọc menu qua `GetForRenderAsync`.
- [Đối chiếu baseline](mobile-ui-20261025/evidence/ui-02e/baseline-delta.json): 560 file cũ, **543 không đổi, 17 đổi, 0 mất**; bao gồm các thay đổi UI-02a–d trước đó. Hash gốc không thay.

Đây là bằng chứng source và cú pháp JS; Razor/C# chưa build, hành vi trình duyệt và dữ liệu quyền chưa chạy thử trong lượt này.

## Người dùng tự kiểm tra trong Visual Studio

Chạy project Web bằng Visual Studio/F5, xem cả điện thoại 390/430 px và desktop 1366 px:

1. Mở `/admin`, `/admin/pos`, `/admin/pos/legacy`, `/admin/pos-shift`, `/admin/pos-shift/history`, `/admin/pos/orders-page`, `/admin/pos/dashboard`, `/admin/pos-shift/requests`. Thanh điều hướng chỉ lấy các mục tương ứng đã cấu hình; cây trống không được tự bổ sung chức năng.
2. Đổi tên/icon/thứ tự một mục có sẵn; ẩn mục hoặc nhóm cha, ngừng hoạt động mục; tải lại và đối chiếu sidebar, POS tabs, menu “thêm”, tài khoản và lối tắt trang chủ.
3. Đăng nhập nhân viên hạn chế quyền và quản trị viên: mục thiếu quyền không xuất hiện; phiếu nhận ca/duyệt ca/mẫu hóa đơn giữ điều kiện riêng. Đổi cửa hàng A/B để kiểm tra không dùng lại menu/ghim của cửa hàng trước.
4. Kiểm tra 0–4 mục ghim và thứ tự trên trang chủ/thanh đáy. Không có chức năng tự lấp chỗ của mục ẩn hoặc thiếu quyền.
5. Nếu cửa hàng đã có mục màn hình khách: mỗi lần bấm chỉ mở một popup đúng URL cấu hình. Mẫu hóa đơn vẫn mở tab mới khi đủ điều kiện.
6. Mở một phiếu tại `/admin/warehouse-receiving`: bấm Menu đầu màn và Menu trong sheet; sheet phải đóng trước khi sidebar mở, đóng menu trả focus hợp lý. Kiểm tra dock, nhập số lượng và quay về danh sách đã cấu hình.

Sau phản hồi nghiệm thu UI-02e, bước tiếp theo là UI-02f. Không tự chạy thêm app hoặc cập nhật database.
