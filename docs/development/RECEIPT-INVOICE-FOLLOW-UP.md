# Theo dõi hóa đơn sau khi duyệt phiếu nhập

- Lần duyệt đầu khi chưa gắn XML: chọn **Chờ nhà cung cấp gửi hóa đơn** hoặc **Hoàn tất, không chờ hóa đơn**.
- Giá nhập, tồn kho, FIFO và công nợ được ghi nhận ngay trong giao dịch duyệt. Chờ XML không hoãn nhập kho.
- `StockDocument.WaitForInputInvoice` là lựa chọn riêng: null = phiếu cũ chưa phân loại, true = chờ, false = không chờ. Migration không phân loại lại dữ liệu cũ.
- Khi có liên kết XML hoạt động, trạng thái hiển thị theo kết quả đối chiếu: Matched/AcceptedMismatch → Đủ hóa đơn; các trường hợp khác → Cần kiểm tra hóa đơn. Không tự sửa giá/VAT đã ghi sổ.
- Gắn, gỡ hoặc thay XML tiếp tục dùng luồng hiện có, không duyệt lại, không ghi lại tồn kho/công nợ. Phiếu duyệt cùng XML lưu lựa chọn chờ để có thể theo dõi nếu sau đó gỡ XML.
- **Kết thúc chờ hóa đơn** yêu cầu quyền duyệt tương ứng nguồn phiếu, ghi chú, phiên bản phiếu và chưa có XML. Khóa phiếu trong giao dịch; chỉ đổi lựa chọn và ghi audit `InputInvoiceWaitingEnded`.
- Danh sách mặc định **Cần xử lý** gồm nháp/cần sửa/chờ duyệt và phiếu đã duyệt đang chờ/cần kiểm tra hóa đơn. Bộ lọc hóa đơn áp dụng cho phiếu đã duyệt.
- Checkbox **Đưa phiếu vào danh sách chờ in tem** mặc định bỏ chọn. Sau khi duyệt thành công, gọi thao tác thêm vào hàng đợi có tính idempotent; không gửi lệnh in. Nếu thêm vào hàng đợi lỗi, thông báo phiếu đã duyệt và cho thử lại riêng thao tác hàng đợi hoặc để sau. Có nút đưa vào danh sách in tem trên phiếu để bổ sung sau. XML không kích hoạt in tem.
- Ảnh nằm cạnh tên sản phẩm; rê chuột hoặc focus/chạm để phóng to, Esc/di chuyển ra ngoài để đóng.

## Đưa lên server

Publish Web và Migrator từ cùng phiên bản. Giữ cấu hình kết nối database của server; sao lưu database trước cập nhật. Dừng Web, chạy Migrator với `--schema-only` để áp dụng `20260930100000_AddReceiptInvoiceFollowUp`, sau khi thành công bật Web mới. Không bật Web mới khi migration chưa thành công vì truy vấn sẽ cần cột mới.

Kiểm thử trên database thử riêng:

```powershell
dotnet test GaoApp.Tests/GaoApp.Tests.csproj --filter "FullyQualifiedName~ReceiptInvoiceFollowUpSqlServerTests|FullyQualifiedName~ReceiptListSqlServerTests|FullyQualifiedName~Confirmed_late_link_unlink_and_atomic_relink_preserve_non_zero_posted_effects|FullyQualifiedName~T3_confirm_posts_purchase_inventory_cost_payable_and_retry_exactly_once"
dotnet run --project GaoApp.Tests.Browser/PosOffline.Browser.csproj -- --receipt-invoice-follow-up
```
