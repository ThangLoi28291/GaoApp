# Module POS Shift

## File chính

- `GaoApp.Domain/Entities/POSShift.cs`
- `GaoApp.Domain/Entities/POSShiftCashTransaction.cs`
- `GaoApp.Domain/Entities/POSShiftCashDenomination.cs`
- `GaoApp.Domain/Entities/POSShiftHandoverSlip.cs`
- `GaoApp.Domain/Entities/POSShiftClosingSlip.cs`
- `GaoApp.Application/Services/POSShifts/POSShiftService.cs`
- `GaoApp.Application/Services/POSShiftHandoverSlips/POSShiftHandoverSlipService.cs`
- `GaoApp.Application/Services/POSShiftClosingSlips/POSShiftClosingSlipService.cs`
- `GaoApp.Infrastructure/Repositories/Orders/POSShiftRepository.cs`
- `GaoApp.Web/Areas/Admin/Controllers/POSShiftController.cs`
- `GaoApp.Web/Areas/Admin/Controllers/POSShiftPageController.cs`
- `GaoApp.Web/Areas/Admin/Controllers/POSShiftManagerDashboardPageController.cs`

## Entity POSShift

Field chính:

- `TerminalId`
- `OpenedByUserId`
- `OpenedAtUtc`
- `Status`
- `ShiftCode`
- `OpeningCash`
- `CashSalesTotal`
- `NonCashSalesTotal`
- `CashRefundTotal`
- `NonCashRefundTotal`
- `RefundCount`
- `VoidCount`
- `CashInTotal`
- `CashOutTotal`
- `ClosingCashExpected`
- `ClosingCashActual`
- `ClosedByUserId`
- `ClosedAtUtc`
- `CurrentOrderId`
- `WarehouseId`

## Nghiệp vụ đã có

- Lấy ca đang mở.
- Mở ca.
- Đóng ca.
- Thu/chi tiền mặt trong ca.
- Lấy danh sách giao dịch tiền mặt.
- Summary ca.
- Lịch sử ca.
- Kiểm tra chủ sở hữu ca.
- Take over.
- Force close.
- In ca.
- Manager dashboard.
- Handover slip.
- Closing slip.

## Quy tắc phải giữ

1. POS muốn bán phải có ca mở.
2. Ca gắn `TerminalId` và `WarehouseId`.
3. Chỉ owner hoặc người có quyền mới thao tác ca người khác.
4. Bán tiền mặt tăng `CashSalesTotal`.
5. Bán không tiền mặt tăng `NonCashSalesTotal`.
6. Refund/Void phải cập nhật lại tổng ca.
7. ClosingCashExpected phải tính từ opening + cash sale + cash in - cash out - cash refund.
8. Không xóa dữ liệu ca vì đây là dữ liệu đối soát.

## Khi sửa Shift cần test

- Mở ca.
- Bán đơn tiền mặt.
- Bán đơn chuyển khoản.
- Thu tiền mặt.
- Chi tiền mặt.
- Refund tiền mặt.
- Void đơn.
- Đóng ca đúng lệch quỹ.
- Take over.
- Force close.
- In phiếu bàn giao/đóng ca.
