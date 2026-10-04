# Mẫu hóa đơn mặc định chung của cửa hàng

## Sử dụng

1. Đăng nhập bằng tài khoản quản trị viên của cửa hàng, mở `/admin/receipt-templates`.
2. Chọn mẫu có sẵn hoặc sửa nội dung và lưu thành mẫu của cửa hàng.
3. Bấm **Đặt mẫu đang chọn làm mặc định**. Tên mẫu đang áp dụng hiện ở đầu trang và trong thư viện.
4. Nhân viên tính tiền/in lại sẽ tự nhận mẫu này; trang in không có hộp chọn mẫu hoặc liên kết cấu hình.

Admin sửa nội dung mẫu đang mặc định thì lần in sau dùng nội dung mới. Các quầy đang mở nhận bản cập nhật qua luồng kiểm tra kết nối POS. Khi mất mạng, quầy dùng bản đã đồng bộ gần nhất; kết nối lại sẽ nhận bản mới.

Máy in, cách kết nối QZ/hộp thoại trình duyệt và số liên vẫn là thiết lập riêng tại quầy. Admin dùng **Lưu máy in tại quầy** trên trình duyệt đó. **In thử** dùng mẫu đang xem, không tự đổi mặc định toàn cửa hàng.

## Phân quyền và tính nhất quán

- Trang cấu hình và mọi thao tác ghi yêu cầu vai trò hệ thống `ADMIN` đang hoạt động trong đúng cửa hàng, ngoài các quyền chức năng hiện có. Gán riêng quyền quản lý mẫu cho nhân viên không cho phép vượt kiểm tra admin.
- Nhân viên có quyền in vẫn được đọc dữ liệu in; máy chủ gửi mẫu mặc định của cửa hàng.
- Bản mẫu từng lưu trong bộ nhớ trình duyệt và tham số `size` của URL in không thay thế mặc định chung. Cấu hình máy in cũ được giữ.
- Không được xóa mẫu đang áp dụng. Chọn mẫu mặc định khác trước khi xóa.
- Thay đổi mặc định kiểm tra phiên bản cửa hàng và mẫu; xóa mẫu cũng cập nhật phiên bản cửa hàng để phát hiện xung đột với thao tác đặt mặc định.
- Chưa có cấu hình chung: dùng mẫu **Hiện đại · 80 mm**. Admin cần chọn mẫu chung một lần sau cập nhật; không tự suy đoán mẫu từ các trình duyệt khác nhau.

## Cập nhật server

Publish lại **Web + Migrator**, giữ cấu hình môi trường và kết nối database của server. Sao lưu theo quy trình hiện có rồi chạy trong thư mục Migrator:

```powershell
dotnet GaoApp.Migrator.dll --schema-only
```

Xác nhận migration `20260928200000_AddStoreReceiptDefault` hoàn tất trước khi khởi động Web mới. Migration thêm cột nullable `Stores.ReceiptTemplateKey`; không tự sửa mẫu cũ hoặc quyền tài khoản.

Kiểm thử tự động dùng SQL riêng và máy in mô phỏng; chưa cập nhật database/server thật, chưa thử máy in vật lý.

## Kiểm chứng 28/09/2026

- Build Web và Migrator thành công.
- 41 kiểm thử JavaScript in hóa đơn/offline đạt; 4 kiểm thử SQL về mẫu, quyền, cửa hàng và migration đạt.
- Chrome: admin đặt mẫu chung 45 mm; quầy đang mở nhận cập nhật; in online/offline cùng mẫu và máy in đã chọn; tài khoản nhân viên trên trình duyệt mới bị chặn trang cấu hình, không thấy bộ chọn mẫu, bỏ qua mẫu A4 cũ trên trình duyệt và tham số đổi khổ giấy, gửi đúng một lệnh in.
- Bộ kiểm thử bán hàng xác nhận lại SQL: đúng 2 đơn, 2 khoản thanh toán, tổng 100, tồn kho 95 trong dữ liệu thử nghiệm.
- Log: `Logs/receipt-default-browser.log`, `Logs/receipt-default-sql-tests.log`, `Logs/receipt-default-js-tests.log`.
