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
---

# Hóa đơn điện tử Viettel SInvoice

## Mục tiêu

Module Invoice hiện hỗ trợ hóa đơn bán ra điện tử qua Viettel SInvoice, gồm các nhóm chức năng:

* Tạo hóa đơn bán ra từ đơn POS sau khi hoàn tất thanh toán.
* Xem JSON payload trước khi phát hành.
* Preview hóa đơn nháp.
* Phát hành hóa đơn thật lên Viettel.
* Tra cứu hóa đơn theo `transactionUuid`.
* Tải PDF/XML hóa đơn chính thức.
* Gửi email hóa đơn cho khách.
* Đồng bộ danh sách hóa đơn từ Viettel.
* Dashboard theo dõi hóa đơn điện tử.
* Xử lý sai sót hóa đơn: thay thế, điều chỉnh tiền, điều chỉnh thông tin.

## Văn bản nghiệp vụ đang áp dụng

Ghi nhận theo quy định hiện hành từ 01/06/2025:

* Nghị định 70/2025/NĐ-CP sửa đổi, bổ sung Nghị định 123/2020/NĐ-CP về hóa đơn, chứng từ.
* Thông tư 32/2025/TT-BTC hướng dẫn thực hiện Nghị định 123/2020/NĐ-CP và Nghị định 70/2025/NĐ-CP.

## Entity chính

### InvoiceHead

Hóa đơn bán ra của GaoApp.

Các nhóm trường quan trọng:

* Thông tin hóa đơn nội bộ:

  * `OrderId`
  * `InvoiceNumber`
  * `InvoiceDate`
  * `BuyerName`
  * `BuyerTaxCode`
  * `BuyerAddress`
  * `SubTotal`
  * `VatAmount`
  * `GrandTotal`
  * `IsLocked`

* Thông tin tích hợp Viettel:

  * `ProviderCode`
  * `ProviderStatus`
  * `TransactionUuid`
  * `SupplierTaxCode`
  * `InvoiceType`
  * `TemplateCode`
  * `InvoiceSeries`
  * `ProviderInvoiceNo`
  * `ProviderTransactionId`
  * `ReservationCode`
  * `CodeOfTax`
  * `IssuedAtUtc`
  * `LastSyncedAtUtc`
  * `LastErrorCode`
  * `LastErrorMessage`
  * `PdfFilePath`
  * `ZipFilePath`

* Thông tin xử lý sai sót:

  * `OriginalInvoiceHeadId`
  * `CorrectionType`
  * `OriginalInvoiceNo`
  * `OriginalInvoiceIssuedAtUtc`
  * `AdjustedNote`
  * `AdditionalReferenceDesc`
  * `AdditionalReferenceDateUtc`

### InvoiceDetail

Dòng hàng của hóa đơn bán ra.

Các trường quan trọng:

* `InvoiceHeadId`
* `OrderLineId`
* `ProductVariantId`
* `SourceType`
* `ItemName`
* `UnitName`
* `Quantity`
* `UnitPrice`
* `Amount`
* `VatRate`
* `VatAmount`
* `TotalAmount`
* `Note`

### InvoiceProviderSetting

Cấu hình nhà cung cấp hóa đơn điện tử.

Các trường quan trọng:

* `ProviderCode`
* `AuthMode`
* `IsProduction`
* `BaseUrl`
* `Username`
* `Password`
* `SupplierTaxCode`
* `InvoiceType`
* `TemplateCode`
* `InvoiceSeries`
* `CurrencyCode`
* `ExchangeRate`
* `PaymentMethodName`
* `CusGetInvoiceRight`
* `DefaultPaymentStatus`
* `IsActive`

Ghi chú bảo mật:

* Không hiển thị mật khẩu Viettel trên UI.
* Không ghi mật khẩu/token vào log.
* Production cần dùng DataProtection để mã hóa mật khẩu cấu hình.

### InvoiceCorrectionCase

Hồ sơ xử lý sai sót hóa đơn.

Các trường quan trọng:

* `OriginalInvoiceHeadId`
* `NewInvoiceHeadId`
* `Type`
* `Status`
* `Reason`
* `AgreementDocumentNo`
* `AgreementDateUtc`
* `IssuedAtUtc`
* `LastErrorCode`
* `LastErrorMessage`

## Enum quan trọng

### InvoiceProviderStatus

* `LocalDraft`: hóa đơn nội bộ.
* `ReadyToIssue`: sẵn sàng phát hành.
* `Previewed`: đã preview nháp.
* `Issuing`: đang phát hành.
* `Issued`: đã phát hành.
* `IssuedWaitingNumber`: đã gửi nhưng đang chờ tra cứu số hóa đơn.
* `IssueFailed`: phát hành lỗi.
* `PdfDownloaded`: đã tải PDF.
* `ZipDownloaded`: đã tải ZIP/XML.
* `EmailSent`: đã gửi email.

### InvoiceCorrectionType

* `Replacement`: hóa đơn thay thế.
* `AdjustmentAmount`: hóa đơn điều chỉnh tiền.
* `AdjustmentInfo`: hóa đơn điều chỉnh thông tin.

### InvoiceCorrectionStatus

* `Draft`: nháp.
* `ReadyToIssue`: sẵn sàng phát hành.
* `Issuing`: đang phát hành.
* `Issued`: đã phát hành.
* `Failed`: lỗi.
* `Cancelled`: đã hủy.

---

# Luồng phát hành Viettel

## 1. Tạo hóa đơn bán ra từ POS

Sau khi đơn POS hoàn tất, hệ thống tạo `InvoiceHead` và `InvoiceDetail`.

Điểm cần kiểm tra:

* Mỗi đơn POS chỉ có một hóa đơn gốc chưa xóa mềm.
* Hóa đơn gốc có `OriginalInvoiceHeadId = NULL`.
* Các hóa đơn thay thế/điều chỉnh được phép dùng cùng `OrderId`, nhưng phải có `OriginalInvoiceHeadId`.

Index quan trọng:

* Unique `{StoreId, OrderId}` chỉ áp dụng cho hóa đơn gốc.
* Filter nên là:

  * `IsDeleted = 0`
  * `OriginalInvoiceHeadId IS NULL`

## 2. Xem JSON Viettel

Màn `ViettelPayload` dùng để kiểm tra payload trước khi phát hành.

Cần kiểm tra:

* `supplierTaxCode`
* `templateCode`
* `invoiceSeries`
* `transactionUuid`
* `generalInvoiceInfo`
* `buyerInfo`
* `itemInfo`
* `summarizeInfo`

## 3. Phát hành hóa đơn

Khi phát hành:

* Nếu chưa có `TransactionUuid`, hệ thống tự sinh UUID.
* Trạng thái chuyển sang `Issuing`.
* Gửi payload sang Viettel.
* Nếu thành công:

  * `ProviderStatus = Issued` hoặc `IssuedWaitingNumber`
  * Lưu `ProviderInvoiceNo`
  * Lưu `ProviderTransactionId`
  * Lưu `ReservationCode`
  * Lưu `CodeOfTax`
  * Lưu `IssuedAtUtc`
  * Khóa hóa đơn nội bộ.
* Nếu lỗi:

  * `ProviderStatus = IssueFailed`
  * Lưu `LastErrorCode`
  * Lưu `LastErrorMessage`

## 4. Tra cứu UUID

Dùng khi phát hành bị timeout hoặc HTTP 500 chưa rõ kết quả.

Quy tắc:

* Nếu `ProviderStatus = Issuing` hoặc `IssuedWaitingNumber`, không cho phát hành lại ngay.
* Phải tra cứu theo `TransactionUuid` trước.
* Nếu Viettel trả về hóa đơn đã phát hành thì đồng bộ lại `ProviderInvoiceNo`, `CodeOfTax`, `IssuedAtUtc`.

## 5. Tải PDF/XML chính thức

Sau khi hóa đơn đã phát hành, hệ thống tải file chính thức từ Viettel.

Cần đảm bảo snapshot cấu hình đúng lúc phát hành:

* `SupplierTaxCode`
* `InvoiceType`
* `TemplateCode`
* `InvoiceSeries`

Không phụ thuộc cấu hình hiện tại nếu sau này đổi mẫu/ký hiệu.

---

# Xử lý sai sót hóa đơn

## Nguyên tắc chung

Hóa đơn đã phát hành không sửa trực tiếp. Khi phát hiện sai phải xử lý bằng một trong các hình thức:

* Thông báo sai sót.
* Hóa đơn điều chỉnh.
* Hóa đơn thay thế.

Trong GaoApp hiện đã triển khai:

* Hóa đơn thay thế.
* Hóa đơn điều chỉnh tiền.
* Hóa đơn điều chỉnh thông tin.

## Khi nào dùng điều chỉnh tiền

Dùng `AdjustmentAmount` khi sai về giá trị hàng hóa/dịch vụ.

Các trường hợp thường gặp:

* Sai số lượng.
* Sai đơn giá.
* Sai thành tiền.
* Sai thuế suất.
* Sai tiền thuế.
* Khách trả hàng.
* Giảm giá sau bán.
* Bổ sung phần chênh lệch tăng/giảm.

Quy tắc dữ liệu:

* Điều chỉnh tăng: dòng điều chỉnh mang giá trị tăng.
* Điều chỉnh giảm: dòng điều chỉnh mang giá trị giảm.
* Payload Viettel dùng `isIncreaseItem` để xác định tăng/giảm.
* `GrandTotal` của hóa đơn điều chỉnh tiền phải khác 0.

## Khi nào dùng điều chỉnh thông tin

Dùng `AdjustmentInfo` khi cần sửa thông tin nhưng không làm thay đổi tiền.

Các trường hợp thường gặp:

* Sai thông tin người mua.
* Sai ghi chú.
* Sai thông tin tham chiếu.
* Sai thông tin không ảnh hưởng tiền, thuế, hàng hóa.

Quy tắc dữ liệu trong GaoApp:

* Không copy lại toàn bộ dòng hàng gốc.
* Tạo một dòng mô tả:

  * `ItemName = Điều chỉnh thông tin hóa đơn ...`
  * `Quantity = 0`
  * `UnitPrice = 0`
  * `Amount = 0`
  * `VatAmount = 0`
  * `TotalAmount = 0`
* `GrandTotal` có thể bằng 0.
* Payload vẫn phải có dòng `itemInfo` hợp lệ để Viettel nhận.

## Khi nào dùng thay thế

Dùng `Replacement` khi muốn lập lại một hóa đơn mới thay cho hóa đơn cũ.

Các trường hợp thường gặp:

* Sai nhiều dòng hàng.
* Sai toàn bộ thông tin người mua cần lập lại rõ ràng.
* Sai nghiêm trọng làm hóa đơn cũ khó đối chiếu.
* Muốn hóa đơn mới là bộ dữ liệu đúng đầy đủ.

Quy tắc dữ liệu:

* Hóa đơn thay thế copy dữ liệu từ hóa đơn gốc.
* Người dùng có thể sửa lại thông tin trước khi phát hành.
* Hóa đơn thay thế phải có `GrandTotal > 0`.
* Payload Viettel phải có thông tin hóa đơn bị thay thế:

  * `originalInvoiceId`
  * `originalTemplateCode`
  * `originalInvoiceIssueDate`

---

# Quy tắc số lần xử lý sai sót

## Điều chỉnh được nhiều lần

Một hóa đơn hiện hành có thể có nhiều hóa đơn điều chỉnh.

Mô hình:

```text
F0 + DC1 + DC2 + DC3
```

Áp dụng cho:

* Điều chỉnh tiền nhiều lần.
* Điều chỉnh thông tin nhiều lần.

## Thay thế đi theo chuỗi

Một hóa đơn chỉ có một hóa đơn thay thế trực tiếp.

Mô hình đúng:

```text
F0 -> TT1 -> TT2 -> TT3
```

Nếu `F0` đã được thay thế bởi `TT1`, không xử lý tiếp trên `F0`. Phải mở `TT1`.

Nếu `TT1` sai, lập `TT2` thay thế cho `TT1`.

## Không chuyển phương án tùy tiện

Quy tắc hệ thống đang áp dụng:

* Nếu lần đầu đã điều chỉnh thì các lần sau tiếp tục điều chỉnh trên hóa đơn hiện hành.
* Nếu đã thay thế thì xử lý tiếp trên hóa đơn thay thế mới nhất.
* Không lập hồ sơ xử lý sai sót trực tiếp từ hóa đơn điều chỉnh.
* Không tạo hồ sơ mới nếu đang có hồ sơ `Draft`, `ReadyToIssue`, `Issuing`, hoặc `Failed` chưa xử lý.

---

# Màn hình quản trị

## ViettelPayload

Dùng để:

* Xem JSON payload.
* Preview PDF nháp.
* Phát hành hóa đơn thật.
* Tra cứu UUID.
* Tải PDF/XML.
* Gửi email hóa đơn.
* Tạo hồ sơ xử lý sai sót.
* Xem lịch sử xử lý sai sót.

## Invoice Detail

Dùng để:

* Xem thông tin hóa đơn nội bộ.
* Xem trạng thái Viettel.
* Xem danh sách dòng hóa đơn.
* Xem lịch sử hóa đơn gốc → thay thế/điều chỉnh.

## InvoiceCorrection

Dùng để:

* Tạo hóa đơn thay thế.
* Tạo hóa đơn điều chỉnh tiền.
* Tạo hóa đơn điều chỉnh thông tin.
* Ghi lý do sai sót.
* Ghi số văn bản/thỏa thuận.
* Ghi ngày văn bản/thỏa thuận.

---

# Test bắt buộc khi sửa Invoice

## Test phát hành hóa đơn gốc

* Tạo đơn POS.
* Hoàn tất thanh toán.
* Kiểm tra `InvoiceHead`.
* Xem JSON.
* Preview nháp.
* Phát hành thật.
* Tra cứu UUID.
* Tải PDF/XML.
* Gửi email.

## Test thay thế

* Chọn hóa đơn đã phát hành, có `CodeOfTax`.
* Tạo hóa đơn thay thế.
* Kiểm tra payload có:

  * `adjustmentType = 3`
  * `adjustmentInvoiceType = 1`
  * `originalInvoiceId`
  * `originalTemplateCode`
* Phát hành thành công.
* Kiểm tra `InvoiceCorrectionCases.Status = Issued`.
* Quay lại hóa đơn cũ, hệ thống phải chặn xử lý tiếp trên hóa đơn đã bị thay thế.
* Mở hóa đơn thay thế, tạo thay thế tiếp được.

## Test điều chỉnh tiền

* Tạo điều chỉnh tăng.
* Tạo điều chỉnh giảm.
* Kiểm tra payload có `isIncreaseItem`.
* Phát hành thành công.
* Kiểm tra được lập nhiều lần.
* Kiểm tra không cho đổi sang thay thế nếu lần đầu đã điều chỉnh.

## Test điều chỉnh thông tin

* Tạo điều chỉnh thông tin.
* Kiểm tra không copy dòng hàng gốc.
* Kiểm tra có một dòng mô tả điều chỉnh.
* Tổng tiền bằng 0.
* Phát hành thành công.
* Kiểm tra được lập nhiều lần.

## Test chống treo hồ sơ

* Tạo một hồ sơ điều chỉnh nháp.
* Chưa phát hành.
* Tạo hồ sơ mới trên cùng hóa đơn.
* Kỳ vọng bị chặn.

## Test bảo mật

* Không log mật khẩu Viettel.
* Không hiển thị mật khẩu Viettel trên UI.
* Chỉ ADMIN được sửa cấu hình Viettel.
* Log request/response không chứa thông tin nhạy cảm không cần thiết.

---

# File chính đã tham gia module

## Domain

* `GaoApp.Domain/Entities/InvoiceHead.cs`
* `GaoApp.Domain/Entities/InvoiceDetail.cs`
* `GaoApp.Domain/Entities/InvoiceProviderSetting.cs`
* `GaoApp.Domain/Entities/InvoiceIntegrationLog.cs`
* `GaoApp.Domain/Entities/InvoiceCorrectionCase.cs`
* `GaoApp.Domain/Enums/InvoiceProviderStatus.cs`
* `GaoApp.Domain/Enums/InvoiceCorrectionType.cs`
* `GaoApp.Domain/Enums/InvoiceCorrectionStatus.cs`

## Application

* `GaoApp.Application/Services/Invoices/InvoiceService.cs`
* `GaoApp.Application/Services/Invoices/ViettelInvoicePayloadBuilder.cs`
* `GaoApp.Application/Services/Invoices/ViettelInvoiceIssueService.cs`
* `GaoApp.Application/Services/Invoices/ViettelInvoicePreviewService.cs`
* `GaoApp.Application/Services/Invoices/ViettelInvoiceFileService.cs`
* `GaoApp.Application/Services/Invoices/ViettelInvoiceLookupService.cs`
* `GaoApp.Application/Services/Invoices/ViettelInvoiceEmailService.cs`
* `GaoApp.Application/Services/Invoices/InvoiceCorrectionService.cs`
* `GaoApp.Application/Interfaces/Repositories/Invoices/IInvoiceRepository.cs`
* `GaoApp.Application/Interfaces/Repositories/Invoices/IInvoiceCorrectionRepository.cs`
* `GaoApp.Application/Interfaces/Services/Invoices/IInvoiceCorrectionService.cs`

## Infrastructure

* `GaoApp.Infrastructure/Repositories/Invoices/InvoiceRepository.cs`
* `GaoApp.Infrastructure/Repositories/Invoices/InvoiceCorrectionRepository.cs`
* `GaoApp.Infrastructure/Persistence/Configurations/Invoices/InvoiceHeadConfiguration.cs`
* `GaoApp.Infrastructure/Persistence/Configurations/Invoices/InvoiceCorrectionCaseConfiguration.cs`

## Web

* `GaoApp.Web/Areas/Admin/Controllers/InvoiceController.cs`
* `GaoApp.Web/Areas/Admin/Controllers/InvoiceCorrectionController.cs`
* `GaoApp.Web/Areas/Admin/Views/Invoice/Detail.cshtml`
* `GaoApp.Web/Areas/Admin/Views/Invoice/ViettelPayload.cshtml`
* `GaoApp.Web/Areas/Admin/Views/Invoice/_InvoiceCorrectionHistory.cshtml`
* `GaoApp.Web/Areas/Admin/Views/InvoiceCorrection/Create.cshtml`

