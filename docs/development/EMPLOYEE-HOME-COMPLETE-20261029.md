# Trang chủ nhân viên theo ca — 29/10/2026

Đã thay phần nhân viên tại /admin trên máy tính theo phản hồi: bỏ tìm/menu tổng quát, các lối tắt lấy ngẫu nhiên từ đầu danh sách và bảng “Chức năng của bạn”. Trang chủ quản lý và trang điện thoại giữ nguyên hành vi đã duyệt.

## Bố cục nhân viên

- Ca hiện tại ở đầu trang: mã ca, người phụ trách, giờ mở ca Việt Nam, quầy, kho và nút làm mới.
- Bốn tác vụ theo thứ tự: Ca làm việc, Bán hàng, Đơn hàng, Nhận hàng. Nhận hàng chỉ khớp menu /admin/warehouse-receiving; không dùng /admin/stock-documents hoặc phiếu mua khác làm phương án thay thế.
- Chỉ hiển thị tác vụ có trong menu thực đã lọc quyền/trạng thái/ẩn-hiện của cửa hàng. Giữ nguyên ID, URL và query từ menu. Không thêm menu hoặc đổi ghim/thứ tự/quyền. Nếu thiếu menu hoặc quyền, không tự tạo lối vào.
- Dưới tác vụ là doanh số đơn hoàn tất; tổng và các trạng thái đơn; hoàn trả; tiền đầu ca; thu tiền mặt/không tiền mặt; hoàn tiền theo phương thức; thu thêm/rút tiền; tiền két dự kiến; tối đa sáu đơn và sáu giao dịch thu/chi mới nhất.

“Doanh số đã chốt” lấy CompletedSalesTotal từ tóm tắt ca, không gọi báo cáo doanh thu toàn cửa hàng. Tiền két dự kiến lấy ClosingCashExpected; cách tính nghiệp vụ hiện có giữ nguyên. Chỉ số đơn giữ/đơn hoàn tiền được đếm từ danh sách đơn của chính tóm tắt đó; không dùng dữ liệu mẫu trong ứng dụng.

## Phạm vi dữ liệu và quyền

Giữ cách phân biệt hiện có: tài khoản có Report.Sales.View dùng trang quản lý; tài khoản không có quyền này dùng trang nhân viên. Xem số liệu ca cần Pos.Shift.View. Hai API hiện có current/summary tự xác định StoreId, TerminalId và UserId của phiên đăng nhập, giữ kiểm tra chủ ca ở service. Frontend không gửi shiftId/userId/storeId/terminalId và không gọi ownership-info để đọc ca người khác. Nó còn kiểm tra cùng mã, chủ và thời điểm mở ca giữa hai phản hồi, tránh hiển thị kết quả lúc ca đang được bàn giao.

Chưa mở ca, thiếu quyền hoặc quầy thuộc người khác có thông báo riêng; không hiển thị số liệu của ca khác. Lỗi tải xóa số cũ; 401/403 khóa làm mới và yêu cầu tải lại. Khi thu nhỏ về điện thoại hoặc rời trang, hủy đọc và xóa số liệu desktop. Trên điện thoại không gọi thêm API ca.

## Kiểm thử và bàn giao

**395/395 ca .NET, 721/721 ca JavaScript đạt.** Gồm 393 ca hồi quy UI/menu/quyền/chủ ca và hai nghiệm thu Production HTTPS + SQL tạm + Chrome headless. Build 0 lỗi, một cảnh báo xUnit2031 có sẵn.

Ca kiểm thử có doanh số 250 ₫, tiền két dự kiến 1.170 ₫ và đủ sáu trạng thái đơn. Quầy khác có ca 880.000 ₫; cửa hàng khác có doanh thu 900.000 ₫ để phát hiện lẫn dữ liệu. Đã kiểm tra ca đúng chủ, khác chủ, chưa mở ca, thiếu quyền, menu/query thực, 503/thử lại, 403/xóa số liệu và phản hồi chủ ca đã đổi. Snapshot DB trước/sau giữ nguyên ca, thu/chi, đơn, thanh toán, kho, cấu hình, menu và quyền. Đây hoàn toàn là dữ liệu fixture do test sở hữu.

Desktop 1024/1366/1440/1920 px không tràn ngang. Chữ phụ của khu nhân viên đạt tương phản tối thiểu 5.01:1. Ảnh điện thoại so bản trước/sau sau khi che tên/thời gian động có 0 pixel khác ở lượt cuối; ba file Home điện thoại giữ nguyên hash. Không có pageerror hoặc request ra ngoài fixture. Chrome hiện có của người dùng không bị đóng.

Gói WebOnly: E:\GaoApp\GAOAPP-SA-20260819-01\publish\releases\20261029-064334-e9076a04

Manifest SHA-256: 8A771DA50249610BEEE8F4B3EAB2B3694A9E2CCF9737264119561919C5A70238

Không có migration mới; DB tạm đã migrate và không có pending model/migration. Không deploy IIS hoặc cập nhật dữ liệu cửa hàng thật. Build/chạy lại bản đang dùng trong Visual để áp dụng source này. Nghiệm thu host/thiết bị thật và hết hạn voucher tiếp tục để sau như đã thống nhất.

[Ảnh tổng quan nhân viên](../../TestResults/employee-home-20261029/staff-1440.png) · [Ảnh thống kê ca](../../TestResults/employee-home-20261029/staff-statistics.png) · [Bằng chứng kiểm thử](mobile-ui-20261025/evidence/employee-home-20261029/runtime-review.json)
