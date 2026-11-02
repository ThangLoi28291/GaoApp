# Trang chủ nhân viên trên điện thoại — 29/10/2026

Đã áp dụng bố cục theo ca của bản máy tính vào /admin trên điện thoại. Chỉ đổi nhánh trang chủ nhân viên; trang chủ quản lý, thanh menu dưới, cấu hình cửa hàng và quyền truy cập giữ nguyên.

## Bố cục và dữ liệu

- Đầu trang có ca hiện tại: mã ca, nhân viên phụ trách, giờ bắt đầu Việt Nam, quầy, kho và nút làm mới 44 px.
- Bốn thao tác xếp 2×2: Ca làm việc, Bán hàng, Đơn hàng, Nhận hàng. Tác vụ Nhận hàng chỉ dùng menu /admin/warehouse-receiving. Mỗi lối vào lấy đúng ID/URL/query từ cây menu đã lọc quyền và thiết lập của cửa hàng; không tự thêm menu hay thay đổi ghim.
- Dưới các tác vụ có doanh số đã chốt, đơn hoàn tất/tổng đơn, tiền két dự kiến, hoàn trả; thu bán hàng/hoàn tiền theo phương thức; tiền đầu ca/thu thêm/rút tiền; các trạng thái đơn; sáu đơn và sáu giao dịch thu/chi gần nhất.
- Nội dung xếp dọc, thẻ co theo màn hình, nút chạm đủ lớn; hỗ trợ reduced-motion và forced-colors. Phần cuối cuộn lên trên menu dưới để đọc được đầy đủ.

Dùng chung một view và một bộ đọc dữ liệu với máy tính. Đổi chiều/kích thước màn hình không tạo thêm vòng đọc. API current/summary hiện có tự xác định đúng cửa hàng, quầy và chủ ca, giữ nguyên toàn bộ guard. Frontend không gửi shiftId/userId/storeId/terminalId; không gọi báo cáo toàn cửa hàng cho nhân viên. Chưa mở ca, thiếu quyền, ca của người khác hoặc lỗi tải có trạng thái riêng; xóa số cũ khi lỗi, hủy khi rời trang và yêu cầu tải lại khi quyền/phiên thay đổi.

Giữ cách phân biệt hiện có: Report.Sales.View dùng tổng quan quản lý; tài khoản không có quyền này dùng trang ca nhân viên. Xem số liệu ca vẫn cần Pos.Shift.View; nhận hàng vẫn cần InventoryStockDocumentCreate. Không tự cấp quyền.

## Kiểm thử

**395/395 ca .NET và 721/721 ca JavaScript đạt.** Gồm 393 ca hồi quy UI/menu/quyền/chủ ca, bài nghiệm thu trang quản lý và bài nghiệm thu điện thoại sau cùng. Build 0 lỗi, một cảnh báo xUnit2031 có sẵn. Các lượt kiểm tra thất bại do thiết lập phép đo đã được giữ riêng trong bằng chứng; không tính chúng thành ca đạt.

Kiểm tra trên WebOnly Production HTTPS, SQL GUID riêng, Chrome headless riêng với trình duyệt ở múi giờ Mỹ nhưng hiển thị giờ Việt Nam. Năm chiều rộng 320/360/390/430/767 px không tràn ngang. Đã đối chiếu số liệu ca, bốn link cấu hình, truy cập thiếu quyền, ca người khác/chưa có ca, lỗi 503/thử lại, 403/xóa dữ liệu, phản hồi chủ ca bị đổi, thao tác mở/đóng menu dưới và đọc dòng giao dịch cuối trang.

Menu dưới giữ nguyên metadata/link/quyền và có **0 pixel khác** ở ảnh riêng không che. Bản nhân viên trên máy tính có **4 pixel khác** trong ngưỡng raster 0,02% (tối đa 276 pixel), sau khi che khối tiêu đề chứa thời gian cập nhật động và loại trạng thái con trỏ/focus. Ảnh trang chủ quản lý trên điện thoại có **0 pixel khác** sau khi che lời chào và khối thời gian động cố định. CSS/view trang chủ quản lý, view/CSS nhân viên desktop và toàn bộ nguồn menu dưới giữ nguyên hash.

Snapshot DB giữ nguyên menu/ghim/ẩn-hiện, cửa hàng, vai trò/quyền, ca và thu/chi, đơn/thanh toán, kho, cấu hình/voucher. Không sử dụng dữ liệu thật; không đóng cửa sổ Chrome hiện có. Không có pageerror hoặc request ra ngoài fixture.

Gói WebOnly: E:\GaoApp\GAOAPP-SA-20260819-01\publish\releases\20261029-072624-650b4fce

Manifest SHA-256: 71F6719C3F30906C835DB07DD7D984172CD62C4921A51796E64E9C4AB91B899C

Không có migration mới/pending model. Không deploy IIS hoặc cập nhật dữ liệu cửa hàng thật. Build/chạy lại bằng Visual để áp dụng source. Nghiệm thu host/thiết bị thật và phần hết hạn voucher vẫn để sau như đã thống nhất.

[Ảnh điện thoại](../../TestResults/employee-phone-home-20261029/employee-phone-390.png) · [Thống kê ca](../../TestResults/employee-phone-home-20261029/employee-phone-statistics.png) · [Giao dịch cuối trang](../../TestResults/employee-phone-home-20261029/employee-phone-recent.png) · [Bằng chứng](mobile-ui-20261025/evidence/employee-phone-home-20261029/runtime-review.json)
