
## 2026-06-09

### Security / Admin Authorization

Đã làm:
- Thêm `[Authorize]` vào `BaseAdminController` để toàn bộ controller kế thừa Admin base mặc định bắt đăng nhập.
- Bổ sung `[Authorize]` cho các controller Admin dạng `Controller` chưa kế thừa `BaseAdminController`.
- Bổ sung `[Authorize]` cho các API Admin dạng `ControllerBase`.
- Thêm `[AllowAnonymous]` cho `AccountController.Login` và `AccountController.AccessDenied`.
- Thêm `[Authorize]` cho `AccountController.Logout`.
- Khóa endpoint debug `/__tenant` và `/__tenant-hash`, chỉ cho chạy trong môi trường Development.
- Giữ `/ping` vì không trả dữ liệu nhạy cảm.

Đã test:
- Chưa đăng nhập vào `/admin/pos` bị chuyển về login.
- Chưa đăng nhập vào `/admin/api/...` không trả dữ liệu.
- Trang `/admin/account/login` vẫn vào được.
- Sau khi đăng nhập, POS/Admin vẫn hoạt động.

Cần kiểm tra khi publish IIS:
- Môi trường Production không truy cập được `/__tenant`.
- Môi trường Production không truy cập được `/__tenant-hash`.
# GaoApp - AI Changelog

File này dùng để ghi ngắn gọn sau mỗi lần hoàn thành chức năng. Không cần tạo file mới cho từng chức năng.

## Mẫu ghi

```md
## 2026-06-09 - Module / Chức năng

### Đã làm
- ...

### File đã sửa
- `path/file.cs`
- `path/file.js`

### Cần test
- ...

### Lưu ý cho lần chat sau
- ...
```

---

## 2026-06-09 - Khởi tạo tài liệu AI

### Đã làm

- Quét source `GaoApp(14).zip`.
- Tạo bộ tài liệu AI context cho dự án.
- Tạo source map theo module.
- Tạo hướng dẫn mở chat mới.

### Cần làm tiếp

- Copy thư mục `docs` vào source.
- Commit Git.
- Từ các lần sau, sau khi PASS một chức năng thì cập nhật file này.

## 2026-06-09 - Promotion/POS

### Đã làm

* Sửa lỗi promotion giảm tiền / giảm % không áp dụng ngay khi mua số lượng 1.
* Nguyên nhân: `OrderLine` mới thêm vào giỏ chưa có `StoreId`, trong khi `PromotionEngine` kiểm tra `line.StoreId <= 0` nên bỏ qua promotion ở lần đầu.
* Đã bổ sung gán `StoreId = order.StoreId` khi thêm hoặc merge dòng hàng trong POS.
* Đã điều chỉnh `PromotionEngine` dùng `effectiveStoreId` từ `line.StoreId` hoặc fallback từ `order.StoreId`.
* Bổ sung SQL tạo/cập nhật các cột lưu snapshot promotion trên `OrderLines` và tổng promotion/combo trên `Orders`.

### File liên quan

* `GaoApp.Application/Services/Orders/POSService.cs`
* `GaoApp.Application/Services/Promotions/PromotionEngine.cs`
* SQL Server:

  * `dbo.OrderLines`
  * `dbo.Orders`

### Cần test

* Tạo giỏ mới, quét sản phẩm có giảm tiền 1 lần, kiểm tra promotion áp dụng ngay.
* Tạo giỏ mới, quét sản phẩm có giảm % 1 lần, kiểm tra promotion áp dụng ngay.
* Quét tiếp số lượng 2 trở lên, kiểm tra tiền giảm nhân đúng theo số lượng.
* Reload lại giỏ, kiểm tra promotion vẫn hiển thị đúng.
* Chốt đơn, kiểm tra dữ liệu promotion được lưu trong `OrderLines`.
* Kiểm tra Buy X Get Y nếu có: dòng hàng tặng giá 0, không làm tăng `GrandTotal`.

### Lưu ý cho lần chat sau

* Khi sửa Promotion/POS cần kiểm tra `StoreId` của `OrderLine` mới tạo trước khi gọi `PromotionEngine`.
* Promotion nên được áp sau khi giá bán/đơn vị bán đã được xác định.
* Không query promotion từng dòng; ưu tiên load active promotions một lần rồi xử lý trên lines đã loaded.


- Khóa endpoint debug `/__tenant` và `/__tenant-hash`, chỉ cho chạy ở môi trường Development.
- Giữ `/ping` vì không trả dữ liệu nhạy cảm.

## 2026-06-18

### INVOICE - Tích hợp Viettel SInvoice

Đã hoàn thiện các phần chính của module hóa đơn điện tử Viettel SInvoice.

Đã làm:

* Tạo nền dữ liệu tích hợp nhà cung cấp hóa đơn điện tử.
* Thêm cấu hình nhà cung cấp Viettel SInvoice dùng Basic Auth.
* Xây dựng payload JSON đúng cấu trúc Viettel.
* Hỗ trợ xem JSON Viettel trước khi phát hành.
* Hỗ trợ preview PDF nháp từ Viettel.
* Ghi log các lần gọi API preview.
* Phát hành hóa đơn thật lên Viettel.
* Lưu số hóa đơn Viettel sau khi phát hành thành công.
* Lưu các thông tin trả về từ Viettel:

  * ProviderInvoiceNo
  * ProviderTransactionId
  * ReservationCode
  * CodeOfTax
  * IssuedAtUtc
* Khóa hóa đơn sau khi phát hành để chống sửa sai dữ liệu.
* Tải PDF chính thức từ Viettel.
* Tải ZIP/XML chính thức từ Viettel.
* Lưu đường dẫn file PDF/XML vào InvoiceHead:

  * PdfFilePath
  * ZipFilePath
* Thêm chức năng tra cứu hóa đơn theo TransactionUuid.
* Dùng tra cứu UUID để xử lý trường hợp phát hành bị HTTP 500 / TIMEOUT / không rõ kết quả.
* Chống phát hành trùng hóa đơn:

  * Đã phát hành thì ẩn nút phát hành.
  * Nếu lỗi không rõ kết quả thì bắt buộc tra cứu UUID trước khi phát hành lại.
  * Nếu đã có số hóa đơn Viettel thì không cho phát hành lại.
* Hoàn thiện UI an toàn cho màn ViettelPayload:

  * Hiển thị trạng thái Viettel.
  * Ẩn PDF nháp sau khi đã phát hành.
  * Chỉ hiện nút tải PDF/XML khi hóa đơn đã phát hành.
  * Hiện nút gửi email khi hóa đơn đã phát hành.
* Thêm chức năng gửi email hóa đơn cho khách qua Viettel.
* Hỗ trợ nhập nhiều email, cách nhau bằng dấu chấm phẩy.
* Ghi log gửi email hóa đơn.
* Thêm màn quản trị log tích hợp:

  * Danh sách log Viettel.
  * Lọc theo ngày.
  * Lọc theo InvoiceHeadId.
  * Lọc theo OrderId.
  * Lọc theo ActionType.
  * Lọc theo thành công / lỗi.
  * Xem chi tiết RequestBody / ResponseBody.
  * Copy request/response để debug.

Tạm bỏ qua:

* Phase 6: Tạo hóa đơn nháp Viettel.
* Phase 11: Cập nhật / hủy trạng thái thanh toán Viettel.

File / khu vực liên quan:

* InvoiceHead
* InvoiceDetail
* InvoiceProviderSetting
* InvoiceIntegrationLog
* InvoiceController
* InvoiceIntegrationLogController
* ViettelInvoicePayloadBuilder
* ViettelInvoiceIssueService
* ViettelInvoiceSyncService
* ViettelOfficialFileService
* ViettelInvoiceEmailService
* ViettelPayload.cshtml
* Detail.cshtml
* InvoiceIntegrationLog/Index.cshtml
* InvoiceIntegrationLog/Detail.cshtml

# CHANGELOG_AI

## 2026-06-19

### Invoice / Viettel SInvoice

Đã hoàn thiện Phase 16 - Xử lý sai sót hóa đơn điện tử.

#### Đã làm

* Thêm luồng lập hóa đơn thay thế.
* Thêm luồng lập hóa đơn điều chỉnh tiền.
* Thêm luồng lập hóa đơn điều chỉnh thông tin.
* Bổ sung entity/hồ sơ `InvoiceCorrectionCase`.
* Bổ sung liên kết hóa đơn gốc và hóa đơn xử lý sai sót qua:

  * `OriginalInvoiceHeadId`
  * `CorrectionType`
  * `OriginalInvoiceNo`
  * `OriginalInvoiceIssuedAtUtc`
* Cho phép hóa đơn thay thế/điều chỉnh dùng chung `OrderId` với hóa đơn gốc bằng cách chỉnh unique index hóa đơn gốc.
* Build payload Viettel cho hóa đơn thay thế:

  * `adjustmentType = 3`
  * `adjustmentInvoiceType = 1`
  * Có thông tin hóa đơn bị thay thế.
* Build payload Viettel cho hóa đơn điều chỉnh tiền:

  * `adjustmentType = 5`
  * `adjustmentInvoiceType = 2`
  * Có `isIncreaseItem` để phân biệt tăng/giảm.
  * Cho phép giá trị điều chỉnh âm/dương theo nghiệp vụ.
* Build payload Viettel cho hóa đơn điều chỉnh thông tin:

  * Không copy dòng hàng gốc.
  * Tạo một dòng mô tả điều chỉnh thông tin.
  * Cho phép tổng tiền bằng 0.
* Sửa validate phát hành:

  * Hóa đơn gốc/thay thế phải có tổng tiền hợp lệ.
  * Hóa đơn điều chỉnh tiền phải có tổng tiền khác 0.
  * Hóa đơn điều chỉnh thông tin được phép tổng tiền bằng 0.
* Tự động cập nhật trạng thái hồ sơ xử lý sai sót:

  * Khi phát hành: `Issuing`.
  * Khi lỗi: `Failed`.
  * Khi thành công: `Issued`.
* Thêm màn hiển thị lịch sử xử lý sai sót:

  * Hóa đơn gốc nhìn thấy các hóa đơn thay thế/điều chỉnh.
  * Hóa đơn con nhìn thấy hóa đơn gốc.
  * Có nút mở chi tiết và JSON Viettel.
* Chốt quy tắc nghiệp vụ Phase 16.8:

  * Điều chỉnh tiền/thông tin được lập nhiều lần.
  * Thay thế đi theo chuỗi: hóa đơn gốc → thay thế lần 1 → thay thế lần 2.
  * Không xử lý tiếp trên hóa đơn đã bị thay thế.
  * Không xử lý sai sót trực tiếp từ hóa đơn điều chỉnh.
  * Không chuyển từ điều chỉnh sang thay thế trên cùng hóa đơn nếu lần đầu đã điều chỉnh.
  * Không tạo nhiều hồ sơ `Draft`, `ReadyToIssue`, `Issuing`, `Failed` treo song song.

#### File chính đã sửa/thêm

* `GaoApp.Domain/Entities/InvoiceHead.cs`
* `GaoApp.Domain/Entities/InvoiceDetail.cs`
* `GaoApp.Domain/Entities/InvoiceCorrectionCase.cs`
* `GaoApp.Domain/Enums/InvoiceCorrectionType.cs`
* `GaoApp.Domain/Enums/InvoiceCorrectionStatus.cs`
* `GaoApp.Application/DTOs/Invoices/InvoiceCorrectionDtos.cs`
* `GaoApp.Application/DTOs/Invoices/InvoiceCorrectionHistoryDtos.cs`
* `GaoApp.Application/Interfaces/Repositories/Invoices/IInvoiceCorrectionRepository.cs`
* `GaoApp.Application/Interfaces/Services/Invoices/IInvoiceCorrectionService.cs`
* `GaoApp.Application/Services/Invoices/InvoiceCorrectionService.cs`
* `GaoApp.Application/Services/Invoices/ViettelInvoicePayloadBuilder.cs`
* `GaoApp.Application/Services/Invoices/ViettelInvoiceIssueService.cs`
* `GaoApp.Infrastructure/Repositories/Invoices/InvoiceCorrectionRepository.cs`
* `GaoApp.Infrastructure/Persistence/Configurations/Invoices/InvoiceHeadConfiguration.cs`
* `GaoApp.Infrastructure/Persistence/Configurations/Invoices/InvoiceCorrectionCaseConfiguration.cs`
* `GaoApp.Web/Areas/Admin/Controllers/InvoiceController.cs`
* `GaoApp.Web/Areas/Admin/Controllers/InvoiceCorrectionController.cs`
* `GaoApp.Web/Areas/Admin/Views/Invoice/Detail.cshtml`
* `GaoApp.Web/Areas/Admin/Views/Invoice/ViettelPayload.cshtml`
* `GaoApp.Web/Areas/Admin/Views/Invoice/_InvoiceCorrectionHistory.cshtml`
* `GaoApp.Web/Areas/Admin/Views/InvoiceCorrection/Create.cshtml`

#### Test đã pass

* Phát hành hóa đơn gốc Viettel.
* Tra cứu UUID sau phát hành.
* Tải PDF/XML chính thức.
* Gửi email hóa đơn.
* Lập hóa đơn thay thế.
* Lập hóa đơn điều chỉnh tiền tăng.
* Lập hóa đơn điều chỉnh tiền giảm.
* Lập hóa đơn điều chỉnh thông tin tổng tiền bằng 0.
* Tự động cập nhật `InvoiceCorrectionCases.Status`.
* Hiển thị lịch sử hóa đơn gốc và hóa đơn xử lý sai sót.
* Chặn tạo hồ sơ sai quy tắc nghiệp vụ.

#### Ghi chú nghiệp vụ

* Căn cứ vận hành từ 01/06/2025:

  * Nghị định 70/2025/NĐ-CP.
  * Thông tư 32/2025/TT-BTC.
* Hóa đơn đã phát hành không sửa trực tiếp.
* Điều chỉnh được lập nhiều lần.
* Thay thế đi theo chuỗi.
* Hóa đơn đã bị thay thế không còn là hóa đơn hiện hành để xử lý tiếp.


