# Thanh điều hướng điện thoại — bốn phần ưu tiên

Ngày 28/10/2026. Bổ sung theo lựa chọn của người dùng: **Tổng quan · Đơn hàng · Nhập hàng · Bán hàng · Menu**.

Khi tài khoản chưa có mục ghim riêng khả dụng, thanh dưới chọn bốn đích thật từ menu cửa hàng theo thứ tự trên. Mục thiếu trong menu, ngừng hoạt động, bị ẩn hoặc thiếu quyền được bỏ qua. Không thêm menu giả, không tạo menu/seeder/migration và không tự ghi AdminMenuPinSettings. Nếu đã ghim riêng thì giữ lựa chọn, thứ tự và tối đa bốn mục như trước.

Đơn hàng dùng menu trỏ đến trang /admin/pos/orders-page; /admin/pos/orders là API nên không lấy làm đích trang. Giữ nguyên href/query/PathBase, icon và ID của mục nguồn; nhãn ngắn chỉ áp dụng trên thanh điện thoại. Dấu chọn theo phần hiện tại, kể cả trang chi tiết đơn/phiếu nhập. Sidebar giữ nguyên nhãn, thứ tự và markup.

Thanh được render ngay từ server, vì vậy không còn phải chờ API ghim để thấy các phần chính. API vẫn kiểm tra lại menu/quyền theo request; client chỉ tham chiếu mục tồn tại trong sidebar hiện tại. Màn xem trước mục ghim trên điện thoại cũng hiển thị các phần mặc định khi chưa chọn riêng và lọc đúng đối tượng vai trò. Desktop của màn này giữ hành vi cũ.

Kiểm thử: **42/42 ca .NET duy nhất, 3/3 ca Node**, không có lỗi. Native Chrome/SQL đã thử: không có pin, API ghim 503, mở/đóng Menu, trang đơn hàng đang chọn, tài khoản chỉ xem đơn, ẩn menu cha, cửa hàng khác, pin riêng và xem trước vai trò. Điện thoại 320/390/600/767px và 390×440; nút ít nhất 44px, không tràn ngang. Đối chiếu CSS/hình học sidebar/nội dung ở 768/1440px với stylesheet trước sửa đều khớp. Đã xem trực quan bảy ảnh cuối.

SQL fixture xác nhận render không thay menu, store, role/grant, visibility hay pin configuration. Trước khi kiểm tra pin riêng, test chủ ý tạo một setting trong database GUID riêng; sau đó đọc/render không thay giá trị. MigrateAsync thật, không pending model/migration. Không cập nhật database cửa hàng đang dùng và giữ Chrome đang mở; chỉ dùng profile headless riêng.

Build dùng toàn bộ 2075 C# production hiện tại, không shadow/filter source; ba asset và gzip SDK khớp nguồn. Không đổi source Home desktop, business controller/service, renderer in hoặc schema. Còn cảnh báo xUnit2031 có sẵn ở InvoiceNotificationTransportTests. Giữ 47 verification lịch sử; cập nhật hash mới của V207 và giữ bản ghi bị thay thế trong addendum. Bước kế hoạch vẫn là UI-23a.

Các màn POS/nhập hàng đang có thanh thao tác riêng tiếp tục giữ thanh đó, navigation trong drawer cũ. Chưa thử Android/iOS và safe area trên máy thật. Chỉ có đủ bốn phần khi menu và quyền của tài khoản cho phép.

[Ảnh trang chủ](../../TestResults/mobile-primary-nav-20261028/default-home-phone.png), [ảnh đơn hàng](../../TestResults/mobile-primary-nav-20261028/default-orders-phone.png), [ảnh xem trước vai trò](../../TestResults/mobile-primary-nav-20261028/editor-role-preview.png), [bằng chứng](mobile-ui-20261025/evidence/mobile-primary-navigation/runtime-review.json).

Bổ sung icon theo yêu cầu tiếp theo ngày 28/10/2026: riêng thanh dưới điện thoại dùng biểu tượng hóa đơn cho Đơn hàng và kiện hàng cho Nhập hàng, kể cả khi icon menu nguồn là vòng tròn mặc định. Hai SVG dùng lại từ bộ icon hiện có, chỉ override CSS dưới 768px; không sửa icon lưu trong menu hoặc sidebar/desktop. Đã kiểm tra render CSS tại 320/390/767/768/1440px và xem ảnh. Không chạy lại bộ .NET cho thay đổi hai mask CSS này.
