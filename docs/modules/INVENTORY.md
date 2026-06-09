# Module Inventory / Warehouse

## File chính

- `GaoApp.Domain/Entities/Warehouse.cs`
- `GaoApp.Domain/Entities/InventoryBalance.cs`
- `GaoApp.Domain/Entities/InventoryTransaction.cs`
- `GaoApp.Domain/Entities/StockDocument.cs`
- `GaoApp.Domain/Entities/StockDocumentLine.cs`
- `GaoApp.Domain/Entities/StockCountDocument.cs`
- `GaoApp.Domain/Entities/StockTransferDocument.cs`
- `GaoApp.Domain/Entities/InventoryAdjustmentDocument.cs`
- `GaoApp.Application/Services/Inventory/InventoryService.cs`
- `GaoApp.Application/Services/Inventory/InventoryMovementService.cs`
- `GaoApp.Application/Services/Inventory/StockDocumentService.cs`
- `GaoApp.Application/Services/Inventory/StockCountService.cs`
- `GaoApp.Application/Services/Inventory/StockTransferService.cs`
- `GaoApp.Application/Services/Inventory/InventoryAdjustmentDocumentService.cs`
- `GaoApp.Infrastructure/Repositories/Inventory/*.cs`

## Entity chính

### InventoryBalance

- `WarehouseId`
- `ProductVariantId`
- `OnHandQty`
- `ReservedQty`
- `InventoryValue`
- `AverageUnitCost`
- `LastInboundUnitCost`
- `LastInboundAtUtc`
- `LastValuationAtUtc`

Unique index: `{{StoreId, WarehouseId, ProductVariantId}}`.

### InventoryTransaction

- `WarehouseId`
- `ProductVariantId`
- `TransactionType`
- `ReferenceType`
- `ReferenceId`
- `ReferenceLineId`
- `QuantityChange`
- `BeforeQty`
- `AfterQty`
- `UnitCostSnapshot`
- `TotalCost`
- `BeforeInventoryValue`
- `AfterInventoryValue`
- `RunningAverageUnitCostAfter`
- `CostSourceType`
- `IsProvisionalCost`
- `OccurredAtUtc`

### StockDocument

- `DocumentNo`
- `Type`
- `Status`
- `WarehouseId`
- `SupplierId`
- `TotalAmount`
- `SubmittedAtUtc`
- `ApprovedAtUtc`
- `ConfirmedAtUtc`
- Revision request fields

### StockDocumentLine

- `ProductVariantId`
- `UnitId`
- `Factor`
- `Quantity`
- `BaseQuantity`
- `UnitCost`
- `LineTotal`

## Trạng thái StockDocument

- `Draft = 1`
- `PendingApproval = 2`
- `Confirmed = 3`
- `Rejected = 4`
- `Cancelled = 5`

## Transaction type quan trọng

- PurchaseReceipt
- SaleIssue
- SaleReturn
- SaleVoidIn
- CustomerReturnIn
- AdjustmentIncrease
- AdjustmentDecrease
- TransferIn
- TransferOut
- StockCountGain
- StockCountLoss
- Hold
- ReleaseHold
- Revaluation

## Quy tắc công thức nhập kho

Cần chốt rõ ở từng màn hình:

- `Quantity`: số lượng theo đơn vị nhập.
- `Factor`: hệ số quy đổi sang đơn vị gốc.
- `BaseQuantity = Quantity * Factor`.
- Nếu `UnitCost` là giá theo đơn vị nhập thì `LineTotal = Quantity * UnitCost`.
- Giá vốn theo đơn vị gốc nên tính riêng: `BaseUnitCost = LineTotal / BaseQuantity`.

Không được lẫn `UnitCost` giá thùng với `BaseQuantity` nếu không sẽ bị nhân sai.

## Khi sửa Inventory cần test

- Nhập 1 đơn vị gốc.
- Nhập 1 thùng/lốc/gói có factor.
- Sửa dòng nhập.
- Xóa dòng nhập.
- Gửi duyệt.
- Duyệt phiếu.
- Từ chối phiếu.
- Yêu cầu sửa lại.
- POS bán ra trừ tồn đúng base quantity.
- Void/Refund trả tồn đúng.
- Tồn âm được ghi log nếu có.
- Giá vốn snapshot trên OrderLine đúng.
