# UI-02f · Trường hợp biên của menu và mục ghim

Ngày 25/10/2026. **Đã sửa mã và hoàn thành rà soát tĩnh; còn chờ người dùng build/chạy Visual Studio để nghiệm thu.** Không chạy app, browser, build, bộ test hoặc migration trong lượt này. Chưa đánh dấu toàn bộ UI-02 đã được nghiệm thu.

## Các điểm đã sửa

### Menu nhiều tầng và tiêu đề dài trên điện thoại

Thêm `gaoapp.navigation-edge-cases.mobile.css`, nạp một lần từ `_CommonMasterLayout`. Tất cả rule mới nằm trong `max-width: 767.98px`:

- Từ tầng lồng thứ hai, menu sidebar không cộng thêm khoảng thụt lề làm mất dần vùng đọc. Cấu trúc, thứ tự và dữ liệu menu không đổi.
- Tên nhóm/nút POS dài được xuống dòng, kể cả chuỗi không có khoảng trắng; icon không bị ép mất. Selector mới đủ độ ưu tiên so với CSS POS nạp trong partial.
- Dropdown POS dài có chiều cao tối đa theo viewport, cuộn dọc và xuống dòng tiêu đề. Bootstrap vẫn sở hữu vị trí, focus và bàn phím.

Hai partial POS chỉ thêm thuộc tính `data-configured-pos-menu` để chọn đúng vùng. CSS desktop/tablet cũ và markup của chúng ngoài thuộc tính này giữ nguyên. Không thay palette, bố cục hay hành vi dock giao dịch ở bước này.

### Phản hồi tải ghim đến muộn

`gaoapp.mobile-pins.js` đối chiếu đúng request đang chờ, trạng thái hủy và breakpoint trước/sau khi đọc JSON. Vì vậy phản hồi của lượt cũ không được vẽ đè lượt mới sau khi đổi điện thoại → desktop → điện thoại. Sự kiện lưu ghim cũng hủy lượt GET đang chờ trước khi vẽ kết quả vừa lưu.

Ghim vẫn giao ID với menu thật đã render, chỉ nhận ID nguyên dương, khác nhau và tối đa 4. Không đổi URL/icon/tên thành danh sách mặc định. Nếu cập nhật làm mất anchor đang focus, chuyển focus tới mục tương ứng còn tồn tại hoặc nút Menu/đóng drawer. Lần đọc lỗi có thể thử lại khi người dùng chủ động mở menu hoặc thiết bị có mạng trở lại; không có timer polling.

## Các ranh giới quyền và cửa hàng đã đối chiếu source

Không sửa controller, service menu/ẩn hiện/ghim, policy, quyền, session, role hay schema. Menu và các lối tắt vẫn dùng `AdminNavigationProvider` scoped theo request và `GetForRenderAsync`; request mới đọc lại cây đủ điều kiện. Mục ghim vẫn đi qua guard cửa hàng/người dùng hiện tại và lọc lại quyền/ẩn hiện tại service.

Menu trống không dùng cửa hàng khác làm dự phòng; không thêm chức năng để lấp chỗ ghim. Cha bị ẩn/thiếu quyền/ngừng hoạt động không đưa hậu duệ lên thành menu gốc. Cây render và cây quản trị không bị tráo nguồn.

`employee-account.js` hiện có guard `pageshow` yêu cầu reload khi trang được phục hồi từ Back/Forward cache; không thêm một handler reload thứ hai. Cần tự kiểm tra guard này cùng session/quyền trên trình duyệt thật. Trang đã mở không tự nhận mọi thay đổi quản trị từ tab khác theo thời gian thực; kiểm tra cấu hình mới sau request/tải lại hoặc đăng nhập lại theo cơ chế session hiện có.

## Ca kiểm tra đã chuẩn bị, chưa chạy

Hai file test mới dùng dữ liệu trong bộ nhớ và service double, không khởi động host/app và không ghi database:

| File | Phạm vi |
|---|---|
| `GaoApp.Tests/Ui/AdminNavigationEdgeCaseTests.cs` | Menu trống/một mục, node không còn đích, cha ngừng hoạt động, tên Unicode dài, cây 64 tầng và 200 mục ngang, chu trình/ID trùng, query/PathBase, cha ẩn/thiếu quyền, lọc cây POS không biến đổi sidebar, quy tắc mẫu hóa đơn |
| `GaoApp.Tests/Ui/AdminNavigationProviderTests.cs` | Thiếu store/user, policy POS/ca/trang chủ và quyền quản trị viên, một snapshot trong một request, ghim đúng thứ tự và không lấp chỗ, request mới sau đổi quyền/cửa hàng/cây đã lọc |

Đếm từ source: 12 phương thức, 22 ca dự kiến sau khi mở rộng `InlineData`; **chưa compile/discover/chạy test**. Các bộ có sẵn `AdminMenuPinRulesTests`, `AdminMenuPinSqlServerTests`, `AdminMenuVisibilitySqlServerTests` và `AdminMenuPermissionTenantIsolationTests` vẫn cần người dùng chạy nếu muốn nghiệm thu tự động. Nhóm SQL Server dùng test fixture riêng, không thay bằng dữ liệu thật của cửa hàng.

## Bằng chứng tĩnh

[static-review.json](mobile-ui-20261025/evidence/ui-02f/static-review.json) so với snapshot đầu lượt `.artifacts/ui-02f/before/`:

- `node --check` đạt với `gaoapp.mobile-pins.js`.
- 24 file bảo vệ giữ nguyên hash, gồm các controller/service/rule quyền-cửa hàng, provider/projection, CSS desktop hiện hữu, handler menu/popup/tài khoản và baseline gốc.
- Markup hai dropdown POS chỉ thêm data hook; layout chung chỉ thêm một import CSS. Tất cả khai báo CSS mới thuộc breakpoint điện thoại.
- Source có kiểm tra định danh request cả trước/sau `response.json()`, hủy GET trước khi vẽ ghim vừa lưu; không có vòng polling.
- [Baseline delta](mobile-ui-20261025/evidence/ui-02f/baseline-delta.json) vẫn là 560 file gốc: **543 không đổi, 17 đổi, 0 mất**. Con số bao gồm các bước trước; file/test mới ghi riêng, không sửa hash gốc.

Đối chiếu source không xác nhận giao diện thật, tốc độ hoặc kết quả quyền/SQL. Các ca runtime được giữ trạng thái `pending`.

## Bạn chạy Visual Studio tự kiểm tra

Chạy Web bằng **Visual Studio → F5**, xem `/admin`, `/admin/pos`, `/admin/pos-shift`, `/admin/pos/orders-page`, `/admin/pos/dashboard`, màn quản lý/ẩn hiện/ghim menu và một phiếu `/admin/warehouse-receiving`.

Thử điện thoại 320/390/430 px và tablet/desktop 768/1024/1366 px. Dùng menu/tài khoản kiểm thử được phép; mọi thay đổi menu do bạn thực hiện qua chức năng quản lý hiện có, không có seed/script tạo menu từ bước này.

[Ma trận 22 tình huống](mobile-ui-20261025/evidence/ui-02f/acceptance-matrix.json) ghi từng kỳ vọng, toàn bộ chưa chạy. Các nhóm chính:

| Nhóm | Cách kiểm tra và kết quả cần thấy |
|---|---|
| Trống/một mục/cây dài/tên dài | Không có đích tự sinh; thứ tự đúng; đọc được nhãn sâu/dài và chạm được mục cuối, không tràn ngang |
| Trạng thái/quyền/ẩn hiện | Tắt/ẩn cha hoặc bỏ quyền rồi tải lại: sidebar, lối tắt và ghim cùng loại mục đó; thao tác trực tiếp vẫn qua controller/service |
| Nhóm/cá nhân/cửa hàng | Đổi nhóm hoặc đăng nhập cửa hàng khác; kiểm tra kế thừa/ghi đè và request mới, không dùng menu/ghim của ngữ cảnh trước |
| 0–4 ghim | Giữ thứ tự đã chọn; mục mất quyền không có mục khác tự lấp vào; Menu vẫn dùng được |
| Mạng chậm/lưu/đổi kích thước | Trong DevTools, làm chậm GET `/admin/menu-pins/configured`, đổi breakpoint hoặc lưu ghim khi GET còn chờ; kết quả đến muộn không được ghi đè lượt mới/kết quả vừa lưu |
| Lỗi mạng/focus | Lần tải ghim lỗi vẫn mở được Menu; có mạng/mở Menu cho phép thử lại; dùng Tab/Escape và cập nhật ghim không làm mất focus điều khiển |
| Giao dịch/desktop | POS/nhận hàng giữ dock riêng, popup mở một lần; desktop/tablet không áp dụng CSS mới; kiểm tra thêm Back/Forward cache và tìm có/không dấu |

Trong Visual Studio Test Explorer có thể chọn riêng hai lớp test mới trước; các bộ quyền/SQL hiện có là đợt kiểm tra riêng. Không có kết quả “pass” được ghi trước khi bạn chạy.

Sau phản hồi UI-02f, bước tiếp theo theo kế hoạch là **UI-03a: khung ứng dụng, header/vùng nội dung và phối hợp dock trên điện thoại**. Các bước UI-02c–f còn chờ nghiệm thu runtime; không tự suy ra đã đạt từ việc chuyển sang bước mới.
