# Trang chủ máy tính — Bàn làm việc, 29/10/2026

Đã thay khung “GaoApp Dashboard / Mở menu” tại /admin bằng bàn làm việc dành cho nhân viên và quản lý. Phạm vi ngoại lệ desktop được người dùng yêu cầu rõ trong đợt này; các trang desktop khác và bản điện thoại được giữ nguyên. Không thay quyền, cấu hình cửa hàng, menu hoặc các cửa sổ Chrome đang mở.

## Bố cục và hành vi

- Thanh đầu có lời chào, ngày Việt Nam, tìm chức năng và nút mở toàn bộ menu. Phím / đưa con trỏ vào tìm kiếm; hỗ trợ tên có/không dấu và lọc theo nhóm.
- “Công việc nhanh” ưu tiên tối đa 4 mục ghim hiện có, rồi lấy thêm mục từ menu đã cấu hình để có tối đa 6 lối tắt desktop. Đây là cách trình bày, không tăng giới hạn mục ghim, không tạo menu hay lưu lại cấu hình.
- “Chức năng của bạn” lấy cùng snapshot AdminNavigationProvider với sidebar, giữ ID, đường dẫn/query, thứ tự, trạng thái, thiết lập ẩn/hiện và quyền. Có trạng thái không tìm thấy/chưa có menu.
- Người có quyền Report.Sales.View thấy doanh thu thuần, số đơn bán, trung bình/đơn và trả hàng; biểu đồ diễn biến và sản phẩm bán nổi bật. Hôm nay / 7 ngày gần nhất / tháng này dùng API báo cáo hiện có. Không có quyền thì không render khối, không gọi API và dành chỗ cho thao tác công việc.
- Lỗi tải không giữ số cũ; có làm mới. 401/403 xóa số liệu, khóa tải và yêu cầu nạp lại trang. Chuỗi phản hồi cũ không được ghi đè yêu cầu mới. Dữ liệu trả về được kiểm tra kiểu trước khi render bằng textContent/SVG DOM.
- Navy/blue, thẻ tác vụ gọn, chữ phụ tăng tương phản, chuyển động nhẹ và tôn trọng reduced-motion. Không thêm thư viện biểu đồ hoặc số liệu mẫu trong frontend.

Hướng bố trí tham khảo trang chủ quản trị bán hàng của [Shopify](https://help.shopify.com/en/manual/shopify-admin/shopify-home), kết hợp ngôn ngữ giao diện commerce đã duyệt trong GaoApp. Chỉ dùng dữ liệu và chức năng sẵn có, không thêm bảng công việc giả.

## Kiểm thử

**393/393 ca .NET và 721/721 Node đạt.** Trong đó 392 ca hồi quy UI/menu/quyền và 1 ca nghiệm thu HTTPS kết hợp trình duyệt/SQL. Build 0 lỗi; 1 cảnh báo xUnit2031 có sẵn. Không có migration mới/pending model.

Nghiệm thu thực hiện trên WebOnly Production HTTPS và SQL GUID riêng, chuyển bản trước → bản mới với cùng dữ liệu. Một cửa hàng có doanh thu kiểm thử 100 ₫, cửa hàng kia 900.000 ₫ để phát hiện đọc nhầm tenant. Nhân viên không có quyền báo cáo không gọi API, gọi trực tiếp vẫn bị 403; người thiếu quyền trang chủ không vào được /admin. Dữ liệu thật của cửa hàng không được sử dụng.

Kiểm tra desktop 1024, 1366, 1440, 1920 px không tràn ngang; tìm/nhóm/phím tắt, query của menu, trạng thái 503/thử lại/403, kỳ báo cáo với trình duyệt ở múi giờ Mỹ nhưng hiển thị giờ Việt Nam. Chữ phụ được đo tương phản tối thiểu 4.79:1. Không có pageerror hoặc gọi ra ngoài origin của fixture.

Ảnh điện thoại 390 px so trước/sau đã che tên chào và thời gian động; ngân sách sai khác raster tối đa 0,01%, lượt cuối **0 pixel khác**. Ba file _MobileOverview.cshtml, gaoapp.mobile-home.css/js giữ nguyên hash. Ảnh desktop bàn giao là viewport đã tải xong số liệu.

Snapshot trước/sau giữ nguyên cửa hàng, menu, mục ghim, ẩn/hiện, vai trò/quyền, đơn/chi tiết/thanh toán, kho và cấu hình/voucher. Không tự seed menu hoặc quyền trong ứng dụng; dữ liệu fixture do test sở hữu. Controller chỉ thêm no-store cho Home; giữ nguyên toàn bộ Authorize và các alias /admin, /admin/home, /admin/dashboard.

Gói mới: E:\GaoApp\GAOAPP-SA-20260819-01\publish\releases\20261029-061102-91d81494

Manifest SHA-256: B10BE80AD5B442110D9D0432CF2F26B67118D4E26973392589DB25484C8298FF

Gói WebOnly gồm 2075 source C# production không filter/shadow và 284 view/443 asset riêng hiện tại. Không chạy Migrator/seed hoặc deploy IIS. Build lại bản đang dùng trong Visual để áp dụng; không cần cập nhật DB. Nghiệm thu điện thoại/thiết bị/host thật và phần hết hạn voucher vẫn để sau như đã thống nhất.

[Ảnh quản lý](../../TestResults/desktop-home-20261029/manager-1440.png), [ảnh nhân viên](../../TestResults/desktop-home-20261029/staff-1440.png), [ảnh trước](../../TestResults/desktop-home-20261029/baseline-desktop.png), [bằng chứng tổng](mobile-ui-20261025/evidence/desktop-home-20261029/runtime-review.json), [source/build](mobile-ui-20261025/evidence/desktop-home-20261029/source-review.json).
