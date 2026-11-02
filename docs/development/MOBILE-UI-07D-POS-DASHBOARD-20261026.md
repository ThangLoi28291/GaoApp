# UI-07d — Dashboard POS trên điện thoại

Ngày 26/10/2026. Đã triển khai và qua kiểm tra tự động; còn nghiệm thu điện thoại thật. Bước tiếp: UI-08a, yêu cầu nghiệp vụ và các panel tiền mặt/thanh toán.

## Kết quả

Đường dẫn hiện hữu: /admin/pos/dashboard. Phone dưới 768px dùng header navy/blue, trạng thái trực tiếp và thời điểm cập nhật rõ, context mã ca/mở lúc/thời gian hoạt động. Bốn KPI thành hai cột; tiền mặt dự kiến nằm cạnh giá trị đơn hoàn tất. Cảnh báo refund/void và cơ cấu tiền được đưa lên trước. Chi tiết quỹ/thu-chi và biểu đồ theo giờ mở bằng native details; biểu đồ cuộn ngang trong khung riêng. Lối tắt vẫn lấy đúng provider/menu của cửa hàng, không gắn thêm mục cố định.

Một Razor chỉ thêm import; hai asset CSS/adapter mới. Adapter chuyển chính node gốc, dùng marker trả lại desktop, không clone số tiền, gọi API hoặc tạo command/storage. Mở biểu đồ chỉ báo resize cho renderer hiện hữu đo lại khung. Màu, vùng chạm từ 44px, tương phản và reduced-motion theo mẫu duyệt.

## Sửa lỗi snapshot khi đổi kích thước

Owner cũ có thể renderSnapshot(lastGoodData) khi resize dù đang tải ca khác hoặc vừa báo lỗi chuyển ca; việc này có thể làm nội dung ca trước xuất hiện lại. Thêm guard ngay trước lần vẽ lại: nếu aria-busy=true hoặc dashboardContent.hidden thì bỏ qua. Guard áp dụng cả phone và desktop; bố cục desktop giữ nguyên. Không đổi URL, dữ liệu summary, tính tiền/biểu đồ, interval 10 giây, abort/sequence hay cơ chế giữ dữ liệu stale cùng ca.

Controller/service/DTO/quyền, store/menu, layout/sidebar/_POSNavTabs và owner in đều giữ nguyên. Không thêm migration/seeder/menu/quyền mới. Không thay các nghiệp vụ mở/nhận/đóng/duyệt ca của UI-07a/b/c.

## Kiểm tra và đánh giá

- Release build 0 lỗi, 0 cảnh báo; 52/52 .NET và 20/20 JS qua. Bộ .NET gồm ca POS, responsive/CSP và 4 hợp đồng dashboard. Test dashboard cũ đòi ba lối tắt cố định đã được thay bằng kiểm tra provider cấu hình, href/id thật và loại current/customer-display; không tạo menu để phục vụ test.
- 5 nhóm browser với Razor/HTTP/SQL thật; 12 lượt bố cục phone gồm 320/360/390/430/600/767px, màn thấp 390×440, nhóm biểu đồ, chưa mở ca và lỗi tải. Không tràn document; cuộn biểu đồ được giữ trong khung. 15 scan không ID trùng; không lỗi JS/CSP.
- 3 đối chiếu toàn bộ computed property/box ở 768/1024/1440px: stylesheet mới không đổi desktop/tablet, các wrapper phone được gỡ. Các node KPI/mã ca/biểu đồ/quỹ/nút refresh gốc được giữ đúng identity.
- SQL fixture có ca hiện tại, ca lịch sử và ca của nhân viên khác; chỉ 12 đơn Completed trong ca hiện tại tạo 120.000 đ, trung bình 10.000 đ. Đơn Draft/Cancelled và đơn ca khác không cộng vào chỉ số/12 cột giờ. Bucket tiền mặt 120.000 + không tiền mặt 80.000 = 200.000 đ giữ nhãn riêng. Quỹ dự kiến 340.000 đ; đầu ca/thu/chi/refund giữ đúng summary gốc.
- Làm mới và callback poll 10 giây mỗi lượt dùng một cặp probe/summary. Poll được điều khiển thời gian trong probe, không đổi production. Mất kết nối cùng ca giữ dữ liệu có nhãn stale; thử lại hồi phục. Lỗi ban đầu, chưa mở ca, thời điểm reset và các action cũ giữ đúng.
- Fault injection trên trang thật kiểm tra request chậm/chồng nhau, abort/sequence, chuyển sang ca mới mô phỏng, summary sai ID và lỗi tải ca mới. Resize ở phone/desktop khi đang chờ hoặc báo lỗi vẫn giữ ca trước ẩn; response cũ đến muộn không thay ca mới. Trạng thái mô phỏng không sửa DB.
- Xung đột chủ ca giữ hợp đồng 409/ownership; thiếu Order.View hoặc Shift.View bị chặn 403. Cửa hàng khác không có ca mở và summary ca ngoài store trả 400 theo service hiện hữu. Không mở rộng quyền để làm test qua.
- Không có POST/PUT/PATCH/DELETE dưới /admin/pos trong probe. Fingerprint ca, đơn, payment, thu/chi, tồn, phiếu, menu/store/quầy/giá/tùy chọn khách/cửa hàng khác không đổi. 319 file bảo vệ khớp snapshot; baseline 560 file: 503 không đổi, 57 đã đổi, 0 mất. UI-07d thay 1 view và 1 asset nghiệp vụ trong baseline; thêm 2 asset ngoài baseline.

Ảnh đã rà: màn tổng quan, quỹ, biểu đồ và các trạng thái stale/chuyển ca lỗi/chưa mở ca/lỗi ban đầu. Bố cục gọn, số chính và phạm vi ca rõ, vùng cuộn biểu đồ tách khỏi trang; desktop giữ hình thức cũ.

## Phạm vi và phần còn lại

Chỉ GUID LocalDB/Web tạm và Chrome headless/profile mới; launcher thường tắt. Không dùng database cấu hình hoặc đóng/thay đổi Chrome đang mở; thiết bị/transport ngoài bị chặn. Các lượt thử sửa mock request chậm/reset và kỳ vọng status theo hợp đồng gốc; production không đổi để khớp mock. Một lượt runner sandbox ban đầu không nối được testhost; lượt hồi quy chính chạy đủ 52. Chẩn đoán cũ giữ trong .artifacts/ui-07d, kết quả cuối đều qua.

Chưa nghiệm thu Android/iOS thật, safe-area/một tay, in/két thật. Các bằng chứng UI-07a/b/c giữ nguyên; helper pos-drawer-atomic ngoài chưa chạy lại do thiếu source ngoài như đã ghi ở UI-07a. Các nghiệm thu trước không được đánh dấu hoàn thành. Có thể tiếp UI-08a với các phần còn mở được giữ trong tiến độ.

[Runtime](mobile-ui-20261025/evidence/ui-07d/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-07d/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-07d/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-07d/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-07d-20261026; snapshot/log: .artifacts/ui-07d.
