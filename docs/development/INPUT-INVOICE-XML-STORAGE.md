# Thư mục XML/PDF hóa đơn đầu vào

## Giá XML và VAT khi đối chiếu phiếu nhập

`DGia` và `ThTien` của từng `HHDVu` là giá/thành tiền trước VAT; `TSuat` là thuế suất của chính dòng đó. Nếu XML có tiền thuế riêng (`TThue` hoặc thông tin phụ `VATAmount`), ưu tiên số tiền này. Khi không có tiền thuế riêng, tính VAT trên `ThTien` (đã phản ánh chiết khấu của dòng), làm tròn tiền đến 2 chữ số; không lấy một thuế suất chung áp cho mọi sản phẩm.

Tab Giá và khuyến mãi hiển thị cả giá trước thuế, thuế suất, tiền thuế và giá sau thuế. Đối chiếu giá quy đổi theo đơn vị gốc dùng giá **sau VAT**. Phiếu không tách VAT riêng chỉ so tổng sau thuế; không báo lệch giả giữa tổng giá đã gồm VAT và cột tiền trước thuế của XML. Phiếu có tách VAT vẫn kiểm tra riêng thuế suất/tiền thuế.

Khi thêm dòng XML vào bill: phiếu không bật Có VAT nhận thành tiền sau thuế; phiếu bật Có VAT để tính thuế riêng nhận thành tiền trước thuế, tránh cộng hai lần. XML đã lưu với tiền thuế dòng bằng 0 được bổ sung phép tính từ thuế suất khi đọc, không sửa chứng từ đã duyệt. Thuế suất chưa xác định cần kiểm tra trước khi nhập giá sau thuế. Bill đã nhập hoặc đã khôi phục không tự sửa giá.

Thư viện của nút **Chọn hóa đơn** dùng thư mục `XML` bên trong `Storage:UploadRoot`, cùng gốc lưu ảnh. Ví dụ cấu hình ảnh hiện tại là `C:\GaoAppData\Uploads` thì thư viện là:

```text
C:\GaoAppData\Uploads\
  products\
  XML\
    2026\
      <MST-nha-cung-cap>\
        09\
          <ten-hoa-don>.xml
          <ten-hoa-don>.pdf
          <ten-hoa-don>_misa_original.xml   (nếu có)
```

XML và PDF cùng hóa đơn dùng chung tên file. Giữ nguyên cây năm/MST/tháng và tên file khi chuyển thư viện để các mã tài liệu đã liên kết vẫn tìm được. Thư viện đọc các file có sẵn trong cây này; Web không tự tải hóa đơn đầu vào từ nhà cung cấp. Công cụ đang tải XML/PDF cần được đặt thư mục xuất về `C:\GaoAppData\Uploads\XML`.

## Cấu hình Web trên server

```json
"Storage": {
  "UploadRoot": "C:\\GaoAppData\\Uploads",
  "CreateIfMissing": true
},
"InputInvoiceLibrary": {
  "Enabled": true,
  "RootPath": "XML",
  "MaxMonthPartitions": 12,
  "MaxCandidates": 200
}
```

`RootPath: "XML"` (hoặc để trống) lấy gốc thư mục từ cấu hình ảnh. Web tạo thư mục khi thư viện được bật và `Storage:CreateIfMissing` là `true`. Tài khoản IIS cần quyền đọc thư viện và quyền tạo thư mục nếu chưa có.

Bản Web hiện bật thư viện trong cấu hình chạy local và mẫu publish `eng/deployment/web.appsettings.json`. Publish chỉ xuất mẫu này thành `appsettings.json`, không xuất `appsettings.Production.json`. Khi cập nhật server đang giữ cấu hình cũ, kiểm tra `appsettings.json` trên host và biến môi trường `InputInvoiceLibrary__Enabled`: nếu đang là `false` thì đổi thành `true` rồi khởi động lại Web. Thông báo “Thư viện hóa đơn chưa được bật cho môi trường này” là do công tắc này, không phải do chọn sai nhà cung cấp. Không cần migration database cho thay đổi công tắc này.

Trong phiếu nhập, chọn nhà cung cấp ở **Thanh toán & thuế** và đợi báo **Đã lưu nhà cung cấp**, sau đó mở **Đối chiếu XML → Chọn hóa đơn**. Danh sách đọc theo MST của nhà cung cấp đã lưu; thiếu MST cần cập nhật hồ sơ nhà cung cấp. Khi đổi nhà cung cấp, danh sách/bản xem trước cũ bị xóa và các yêu cầu tải cũ bị hủy. API vẫn kiểm tra nhà cung cấp và chủ thể người mua trước khi cho liên kết.

Nếu đã cấu hình biến môi trường `InputInvoiceLibrary__RootPath`, đổi giá trị đó thành `XML`; biến môi trường có độ ưu tiên cao hơn appsettings. Có thể tiếp tục dùng đường dẫn tuyệt đối để giữ một thư viện ngoài riêng biệt. Các profile chạy local của Web đã chọn `XML` và bật thư viện.

Sao chép XML/PDF từ thư viện cũ sang cây mới trước khi khởi động lại Web. Nếu đích đã có file cùng tên, đối chiếu nội dung trước, không ghi đè tự động. Giữ lại bản cũ cho đến khi kiểm tra mở PDF và chọn XML thành công. Server khác cần được chép dữ liệu riêng; publish Web không mang theo dữ liệu thư viện.

XML/PDF vẫn được đọc qua API có kiểm tra quyền truy cập hóa đơn, không công khai qua `/uploads/XML/...`. Hóa đơn bán ra tải từ Viettel tiếp tục dùng nơi lưu hiện có.
