# GaoApp · Thiết kế lại điện thoại v2

Ngày 25/10/2026. Theo phản hồi chưa hài lòng với bản điện thoại đầu, đã dựng lại bản mẫu thành năm màn tác nghiệp riêng, đổi màu, phân cấp nội dung, danh sách, menu, bảng trượt và chuyển động. Người dùng đã duyệt mẫu này làm chuẩn ngày 25/10/2026 và yêu cầu bắt đầu chuyển view thật theo từng bước. Tiến độ áp dụng mới nhất: [UI-02a–b · menu thật](MOBILE-UI-02AB-NAVIGATION-20261025.md). Các ảnh và kết quả ở tài liệu này giữ snapshot của lần dựng demo.

- [Xem trong khung điện thoại và so sánh desktop](http://127.0.0.1:5206/admin/ui-foundation/compare).
- [Mở bản điện thoại trực tiếp](http://127.0.0.1:5206/admin/ui-foundation/mobile).
- Route ứng dụng: `/admin/ui-foundation/mobile`, cùng controller, đăng nhập và policy `security.role.permissions` của trang nền. Không tạo mục menu.

## Cơ sở tham khảo và lựa chọn thiết kế

| Nguồn chính thức | Điều tham khảo | Cách áp dụng vào GaoApp |
|---|---|---|
| [Shopify Mobile](https://www.shopify.com/mobile) | Quản lý đơn, hàng hóa và theo dõi hoạt động cửa hàng trên điện thoại | Tổng quan ưu tiên giá trị đơn, việc chờ xử lý và đường vào tác vụ |
| [Square Dashboard app](https://squareup.com/help/us/en/article/5618-get-started-with-the-square-dashboard-app) | Truy cập tình hình kinh doanh và các khu vực quản lý qua ứng dụng mobile | Mỗi mục điều hướng có màn riêng; chỉ số và tác vụ được phân cấp rõ |
| [Sapo – ứng dụng quản lý bán hàng](https://www.sapo.vn/ung-dung-quan-ly-ban-hang.html) | Các tác vụ tìm hàng, đơn hàng, bán hàng và quản lý kho trên mobile | Thiết kế thử trọn luồng chọn hàng → giỏ; nhận hàng theo từng dòng |
| [Android – layout and navigation patterns](https://developer.android.com/design/ui/mobile/guides/layout-and-content/layout-and-nav-patterns?hl=en) | Điều hướng chính 3–5 điểm đến, điều hướng phụ và hành động chính theo ngữ cảnh | Giữ năm mục dưới theo ảnh người dùng; tách bộ lọc trạng thái khỏi điều hướng chính |

Đây là cách tổng hợp và lựa chọn thiết kế của GaoApp, không phải sao chép giao diện hay tuyên bố một mẫu là phổ biến nhất. Bảng màu navy/lam, kiểu thẻ, khoảng cách và minh họa bao gạo là phương án mới của bản demo. Font Public Sans và tất cả tài nguyên phục vụ nội bộ; không nhúng SDK, font hay ảnh từ các trang tham khảo.

## Năm màn để đánh giá

| Màn | Bố cục và thao tác đã dựng |
|---|---|
| Tổng quan | Header cửa hàng gọn; giá trị đơn lớn; đã thu/chờ thu; hai hành động nhanh; việc cần xử lý; đơn gần đây |
| Đơn hàng | Tìm tên/mã có hoặc không dấu; lọc nhanh; bảng lọc và sắp xếp; dòng đơn có khách, giá trị, mã, giờ và nhãn trạng thái; chi tiết mở từ đáy |
| Nhập hàng | Một phiếu với tiến độ theo số dòng đã kiểm; quy cách và dự kiến nằm ở từng dòng; nhập thực nhận, lỗi tại ô, cập nhật tiến độ mẫu |
| Bán hàng | Nhóm sản phẩm; tìm hàng; lưới hai cột; dấu số lượng đã chọn; tổng và nút giỏ ở trên menu; giỏ có tăng/giảm; thử biểu mẫu tiền khách đưa |
| Menu | Hồ sơ cửa hàng; nhóm điểm đến; tổng kết mẫu; tùy chọn giảm chuyển động |

Thanh dưới có icon, nhãn, vạch chỉ mục hiện hành và nền nhạt sau icon. Header và menu nằm ngoài vùng cuộn; phần nội dung cuộn riêng. Mỗi màn giữ vị trí cuộn trong phiên xem thử. Bộ lọc, dữ liệu giỏ và tiến độ kiểm giữ khi chuyển màn; tải lại trang khôi phục fixture ban đầu.

Đơn hàng có tổng giá trị **19.530.000 ₫**, đã thu **16.160.000 ₫**, chờ thu **3.370.000 ₫**. Đây là tổng giá trị của bốn đơn minh họa, không đổi tên thành doanh thu hoặc lợi nhuận. Nhận hàng dùng tiến độ **3/8 dòng**, không cộng các quy cách khác nhau thành tổng số lượng. Giỏ thử ST25 hai bao 5 kg và Jasmine một bao 10 kg có **hai dòng**, tổng **640.000 ₫**.

## Màu, chữ và chuyển động

| Nhóm | Phương án v2 |
|---|---|
| Màu chính | Navy `#192C4A`, lam `#3858DF`, hover `#2945C3` |
| Bề mặt | Canvas `#F4F6FA`, trắng, viền `#E5EAF1` |
| Chữ | Chính `#19253B`, phụ `#647188`; số tiền dùng tabular numerals |
| Phân cấp | Tiêu đề 25 px; chỉ số lớn 28–36 px; tên/giá chính 12–13 px; input 16 px; nhãn phụ gọn |
| Hình khối | Nhóm danh sách bo 17 px; thẻ tổng quan 21 px; bảng trượt 24 px; icon cùng nét |
| Vùng chạm | Nút ít nhất 44 × 44 CSS px; nút chính và ô nhập 48–50 px; radio/switch có cả hàng label để chạm |
| Chuyển màn | Fade + dịch 7 px trong 220 ms, độ trễ tối đa 60 ms cho các khối đầu |
| Bảng trượt | Dịch 30 px trong 230 ms, lớp nền mờ 180 ms |
| Phản hồi | Nhấn thu nhỏ nhẹ 100 ms; mục menu đổi màu và vạch chỉ báo; toast ngắn; nút kiểm tra mẫu có trạng thái đang xử lý |
| Giảm chuyển động | Tắt animation và transition khi người dùng bật trong demo hoặc hệ thống yêu cầu reduced motion |

Ảnh sản phẩm đang là hình bao gạo SVG để đánh giá tỷ lệ và bố cục; khi chuyển màn thật dùng ảnh dữ liệu hiện có và trạng thái ảnh dự phòng.

## Phạm vi mã và menu

- Bổ sung `UiFoundation/Mobile.cshtml`, `_CommerceMobileLayout.cshtml`, `MobilePreview.cs`, `ui-mobile.v2.css`, `ui-mobile.v2.js`. Controller và công cụ preview thêm GET `/mobile`; trang Compare mặc định mở bản v2.
- Layout điện thoại độc lập dùng CSS của demo, không nạp Bootstrap hoặc CSS gallery v1. Chỉ trang này nạp CSS/JS v2. Desktop trong Compare giữ bản tham chiếu trước để đối chiếu diện tích và mật độ; chưa đồng bộ palette mới lên desktop.
- Dùng lại native dialog từ `gaoapp.commerce.js`. Đã chỉnh scroll lock chỉ giữ gutter khi document thực sự cuộn, tránh thu hẹp khung điện thoại 15 px khi mở sheet. Đã sửa ID icon lọc trùng với dialog.
- Fixture nằm trong model riêng; JavaScript không có HTTP call, storage, camera, thanh toán thật hoặc ghi dữ liệu. Xác nhận dòng chỉ đổi tiến độ mẫu; kiểm tra thanh toán hiển thị rõ chưa thu tiền/chưa tạo đơn.
- Navigation và danh sách Menu trên trang demo là fixture để đánh giá, không phải sidebar hay màn ẩn/hiện menu thật. `AdminMenuItems`, dữ liệu cấu hình, seeder và migration không thay đổi. UI-02 phải lấy mục ghim và Menu từ cấu hình cửa hàng, đúng quyền/trạng thái/thứ tự, theo [AGENTS.md](../../AGENTS.md).

## Kiểm tra thực tế

| Kiểm tra | Kết quả |
|---|---|
| Build công cụ preview | Qua; Razor, model và controller thật được link; lượt build cuối 0 warning/0 error |
| Metadata quyền/route | GET Index/Compare/Mobile, kế thừa đăng nhập và policy hiện có đạt; không thay kiểm tra authorization HTTP toàn ứng dụng |
| JavaScript | `node --check` ba file thay đổi đạt |
| 320/360/390/430 px × năm màn | 20 trường hợp không tràn ngang; các nút hiển thị/khả dụng ít nhất 44 × 44 px; thanh menu trong viewport; input tìm kiếm 16 px |
| DOM và tài nguyên | Không trùng ID; năm màn/năm nút điều hướng/chín dialog; CSS v2 và hai script mỗi loại một lần |
| Đơn hàng | Tìm `huong viet` ra đúng một đơn; trạng thái rỗng/đặt lại; chờ thu hai đơn; đã thanh toán hai đơn; sắp giá giảm dần đúng |
| Chi tiết và bàn phím | Tên công ty dài; Tab/Shift+Tab vòng trong sheet; Escape đóng và trả focus về đơn đang xem |
| Nhập hàng | `-1` báo lỗi; xác nhận Gạo tẻ thực nhận 10 cập nhật 4/8 dòng và trả focus về đúng dòng |
| Giỏ và biểu mẫu tiền | Hai dòng tổng 640.000 ₫; tăng ST25 thành 825.000 ₫ rồi giảm lại; giữ focus nút ±; chỉ một dialog; thiếu tiền báo lỗi; đưa 700.000 ₫ ra thừa 60.000 ₫ và kết quả mẫu rõ |
| Tra mã/thông báo | Mã `893000000001` tìm ST25; thông báo chuyển sang hai đơn chờ thu |
| Chuyển động | Mặc định sheet 230 ms/backdrop 180 ms; bật giảm chuyển động cho animation `none` |
| Viewport thấp | 390 × 420 px: sheet cao 408 px, footer kết thúc tại 420 px, nội dung cuộn trong phần thân |
| Console trực tiếp | Không error/warn trên route `/mobile` trong phiên kiểm tra |
| Compare | Ba chế độ chuyển được và đúng nguồn iframe; có lỗi `MutationObserver.observe` không kèm URL khi đổi ẩn/hiện khung, đã lưu riêng. Chưa xác định nguồn; script preview không dùng MutationObserver. Không tuyên bố console của wrapper sạch |
| Baseline | Cả 560 SHA256 của 260 Razor và 300 asset ban đầu giữ nguyên |

Ảnh: [tổng quan](mobile-ui-20261025/evidence/mobile-v2/home-390.jpg), [đơn hàng](mobile-ui-20261025/evidence/mobile-v2/orders-390.jpg), [nhập hàng](mobile-ui-20261025/evidence/mobile-v2/receiving-390.jpg), [bán hàng](mobile-ui-20261025/evidence/mobile-v2/pos-390.jpg), [Menu](mobile-ui-20261025/evidence/mobile-v2/menu-390.jpg), [giỏ](mobile-ui-20261025/evidence/mobile-v2/cart-390.jpg), [bộ lọc](mobile-ui-20261025/evidence/mobile-v2/filter-390.jpg), [chi tiết](mobile-ui-20261025/evidence/mobile-v2/detail-390.jpg), [viewport thấp](mobile-ui-20261025/evidence/mobile-v2/short-height-390x420.jpg).

Dữ liệu: [ma trận kích thước](mobile-ui-20261025/evidence/mobile-v2/viewport-matrix.json), [thao tác](mobile-ui-20261025/evidence/mobile-v2/interaction-checks.json), [DOM/assets](mobile-ui-20261025/evidence/mobile-v2/dom-assets.json), [motion mặc định](mobile-ui-20261025/evidence/mobile-v2/motion-default.json), [console trực tiếp](mobile-ui-20261025/evidence/mobile-v2/console-direct.json), [console wrapper](mobile-ui-20261025/evidence/mobile-v2/console-comparison.json), [kết quả và hash](mobile-ui-20261025/evidence/mobile-v2/result.json).

Máy hiện có SDK 10.0.401/runtime 10.0.12; repo yêu cầu SDK 10.0.402/runtime 10.0.13. Bản kiểm tra dùng công cụ độc lập với phiên bản đang cài, không sửa pin của repo. Chưa xác nhận build toàn ứng dụng, kết nối dữ liệu thật hoặc kiểm thử giao dịch. Viewport trình duyệt chưa thay cho thử Safari/Chrome trên điện thoại vật lý, bàn phím ảo, safe area thực và thao tác vuốt.

UI-01c/e đã có [hợp đồng tích hợp](MOBILE-UI-INTEGRATION-CONTRACT-20261025.md); UI-02a–b đã chuyển partial menu thật. Tiếp theo UI-02c cấu hình ghim, rồi các bước menu/layout/module theo [kế hoạch tổng](MOBILE-UI-UPGRADE-PLAN-20261025.md). UI-01d còn nghiệm thu thiết bị và môi trường thực.
