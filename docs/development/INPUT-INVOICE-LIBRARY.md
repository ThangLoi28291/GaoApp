# Thư viện hóa đơn đầu vào

Đường dẫn: `/admin/input-invoices`. Menu: **Mua hàng → Hóa đơn đầu vào XML**.
Trang danh sách phiếu nhập cũng có nút **Thư viện hóa đơn XML**.

## Sử dụng

1. Chọn năm/tháng rồi **Cập nhật từ thư viện** để đưa các XML đang có vào danh mục. Xóa năm/tháng để đọc tất cả năm. Không bị giới hạn 200 hóa đơn như popup chọn phiếu nhập. Lần cập nhật sau nhận diện bản đã có; không xóa hoặc đặt lại xác nhận.
2. **Thêm XML / PDF** nhận XML tối đa 10 MB và PDF cùng hóa đơn tối đa 50 MB. Nếu bổ sung PDF sau, chọn lại đúng XML đã nhập kèm PDF. Không ghi đè file khác nội dung.
3. Lọc số/ký hiệu/tên/MST, năm, tháng, nhà cung cấp, loại hóa đơn, liên kết và kết quả kiểm tra. Tổng hợp áp dụng theo toàn bộ bộ lọc, không chỉ trang hiện tại.
4. **Xem & kiểm tra** mở PDF nếu có hoặc bản dựng an toàn từ XML; có thể chuyển qua XML và tải file nguồn. Nút lưu/đóng luôn nằm ở chân cửa sổ, nội dung cuộn riêng.
5. Bốn kết quả kiểm tra: Chưa kiểm tra; Đã xác nhận hợp lệ; Chi phí cửa hàng; Không ghi nhận / cần làm rõ. Mục cuối bắt buộc lý do. Lưu người thực hiện, thời gian, trạng thái cũ/mới và ghi chú. Hai người mở cùng bản không được âm thầm ghi đè nhau.

Liên kết phiếu nhập được đọc trực tiếp từ các liên kết đang hoạt động. Bấm số phiếu để mở phiếu nhập. Xác nhận chi phí không yêu cầu liên kết; mọi xác nhận trên trang này không ghi kho/công nợ và không gửi thao tác tới cơ quan thuế hoặc nhà cung cấp.

## Nguồn và phạm vi

- Dùng cùng `InputInvoiceLibrary:RootPath` với popup hiện có: `<root>/<năm>/<MST thư mục nguồn>/<tháng 2 số>/<tên>.xml` và PDF cùng tên. Hỗ trợ cặp `_misa_original` hiện tại. Trang lưu cả MST thư mục nguồn; vì vậy XML có MST chi nhánh khác tên thư mục vẫn đọc được đúng file như popup, nhưng được gắn cảnh báo để kiểm tra trước khi liên kết.
- Cần `InputInvoiceLibrary:Enabled=true`, đường dẫn đã cấu hình qua `Storage:UploadRoot`. Tài khoản chạy Web cần đọc nguồn và quyền ghi khi upload từ trang mới.
- XML chỉ vào danh mục nếu MST người mua khớp duy nhất một chủ thể đang hoạt động trong cửa hàng. Các file không hợp lệ hoặc ngoài phạm vi được thống kê khi cập nhật, không lộ nội dung của cửa hàng khác.
- Định danh theo MST người bán, ký hiệu, số và ngày lập; SHA-256 kiểm tra nội dung. Cùng định danh nhưng khác nội dung được báo xung đột và giữ nguyên bản cũ. Nguồn đã đổi không được dùng để xác nhận hoặc xem như bản đã kiểm tra trước đó. Kiểm tra lại file tại nguồn khi có xung đột.
- Loại gốc/thay thế/điều chỉnh lấy từ `TTChung/TTHDLQuan/TCHDon`. Giá trị không nhận diện hiển thị “Chưa xác định”. Không suy diễn trạng thái hủy hoặc đã được cơ quan thuế chấp nhận từ dữ liệu này.
- Tổng tiền từng loại là tổng chứng từ theo XML, chưa bù trừ hóa đơn gốc bị thay thế. Không dùng con số cộng các loại như chi phí hạch toán thuần.

## Phân quyền và triển khai

- Xem: `inventory.stockdocument.view` theo hằng quyền hiện có trong `PermissionCodes.Inventory.StockDocument.View`.
- Nhập/cập nhật thư viện: quyền sửa phiếu nhập (`StockDocument.Update`), đồng thời phải có quyền xem.
- Xác nhận: quyền duyệt phiếu nhập (`StockDocument.Approve`), đồng thời phải có quyền xem.
- Các POST kiểm tra antiforgery. Các endpoint chi tiết, PDF, XML, xác nhận đều kiểm tra cửa hàng và MST người mua phía server.

**Cần publish Web và Migrator, chạy hai migration mới theo thứ tự** `20261001115308_AddInputInvoiceLibrary` và `20261001121734_PreserveInputInvoiceLibrarySource` (bảng danh mục, lịch sử và cột MST thư mục nguồn). Không dùng bộ Migrator cũ và không chèn thủ công `__EFMigrationsHistory`.

Trong thư mục Migrator đã publish với connection string đúng database:

```powershell
dotnet GaoApp.Migrator.dll --schema-only
```

Sau khi thành công, nếu menu mới chưa xuất hiện, tại thư mục Web đã publish, dùng chế độ cập nhật menu hiện có:

```powershell
dotnet GaoApp.Web.dll --recover-admin-menus-only
```

Sau đó mở trang, chọn năm/tháng và bấm **Cập nhật từ thư viện**. Migration không tự quét các file của server. Không cần thay đổi dữ liệu phiếu nhập đang có.

## Kiểm thử

- `InputInvoiceCatalogSqlServerTests`: database dùng một lần, 205 XML, lọc/phân trang, phân quyền, cách ly cửa hàng, liên kết trực tiếp, xác nhận có lịch sử và kiểm soát phiên bản, upload, chống nguồn thay đổi, phân loại quan hệ và DTD/XXE.
- `InputInvoiceDocumentLibraryTests`: giữ tương thích thư viện cũ, chặn đường dẫn không an toàn.
- `--input-invoice-library` trong browser runner: đăng nhập và SQL thật trên database tạm, xem XML, lọc, lưu/xem lại lịch sử; nút lưu/đóng tại 1366×600, 1024×600 và 390×700.
