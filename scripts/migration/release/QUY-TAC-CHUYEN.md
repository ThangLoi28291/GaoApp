# Quy tắc đã chốt — 29/09/2026

Nguồn thực tế `DataGaoStore`, chỉ đọc. Đích `GaoAppDb` có nghiệp vụ TEST cần dọn trước. Danh sách đủ tại TABLE-PLAN.csv và scripts/migration/initial-import/TABLE-PLAN.json.

## Giữ nguyên 27 bảng

`__EFMigrationsHistory`, `AcbCallbackRouteChanges`, `AcbCallbackRoutes`, `AdminMenuItems`, `Attribute`, `AttributeValue`, `Brands`, `InvoiceProviderSettings`, `LegalEntities`, `LegalEntityActivationEvents`, `Permissions`, `PosReceiptTemplates`, `POSTerminalDevices`, `POSTerminals`, `ProductLabelPrinters`, `ProductLabelTemplates`, `RewardSettings`, `RolePermissions`, `Roles`, `StoreAcbSettings`, `StoreBankAccounts`, `Stores`, `Taxes`, `UserInStores`, `Users`, `Warehouses`, `AutoInvoiceSettings`.

Giữ ID/nội dung, tài khoản/quyền/mật khẩu/cấu hình. Mapping cửa hàng 1/pháp danh 1/kho 1, đổi tên sau; không ép số dòng kho hoặc nhân viên còn 1. Người vận hành tắt phát hành tự động trước chốt; script giữ trạng thái tắt và các tham số khác, không tự bật.

## Dọn 100 bảng CLEAR và xử lý 1 bảng SELECTIVE

CLEAR gồm danh mục sẽ nhập lại, đơn/dòng/thanh toán/ca/trả hàng, chứng từ kho/tồn/giao dịch/giá vốn, hóa đơn, công nợ/đặt cọc TEST, liên kết ảnh, nhật ký nghiệp vụ và hàng đợi TEST, biên nhận import cũ. Bảng ngoài phạm vi nhập lại sẽ rỗng. Xem tên từng bảng/lý do/bước nhận tại CSV, không dùng danh sách khái quát này làm SQL xóa.

Bốn bảng mới CLEAR: AutoInvoiceOperations, AutoInvoiceOperationSources, AutoInvoiceWorkerStates, InvoiceBuyerSelfServiceRequests. Không giữ token/liên kết hàng đợi TEST trỏ nhầm đơn mới.

MediaAssets SELECTIVE: giữ ảnh được cấu hình KEEP tham chiếu, dọn bản ghi ảnh nghiệp vụ TEST. Không xóa file vật lý. TRUNCATE trong transaction, FK được khôi phục và đối chiếu; không DROP bảng dữ liệu.

## Cách chép

1. Sản phẩm/biến thể/category/unit/quy đổi/supplier và mapping lịch sử theo bộ đã diễn tập; giữ danh mục kỹ thuật bất hoạt cho dòng điều chỉnh tài chính.
2. Khách, điểm/voucher theo luật TEST. Không chuyển Debt/công nợ, không lấy HaveDebt tạo dư nợ mới. Giữ cách ghi lịch sử thanh toán đã thống nhất.
3. Đơn/dòng/thanh toán/ca/thu chi thuộc phạm vi bước 03. Trả có liên kết vào SalesReturns; thiếu đơn mua vào LegacyReturnArchives để tra cứu. Không tạo liên kết giả.
4. Phiếu nhập/kho vật lý/lớp giá vốn/phân bổ/tồn theo bước 04. Chứng từ loại trừ có báo cáo.
5. Tồn hóa đơn: từ 01/06/2025, mở đầu SanPhamKhaiThue.SLDauKy; nhập Product có InvoiceSeries với lượng Warranty/ngày CreatedDate; xuất InvoiceHead có InvoiceNumber với IssuedDate và lượng InvoiceDetail. Quy về đơn vị gốc, mã không mapping bỏ qua có báo cáo, giữ tồn âm tính được. Không dùng tổng tháng cũ ghi đè kết quả. Lưu OrderID/InvoiceNumber. Cutoff tự theo chứng từ mới nhất.
6. Tất cả InvoiceHead/Detail: giữ số/trạng thái/thông tin khách/lượng/đơn vị/tiền, JSON nguồn để truy vết. Ba MST lịch sử cùng chủ thể 1. Nhóm điều chỉnh 13 và hóa đơn không liên kết đơn chỉ tra cứu theo luật đã chốt. Không gọi phát hành/email/API khi chuyển.
7. Bổ sung mới: InvoiceHead.LayHD=1 → thủ công, còn lại → tự động. InvoiceHead.Id chính là OrderID cũ, liên kết Orders.Id. Route được nhập ngay bước 03 và đối chiếu/hash ở bước đó, không sửa Orders sau đó. Đơn không có InvoiceHead giữ chưa chọn. Trạng thái phát hành không đổi. Không ép BuyerType/xóa MST; nếu thông tin khách không phù hợp luồng tự động, cần xử lý trong ứng dụng trước phát hành.
8. Ảnh: chỉ gắn file có thật/đường dẫn hợp lệ; thiếu file hoặc mã không mapping có báo cáo. Web dùng UploadRoot hiện hành đúng thư mục ảnh.

Số TEST là tham khảo, không giới hạn dữ liệu thật. PASS kỹ thuật vẫn cần duyệt ngoại lệ/chứng từ mẫu. Không mở bán/bật worker trước nghiệm thu.
