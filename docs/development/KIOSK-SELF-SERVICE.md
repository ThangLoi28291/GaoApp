# Quầy cảm ứng tự phục vụ

Màn hình khách: `/kiosk`. Quản trị thiết bị: `/admin/kiosks` (vai trò ADMIN; có trong menu quản trị và trang quản lý máy POS).

## Cập nhật và kích hoạt

1. Publish bản mới của **GaoApp.Migrator** và **GaoApp.Web** cùng phiên bản. Sao lưu database theo quy trình triển khai hiện tại. Giữ cấu hình kết nối, Data Protection và thư mục upload của server.
2. Tại thư mục publish Migrator, chạy `dotnet GaoApp.Migrator.dll --schema-only` với cấu hình database cửa hàng đúng môi trường. Các cờ demo/default-admin/bootstrap phải tắt theo quy tắc hiện có của Migrator. Migration mới: `20261001010000_AddKioskStations`; không cần seed quyền mới vì quản lý kiosk dùng vai trò ADMIN có sẵn.
3. Triển khai Web sau khi Migrator báo thành công. Web không tự chạy migration. Cấu hình HTTPS cho địa chỉ cửa hàng.
4. Admin mở **Quầy tự phục vụ**, tạo/chọn một máy POS riêng chưa có ca mở, chọn kho xuất bán, bấm **Tạo và cấp khóa kích hoạt**.
5. Trên máy cảm ứng tại cửa hàng, mở `https://<địa-chỉ-cửa-hàng>/kiosk`, nhập khóa vừa cấp. Khóa dùng một lần, hết hạn sau 30 phút. Cookie quyền thiết bị chỉ có trên trình duyệt đã kích hoạt; biết đường dẫn không cấp quyền tạo đơn hoặc tra cứu dữ liệu.
6. Bật chế độ toàn màn hình của trình duyệt. Máy quét dùng chế độ bàn phím USB/HID, gửi **Enter** sau barcode. Nên dành riêng trình duyệt này cho kiosk, không đăng nhập tài khoản quản trị trên máy khách.
7. Kiểm tra cấu hình ACB tự động đang hoạt động ở `/admin/acb/settings` và nội dung quảng cáo trong phần khuyến mãi hiển thị hiện có.

## Trải nghiệm khách

- Khi chờ: chạy ảnh/video/nội dung đang được bật trong DisplayPromotions, theo thời gian và thứ tự hiện có. Poster toàn màn hình giữ nguyên nội dung ảnh, có nút chạm để bắt đầu.
- Menu gồm ba nút lớn: thông tin sản phẩm; thông tin khách hàng; mua hàng không tiền mặt. Có bàn phím cảm ứng và hỗ trợ máy quét.
- Sản phẩm: tìm không dấu, tên hoặc barcode; hiện ảnh, mô tả và giá bán lẻ của từng đơn vị đang hoạt động. Mua hàng dùng chung cách tính giá theo số lượng/quy cách và khuyến mãi của POS.
- Khách hàng: nhập đúng số điện thoại để xem điểm, lịch sử tích/đổi điểm, voucher và chi tiết đơn mua. Không có thao tác đổi điểm, sử dụng voucher, chỉnh hồ sơ hay công nợ. OTP chưa triển khai theo phạm vi đã thống nhất.
- Mua hàng: giỏ có ảnh sản phẩm (ưu tiên ảnh biến thể, sau đó ảnh sản phẩm), chạm ảnh để phóng to; quét mã, thêm/bớt hoặc chạm số lượng để nhập. Nút **Xóa sản phẩm** ở từng dòng bỏ toàn bộ số lượng của dòng và tính lại tổng tiền/khuyến mãi; quà tặng vẫn theo quy tắc khuyến mãi, không xóa trực tiếp. Xác nhận tổng tiền rồi tạo QR. Không có nút khách tự xác nhận đã trả tiền. Giỏ khóa khi có QR đang chờ hoặc cần kiểm tra.
- Hóa đơn điện tử mặc định **Khách không lấy hóa đơn**. Sau khi đơn hoàn tất, kiosk lưu `InvoiceIssuanceRoute.Automatic` qua service chọn phương thức hiện có (có thời điểm và người thực hiện là danh tính hệ thống của quầy). Giỏ chưa thanh toán vẫn giữ `Unselected`; không cần thêm popup hỏi khách. Nếu nhân viên đã chọn phương thức cho đơn, kiosk giữ nguyên lựa chọn đó. Khách cần lấy hóa đơn được hướng dẫn gọi nhân viên.
- Thông tin và giỏ chưa thanh toán tự đóng sau **90 giây không thao tác**. Phía server dọn giỏ bỏ dở sau **2 phút** khi không nhận hoạt động; giỏ có QR được giữ để kiểm tra ngân hàng.
- Thanh toán thành công: hiện mã đơn, số tiền; tự trở về quảng cáo sau **15 giây**, hoặc bấm Hoàn tất. Thông tin khách được xóa khỏi phiên. Đếm ngược này không áp dụng cho QR đang chờ.

## Thanh toán và đối soát

Kiosk dùng **ACB tự động hiện có**. Quy tắc ACB của app hiện yêu cầu tất cả sản phẩm, kể cả quà tặng, có `HasInputInvoice=true`. Mặt hàng chưa đủ điều kiện vẫn tra cứu được nhưng được hướng sang thu ngân khi mua; kiosk không chuyển sang QR xác nhận thủ công. Cần cấu hình tài khoản ACB và khóa tích hợp hợp lệ trước khi mở bán.

Kiosk dùng chung xử lý callback, bằng chứng ngân hàng và khóa chống ghi nhận trùng của POS. Màn QR đếm ngược **30 giây** cho lần kiểm tra đầu, sau đó **8 giây** mỗi lần như POS; có nút **Kiểm tra ngay**. Trạng thái callback đã xác minh được đọc riêng mỗi khoảng **2 giây**, không phải chờ hết đếm ngược. Nguồn xác nhận hợp lệ trước được lưu làm nguồn xác nhận; lần kiểm tra đến sau không thêm khoản tiền hay chốt đơn lần nữa. Tải lại trang lấy mốc kiểm tra từ phiên QR đã lưu.

Khách bấm **Hủy thanh toán** rồi **Kiểm tra & hủy**: server tra cứu và yêu cầu ACB hủy QR trước, chỉ sau khi ngân hàng xác nhận hủy mới hủy giỏ. Nếu đã nhận tiền, số tiền không khớp hoặc kết nối không xác định được, giữ giao dịch và gửi yêu cầu hỗ trợ. Tiền đến muộn sau khi hủy vẫn được lưu để đối soát; không tự phục hồi/chốt đơn đã hủy. Lịch sử QR không bị xóa.

Doanh thu, xuất kho và đối soát dùng Order/POSShift của quầy riêng. Ca được mở khi bắt đầu giỏ đầu tiên; số tiền đầu ca bằng 0. Một danh tính hệ thống không có quyền đăng nhập được tạo để đáp ứng khóa ngoại/lịch sử nghiệp vụ; khách không chọn nhân viên. Đơn chưa thanh toán không được chốt/xuất kho. Sau khi ngân hàng xác nhận, dùng chung luồng hoàn tất POS và chống ghi nhận trùng của ACB.

Màn quản trị tự cập nhật mỗi 5 giây: kết nối, yêu cầu hỗ trợ, đơn hiện tại, tổng tiền và trạng thái QR. Admin có thể tạm dừng, cấp lại khóa, thu hồi, xác nhận đã hỗ trợ, kiểm tra thanh toán và chốt phiên quầy. Xử lý xong giao dịch hiện tại trước khi chốt phiên. Có liên kết xem đơn và lịch sử ACB; báo cáo POS hiện có lọc theo quầy để đối soát.

## Phục hồi

- Tải lại hoặc mở lại kiosk: đọc giỏ/QR đã lưu trên server, tiếp tục kiểm tra đúng giao dịch; không tạo QR mới vì mất kết nối.
- Lần tạo QR lỗi/chưa có ảnh: hiện **Đang xác minh lần tạo QR**, không hướng khách quét mã chưa tồn tại. **Kiểm tra & tạo lại QR** giữ nguyên Order và giỏ; chỉ thay lần tạo QR sau khi luồng hủy ACB hiện có xác nhận giải phóng được lần trước. Không xem mã lỗi `30020500` hoặc timeout là bằng chứng chưa nhận tiền. Nếu kiểm tra phát hiện đã nhận tiền thì hoàn tất đúng đơn một lần; số tiền không khớp hoặc ngân hàng không kiểm tra được thì giữ lại để đối soát. QR Pending đã hiển thị không được thay qua thao tác này.
- Khi server báo hoàn tất, màn hình xóa lỗi tạo/kiểm tra QR cũ. Thay đổi trạng thái/ảnh QR trong cùng phiên cũng cập nhật giao diện. Phiên mua tiếp theo dùng Order, SessionKey và CheckoutKey riêng, không thừa hưởng QR, tiền hoặc lỗi từ phiên trước.
- Đóng trình duyệt hoặc mất điện khi chờ chuyển khoản: bằng chứng ngân hàng vẫn thuộc phiên QR đã lưu. Khi thiết bị hoạt động trở lại, kiosk kiểm tra tiếp. Admin có thể bấm **Kiểm tra thanh toán** tại quầy để hoàn tất hoặc xem lý do cần đối soát khi máy chưa mở lại.
- Chuyển thiếu/thừa/lặp, dữ liệu đơn thay đổi hoặc lỗi hoàn tất: giữ giao dịch ở trạng thái cần kiểm tra, hiển thị lời nhắc không thanh toán thêm, gửi yêu cầu hỗ trợ trong bảng quản trị.
- **Hủy phiên / QR chưa trả** kiểm tra ngân hàng trước khi hủy. Nếu đã nhận tiền hoặc cần đối soát thì chặn hủy. Không xóa lịch sử QR. Tiền đến sau khi QR đã hủy được giữ trong luồng đối soát ACB hiện có.
- Thu hồi quyền máy có hiệu lực ở các API, không xóa giao dịch đang chờ. Cấp khóa mới rồi kích hoạt sẽ thay quyền thiết bị cũ.

## Kiểm chứng

`GaoApp.Tests/Security/KioskSqlServerTests.cs`: database SQL dùng một lần, migration thật, API/middleware thật, thiết bị/tenant/quyền/CSRF, khách hàng chỉ đọc, giá quy cách, chống thao tác trùng và giỏ chưa xuất kho.

`GaoApp.Tests/Payments/KioskPaymentTests.cs`: luồng ACB với ngân hàng giả lập; nhận tiền/hoàn tất một lần, số tiền không khớp, khóa giỏ, tạo lại QR sau lỗi lấy token, tự kết thúc sau 15 giây. Không chuyển tiền thật.

`GaoApp.Tests.Browser/kiosk.browser.cjs`: trình duyệt Chrome trên SQL tạm; tìm sản phẩm, giá đơn vị, lịch sử khách, scanner liên tiếp, bàn phím số lượng, phục hồi trang, yêu cầu hỗ trợ, bố cục 1024×768 / 1280×800 / dọc. QR trong kiểm thử trình duyệt là mô phỏng giao diện; cần kiểm tra tích hợp ACB của cửa hàng khi triển khai.
