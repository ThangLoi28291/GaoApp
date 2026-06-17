# Module Invoice / Input Invoice

## File chính

- `GaoApp.Domain/Entities/InputInvoiceHead.cs`
- `GaoApp.Domain/Entities/InputInvoiceDetail.cs`
- `GaoApp.Domain/Entities/InvoiceHead.cs`
- `GaoApp.Domain/Entities/InvoiceDetail.cs`
- `GaoApp.Domain/Entities/StockDocumentInputInvoiceMap.cs`
- `GaoApp.Domain/Entities/StockDocumentLineInputInvoiceMap.cs`
- `GaoApp.Application/Services/Invoices/InvoiceService.cs`
- `GaoApp.Application/Services/Inventory/InputInvoiceXmlService.cs`
- `GaoApp.Infrastructure/Repositories/Invoices/InvoiceRepository.cs`
- `GaoApp.Infrastructure/Repositories/Inventory/InputInvoiceRepository.cs`
- `GaoApp.Web/Areas/Admin/Controllers/InvoiceController.cs`

## Mục tiêu module

- Quản lý hóa đơn đầu vào/đầu ra trong GaoApp.
- Map hóa đơn đầu vào với phiếu nhập kho.
- Lưu XML hóa đơn đầu vào nếu có.
- Hỗ trợ đối soát nhập hàng.

## Khi sửa Invoice cần test

- Import XML.
- Xem chi tiết hóa đơn.
- Map dòng hóa đơn với dòng nhập kho.
- Kiểm tra MST/ngày/số hóa đơn.
- Không làm sai tổng tiền nhập kho.
# Module INVOICE - Hóa đơn bán ra / Hóa đơn điện tử

## 1. Mục tiêu module

Module INVOICE dùng để quản lý hóa đơn bán ra phát sinh từ POS và tích hợp hóa đơn điện tử Viettel SInvoice.

Luồng chính:

```text
POS bán hàng
→ Tạo InvoiceHead
→ Sinh InvoiceDetail từ OrderLine
→ Kiểm tra JSON Viettel
→ Preview PDF nháp
→ Phát hành hóa đơn thật
→ Tra cứu UUID nếu lỗi/timeout
→ Tải PDF/XML chính thức
→ Gửi email hóa đơn cho khách
→ Theo dõi log tích hợp
```

---

## 2. Các bảng / entity chính

### InvoiceHead

Lưu thông tin đầu hóa đơn bán ra.

Các nhóm dữ liệu chính:

* OrderId
* InvoiceNumber
* InvoiceDate
* BuyerName
* BuyerTaxCode
* BuyerAddress
* TotalQuantity
* SubTotal
* VatAmount
* GrandTotal
* Note
* IsLocked
* LockReason

Thông tin tích hợp Viettel:

* ProviderStatus
* TransactionUuid
* ProviderCode
* SupplierTaxCode
* InvoiceType
* TemplateCode
* InvoiceSeries
* ProviderInvoiceNo
* ProviderTransactionId
* ReservationCode
* CodeOfTax
* IssuedAtUtc
* LastSyncedAtUtc
* LastErrorCode
* LastErrorMessage
* PdfFilePath
* ZipFilePath

---

### InvoiceDetail

Lưu dòng chi tiết hóa đơn.

Các nguồn dòng:

* FromOrderLine: dòng sinh từ đơn bán POS.
* Manual: dòng thêm thủ công.

Các trường chính:

* InvoiceHeadId
* OrderLineId
* ProductVariantId
* SourceType
* ItemName
* UnitName
* Quantity
* UnitPrice
* Amount
* VatRate
* VatAmount
* TotalAmount
* Note

---

### InvoiceProviderSetting

Lưu cấu hình nhà cung cấp hóa đơn điện tử.

Viettel hiện dùng Basic Auth.

Các trường quan trọng:

* ProviderCode
* AuthMode
* IsProduction
* BaseUrl
* Username
* Password
* SupplierTaxCode
* InvoiceType
* TemplateCode
* InvoiceSeries
* CurrencyCode
* ExchangeRate
* PaymentMethodName
* CusGetInvoiceRight
* DefaultPaymentStatus
* IsActive

---

### InvoiceIntegrationLog

Ghi log toàn bộ các lần gọi API Viettel.

Các ActionType chính:

* BuildPayload
* PreviewDraft
* IssueInvoice
* SearchByTransactionUuid
* DownloadPdf
* DownloadZip
* SendEmail
* UpdatePaymentStatus
* CancelPaymentStatus
* CancelInvoice

Các trường log:

* InvoiceHeadId
* ActionType
* RequestUrl
* RequestBody
* ResponseBody
* IsSuccess
* ErrorCode
* ErrorMessage
* StartedAtUtc
* FinishedAtUtc
* DurationMs

---

## 3. Trạng thái ProviderStatus

```text
LocalDraft = 0
ReadyToIssue = 1
Previewed = 2
DraftSent = 3
Issuing = 4
Issued = 5
IssuedWaitingNumber = 6
IssueFailed = 7
Cancelled = 8
PdfDownloaded = 9
ZipDownloaded = 10
EmailSent = 11
```

Ý nghĩa:

* LocalDraft: hóa đơn mới tạo nội bộ.
* ReadyToIssue: sẵn sàng phát hành.
* Previewed: đã preview PDF nháp.
* Issuing: đang gọi API phát hành.
* Issued: đã phát hành thành công.
* IssuedWaitingNumber: chưa rõ số hóa đơn, cần tra cứu UUID.
* IssueFailed: phát hành lỗi.
* PdfDownloaded: đã tải PDF chính thức.
* ZipDownloaded: đã tải ZIP/XML chính thức.
* EmailSent: đã gửi email hóa đơn cho khách.

---

## 4. Tích hợp Viettel SInvoice

### 4.1. Preview PDF nháp

Mục đích:

* Kiểm tra hình thức hóa đơn trước khi phát hành thật.
* Không sinh số hóa đơn thật.
* Không khóa hóa đơn.

Màn hình:

```text
/Admin/Invoice/ViettelPayload/{invoiceHeadId}
```

API Viettel dùng:

```text
createInvoiceDraftPreview
```

---

### 4.2. Phát hành hóa đơn thật

Mục đích:

* Gửi JSON hóa đơn lên Viettel.
* Viettel phát hành hóa đơn thật.
* GaoApp lưu số hóa đơn Viettel.
* GaoApp khóa hóa đơn sau phát hành.

API Viettel dùng:

```text
createInvoice
```

Sau khi thành công, cập nhật:

* ProviderStatus = Issued
* ProviderInvoiceNo
* InvoiceNumber
* ProviderTransactionId
* ReservationCode
* CodeOfTax
* IssuedAtUtc
* IsLocked = true
* LockReason

---

### 4.3. Tra cứu UUID

Mục đích:

* Xử lý trường hợp phát hành bị HTTP 500, TIMEOUT hoặc không rõ kết quả.
* Tránh phát hành trùng hóa đơn.
* Đồng bộ lại số hóa đơn nếu Viettel đã phát hành nhưng GaoApp chưa nhận được response.

API Viettel dùng:

```text
searchInvoiceByTransactionUuid
```

Quy tắc:

```text
Nếu tìm thấy:
    cập nhật ProviderStatus = Issued
    cập nhật ProviderInvoiceNo
    khóa hóa đơn

Nếu không tìm thấy:
    cho phép phát hành lại bằng transactionUuid cũ

Nếu GaoApp đã có ProviderInvoiceNo:
    không hạ trạng thái xuống ReadyToIssue
    chỉ ghi lỗi đối soát
```

---

### 4.4. Tải PDF chính thức

Mục đích:

* Tải file PDF chính thức từ Viettel sau khi hóa đơn đã phát hành.
* Lưu file vào thư mục upload.
* Lưu đường dẫn vào InvoiceHead.PdfFilePath.

API Viettel dùng:

```text
createExchangeInvoiceFile
```

---

### 4.5. Tải ZIP/XML chính thức

Mục đích:

* Tải file biểu diễn hóa đơn / XML chính thức từ Viettel.
* Lưu file vào thư mục upload.
* Lưu đường dẫn vào InvoiceHead.ZipFilePath.

API Viettel dùng:

```text
getInvoiceRepresentationFile
```

---

### 4.6. Gửi email hóa đơn

Mục đích:

* Gửi hóa đơn đã phát hành cho khách hàng qua email.
* Có thể nhập nhiều email, cách nhau bằng dấu chấm phẩy.

API Viettel dùng:

```text
sendEmailToCustomer
```

Body chính:

```json
{
  "supplierTaxCode": "...",
  "transactionUuid": "...",
  "buyerEmail": "email1@gmail.com;email2@gmail.com"
}
```

Sau khi gửi thành công:

* ProviderStatus = EmailSent
* LastSyncedAtUtc = DateTime.UtcNow
* LastErrorCode = null
* LastErrorMessage = null

---

## 5. Luồng chống phát hành trùng

Quy tắc bảo vệ:

```text
1. Nếu hóa đơn đã có ProviderInvoiceNo:
   Không cho phát hành lại.

2. Nếu ProviderStatus = Issuing:
   Không cho phát hành lại.

3. Nếu ProviderStatus = IssuedWaitingNumber:
   Bắt buộc tra cứu UUID trước.

4. Nếu ProviderStatus = IssueFailed và lỗi là HTTP_500 / TIMEOUT:
   Bắt buộc tra cứu UUID trước khi phát hành lại.

5. Nếu tra cứu UUID không thấy hóa đơn:
   Có thể phát hành lại bằng transactionUuid cũ.

6. Nếu tra cứu UUID thấy hóa đơn:
   Đồng bộ về Issued và khóa hóa đơn.
```

---

## 6. UI quản trị

### ViettelPayload

Đường dẫn:

```text
/Admin/Invoice/ViettelPayload/{invoiceHeadId}
```

Chức năng:

* Xem JSON gửi Viettel.
* Xem trạng thái Viettel.
* Preview PDF nháp.
* Phát hành hóa đơn.
* Tra cứu UUID.
* Tải PDF chính thức.
* Tải ZIP/XML chính thức.
* Xem PDF đã lưu.
* Tải ZIP đã lưu.
* Gửi email hóa đơn.
* Xem log gần nhất.

---

### Invoice Detail

Đường dẫn:

```text
/Admin/Invoice/Detail/{invoiceHeadId}
```

Chức năng:

* Xem thông tin hóa đơn.
* Xem dòng tự sinh từ OrderLine.
* Xem dòng manual.
* Thêm/sửa/xóa dòng manual nếu hóa đơn chưa khóa.
* Không cho sửa nếu hóa đơn đã phát hành Viettel.

---

### InvoiceIntegrationLog

Đường dẫn:

```text
/Admin/InvoiceIntegrationLog
```

Chức năng:

* Xem danh sách log tích hợp Viettel.
* Lọc theo ngày.
* Lọc theo InvoiceHeadId.
* Lọc theo OrderId.
* Lọc theo ActionType.
* Lọc theo thành công / lỗi.
* Tìm theo URL, UUID, mã lỗi, số hóa đơn.
* Xem chi tiết RequestBody / ResponseBody.
* Copy request/response để debug.

---

## 7. Các phase đã hoàn thành

```text
Phase 1  - Nền dữ liệu tích hợp Viettel: PASS
Phase 2  - Cấu hình Viettel Basic Auth: PASS
Phase 3  - Build JSON Viettel: PASS
Phase 4  - Preview PDF nháp: PASS
Phase 5  - Lưu log preview + iframe PDF: PASS
Phase 6  - Tạo hóa đơn nháp Viettel: BỎ QUA
Phase 7  - Phát hành hóa đơn thật: PASS
Phase 8  - Tải PDF/XML chính thức: PASS
Phase 9  - Tra cứu UUID / đồng bộ sau timeout: PASS
Phase 10 - Gửi email hóa đơn cho khách: PASS
Phase 11 - Cập nhật trạng thái thanh toán Viettel: TẠM BỎ QUA
Phase 13 - Màn quản trị log tích hợp: PASS
Phase 15 - Chống bấm nhầm / phát hành trùng / UI an toàn: PASS
```

---

## 8. Việc còn lại / làm sau

### Phase 11 - Trạng thái thanh toán Viettel

Tạm bỏ qua.

Khi làm sau sẽ thêm:

* updatePaymentStatus
* cancelPaymentStatus
* Log UpdatePaymentStatus
* Log CancelPaymentStatus

---

### Phase 14 - Dọn log định kỳ

Dự kiến:

* Log preview giữ 30 ngày.
* Log lỗi giữ lâu hơn.
* Log phát hành và tra cứu UUID giữ dài hạn.

---

### Phase 16 - Hủy / thay thế / điều chỉnh hóa đơn

Dự kiến làm sau vì nghiệp vụ kế toán phức tạp.

Không sửa trực tiếp hóa đơn đã phát hành.

Nếu sai hóa đơn sẽ xử lý bằng:

* Hủy hóa đơn
* Thay thế hóa đơn
* Điều chỉnh hóa đơn

---

### Phase 17 - Đồng bộ danh sách hóa đơn từ Viettel

Dự kiến:

* Lấy danh sách hóa đơn từ Viettel theo ngày.
* So sánh với InvoiceHead trong GaoApp.
* Cập nhật những hóa đơn thiếu số, thiếu trạng thái, thiếu file.

---

### Phase 18 - Dashboard hóa đơn điện tử

Dự kiến:

* Tổng số hóa đơn đã phát hành.
* Tổng số hóa đơn lỗi.
* Tổng số hóa đơn chưa tải PDF/XML.
* Tổng số email đã gửi.
* Thống kê theo ngày/tháng.
