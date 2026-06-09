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
