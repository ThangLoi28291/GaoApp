# Bill chạy trên màn hình khách — 2026-10-09

## Hành vi

Tại `/admin/pos/customer-display`, sau khi chốt đơn, màn hình mô phỏng bill giấy chạy ra từ khe máy in theo ảnh tham khảo của người dùng. Thông tin giao dịch ở bên trái; bill ở bên phải, hoặc bên dưới trên điện thoại. Giữ tên cửa hàng, nhân viên và thông tin Wi-Fi bên ngoài lớp bill.

Bill dùng `GET /admin/pos/orders/{orderId}/receipt`, cùng DTO mà trang in dùng. Hiển thị mã bill, thời gian Việt Nam, khách, quầy, thu ngân, từng sản phẩm/biến thể, đơn vị, số lượng, đơn giá, thành tiền, giảm giá tổng, tổng thanh toán, cọc, các khoản thanh toán, công nợ và tiền thừa. Không dựng bill từ giỏ hiện tại, không cộng lại giảm giá đơn vào tổng giảm giá.

Sự kiện máy chủ `order_finalized` đã có trong luồng chốt tiền mặt, công nợ, tự chốt và xác nhận QR. Chỉ xử lý khi store và terminal trùng với màn hình đang mở. Không thay đổi lệnh thanh toán, lệnh in hoặc menu.

## Chuyển trạng thái

- Thanh toán một phần và nhận cọc tiếp tục dùng thông báo cũ.
- Chỉ nhận receipt có cùng orderId và trạng thái `Completed`; dữ liệu không tải được không được thay bằng bill giả.
- Sự kiện chốt lặp không chạy bill lại. Giới hạn bộ nhớ chống lặp ở 100 đơn trong phiên màn hình.
- Bill hiện ít nhất 20 giây; bill dài tự cuộn và có thể giữ đến 60 giây. Có thể cuộn bằng chuột/cảm ứng; tạm dừng tự cuộn khi tương tác hoặc rê chuột vào bill.
- Lựa chọn giảm chuyển động của trình duyệt tắt hiệu ứng và tự cuộn.
- Reset từ POS sau khoảng 4,7 giây không cắt ngang bill. Giỏ mới rỗng không cắt ngang bill; khi giỏ khác có hàng, màn hình ưu tiên khách tiếp theo.
- Hủy yêu cầu receipt đang chờ khi đổi giao dịch. Phản hồi cũ không được ghi đè bill mới.
- Bỏ phản hồi giỏ đang chờ từ trước sự kiện chốt đơn, rồi tải lại snapshot mới. Nhận tiền cọc mới vẫn thay thế bill bán hàng bằng thông báo nhận cọc.

## Kiểm tra

```powershell
node --test GaoApp.Tests/Ui/customer-display-receipt.test.cjs
dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj --artifacts-path Logs/customer-deposit-upgrade-20261009/build --no-restore -m:1 -nr:false -p:UseAppHost=false -p:UseSharedCompilation=false -v:quiet
$env:GAOAPP_TEST_SQL_SERVER = '.\SQLEXPRESS'
dotnet Logs/customer-deposit-upgrade-20261009/build/bin/PosOffline.Browser/debug/PosOffline.Browser.dll --customer-display-receipt
dotnet Logs/customer-deposit-upgrade-20261009/build/bin/PosOffline.Browser/debug/PosOffline.Browser.dll --customer-display
```

Browser probe sử dụng SQL dùng một lần, tồn kho nhập thật của fixture, SignalR và hai hóa đơn có dữ liệu khác nhau. So sánh DTO hiển thị với DTO nhúng trong trang in; kiểm tra thanh toán một phần, giảm giá, tiền thừa, reset, chuyển khách, 20 dòng hàng và các kích thước màn hình. Không gọi máy in thật.

Ảnh kiểm tra: `TestResults/customer-display/receipt/`. Log build và browser: `Logs/customer-display-receipt-*.log`.

Kết quả: build thành công; 3 kiểm thử trạng thái JavaScript qua; browser probe bill chạy qua, bao gồm thao tác thu tiền mặt/tự chốt từ giao diện POS và chuyển sang nhận cọc; probe màn hình khách hiện có (giỏ, QR, quảng cáo, video, các kích thước màn hình) qua. Đã xem lại ảnh trên máy tính và điện thoại. Chưa triển khai lên máy chạy thực tế.
