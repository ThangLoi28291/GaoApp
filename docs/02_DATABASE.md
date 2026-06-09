# Database / Entity Map

Tài liệu này được tạo từ `GaoApp.Infrastructure/Data/AppDbContext.cs` và thư mục `GaoApp.Domain/Entities`.

## DbContext

DbContext chính:

```txt
GaoApp.Infrastructure/Data/AppDbContext.cs
```

DbContext phụ cho audit:

```txt
GaoApp.Infrastructure/Data/AuditLogDbContext.cs
```

## Cơ chế nền

- `ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)` tự nạp toàn bộ `IEntityTypeConfiguration` trong Infrastructure.
- `ApplyGlobalFilters(builder)` áp global filter cho soft delete và tenant.
- `DisableCascadeDeleteToStore(builder)` tắt cascade delete về Store.
- `SaveChanges/SaveChangesAsync` tự gọi `ApplyAuditAndTenantRules()`.

## DbSet hiện có

| Entity | DbSet |
|---|---|
| `Store` | `Stores` |
| `UserInStore` | `UserInStores` |
| `Category` | `Categories` |
| `Brand` | `Brands` |
| `Supplier` | `Suppliers` |
| `Tax` | `Taxes` |
| `ProductAttribute` | `ProductAttributes` |
| `AttributeValue` | `AttributeValues` |
| `Unit` | `Units` |
| `Product` | `Products` |
| `ProductVariant` | `ProductVariants` |
| `ProductVariantAttributeValue` | `ProductVariantAttributeValues` |
| `ProductVariantBarcodeHistory` | `ProductVariantBarcodeHistories` |
| `MediaAsset` | `MediaAssets` |
| `ProductImage` | `ProductImages` |
| `Customer` | `Customers` |
| `Order` | `Orders` |
| `OrderLine` | `OrderLines` |
| `OrderPayment` | `OrderPayments` |
| `POSShift` | `POSShifts` |
| `POSShiftCashTransaction` | `POSShiftCashTransactions` |
| `POSAuditLog` | `POSAuditLogs` |
| `Warehouse` | `Warehouses` |
| `InventoryBalance` | `InventoryBalances` |
| `InventoryTransaction` | `InventoryTransactions` |
| `InventoryReservation` | `InventoryReservations` |
| `StockDocument` | `StockDocuments` |
| `StockDocumentLine` | `StockDocumentLines` |
| `ProductUnitConversion` | `ProductUnitConversions` |
| `ProductVariantUnitBarcode` | `ProductVariantUnitBarcodes` |
| `NegativeInventoryLog` | `NegativeInventoryLogs` |
| `StockCountDocument` | `StockCountDocuments` |
| `StockCountLine` | `StockCountLines` |
| `StockTransferDocument` | `StockTransferDocuments` |
| `StockTransferLine` | `StockTransferLines` |
| `Role` | `Roles` |
| `Permission` | `Permissions` |
| `RolePermission` | `RolePermissions` |
| `User` | `Users` |
| `AuditLog` | `AuditLogs` |
| `POSTerminal` | `POSTerminals` |
| `SalesReturn` | `SalesReturns` |
| `SalesReturnLine` | `SalesReturnLines` |
| `SalesReturnPayment` | `SalesReturnPayments` |
| `OrderInventoryIssue` | `OrderInventoryIssues` |
| `OrderInventoryIssueLine` | `OrderInventoryIssueLines` |
| `OrderInventoryIssueAction` | `OrderInventoryIssueActions` |
| `InventoryValuationEntry` | `InventoryValuationEntries` |
| `DocumentNumberSequence` | `DocumentNumberSequences` |
| `OrderInventoryIssueLineAllocation` | `OrderInventoryIssueLineAllocations` |
| `InventoryCostLayer` | `InventoryCostLayers` |
| `InventoryCostLayerAllocation` | `InventoryCostLayerAllocations` |
| `StoreBankAccount` | `StoreBankAccounts` |
| `PosPaymentQrRequest` | `PosPaymentQrRequests` |
| `DisplayPromotion` | `DisplayPromotions` |
| `InputInvoiceHead` | `InputInvoiceHeads` |
| `InputInvoiceDetail` | `InputInvoiceDetails` |
| `StockDocumentInputInvoiceMap` | `StockDocumentInputInvoiceMaps` |
| `StockDocumentLineInputInvoiceMap` | `StockDocumentLineInputInvoiceMaps` |
| `InvoiceHead` | `InvoiceHeads` |
| `InvoiceDetail` | `InvoiceDetails` |
| `AdminMenuItem` | `AdminMenuItems` |
| `InventoryAdjustmentDocument` | `InventoryAdjustmentDocuments` |
| `InventoryAdjustmentLine` | `InventoryAdjustmentLines` |
| `POSTerminalDevice` | `POSTerminalDevices` |
| `RewardSettings` | `RewardSettings` |
| `CustomerRewardLedger` | `CustomerRewardLedgers` |
| `CustomerRewardVoucher` | `CustomerRewardVouchers` |
| `OrderRewardVoucher` | `OrderRewardVouchers` |
| `POSShiftCashDenomination` | `POSShiftCashDenominations` |
| `POSShiftHandoverSlip` | `POSShiftHandoverSlips` |
| `POSShiftHandoverSlipDenomination` | `POSShiftHandoverSlipDenominations` |
| `POSShiftClosingSlip` | `POSShiftClosingSlips` |
| `POSShiftClosingSlipDenomination` | `POSShiftClosingSlipDenominations` |
| `ProductBarcodeVerificationRequest` | `ProductBarcodeVerificationRequests` |
| `Promotion` | `Promotions` |
| `PromotionItem` | `PromotionItems` |
| `PromotionComboRule` | `PromotionComboRules` |

## Entity hiện có

- `AdminMenuItem`
- `AttributeValue`
- `AuditLog`
- `Brand`
- `Customer`
- `CustomerRewardLedger`
- `CustomerRewardVoucher`
- `DisplayPromotion`
- `DocumentNumberSequence`
- `InputInvoiceDetail`
- `InputInvoiceHead`
- `InventoryAdjustmentDocument`
- `InventoryAdjustmentLine`
- `InventoryBalance`
- `InventoryCostLayer`
- `InventoryCostLayerAllocation`
- `InventoryReservation`
- `InventoryTransaction`
- `InventoryValuationEntry`
- `InvoiceDetail`
- `InvoiceHead`
- `MediaAsset`
- `NegativeInventoryLog`
- `Order`
- `OrderInventoryIssue`
- `OrderInventoryIssueAction`
- `OrderInventoryIssueLine`
- `OrderInventoryIssueLineAllocation`
- `OrderLine`
- `OrderNumberSequence`
- `OrderPayment`
- `OrderRewardVoucher`
- `POSAuditLog`
- `POSShift`
- `POSShiftCashDenomination`
- `POSShiftCashTransaction`
- `POSShiftClosingSlip`
- `POSShiftClosingSlipDenomination`
- `POSShiftHandoverSlip`
- `POSShiftHandoverSlipDenomination`
- `POSTerminal`
- `POSTerminalDevice`
- `Permission`
- `PosPaymentQrRequest`
- `Product`
- `ProductAttribute`
- `ProductBarcodeVerificationRequest`
- `ProductImage`
- `ProductUnitConversion`
- `ProductVariant`
- `ProductVariantAttributeValue`
- `ProductVariantBarcodeHistory`
- `ProductVariantUnitBarcode`
- `Promotion`
- `PromotionComboRule`
- `PromotionItem`
- `RewardSettings`
- `Role`
- `RolePermission`
- `SalesReturn`
- `SalesReturnLine`
- `SalesReturnPayment`
- `StockCountDocument`
- `StockCountLine`
- `StockDocument`
- `StockDocumentInputInvoiceMap`
- `StockDocumentLine`
- `StockDocumentLineInputInvoiceMap`
- `StockTransferDocument`
- `StockTransferLine`
- `Store`
- `StoreBankAccount`
- `Supplier`
- `Tax`
- `Unit`
- `User`
- `UserInStore`
- `Warehouse`

## Enum hiện có

- `AuditActionType`
- `AuditModuleType`
- `BankQrConfirmMode`
- `BankQrRenderMode`
- `BarcodeHistoryActionType`
- `BarcodeLookupSourceType`
- `BarcodeType`
- `BarcodeVerificationRequestStatus`
- `BarcodeVerificationRequestType`
- `CustomerRewardLedgerType`
- `CustomerRewardVoucherStatus`
- `DocumentNumberSequenceType`
- `InputInvoiceMatchStatus`
- `InventoryAdjustmentDocumentStatus`
- `InventoryAdjustmentReasonType`
- `InventoryCostSourceType`
- `InventoryIssueActionType`
- `InventoryIssueDetectedSignalType`
- `InventoryIssueReasonType`
- `InventoryIssueReferenceType`
- `InventoryIssueSeverity`
- `InventoryReferenceType`
- `InventoryReservationStatus`
- `InventoryResolutionStatus`
- `InventoryTransactionType`
- `InventoryValuationEntryType`
- `InvoiceDetailSourceType`
- `OrderInventoryStatus`
- `OrderStatus`
- `POSShiftCashDenominationEntryType`
- `POSShiftCashTransactionType`
- `POSShiftClosingSlipStatus`
- `POSShiftHandoverSlipStatus`
- `POSShiftStatus`
- `POSTerminalStatus`
- `PaymentMethod`
- `PaymentStatus`
- `PosPaymentQrStatus`
- `PromotionDiscountType`
- `PromotionType`
- `SalesReturnLineAction`
- `SalesReturnStatus`
- `SalesReturnType`
- `StockCountDocumentStatus`
- `StockDocumentStatus`
- `StockDocumentType`
- `StockTransferDocumentStatus`

## Nhóm bảng chính

### Catalog

- Store
- Category
- Brand
- Supplier
- Tax
- Unit

### Product / Barcode / Media

- Product
- ProductVariant
- ProductVariantAttributeValue
- ProductUnitConversion
- ProductVariantUnitBarcode
- ProductVariantBarcodeHistory
- ProductBarcodeVerificationRequest
- ProductImage
- MediaAsset

### POS / Order

- Order
- OrderLine
- OrderPayment
- SalesReturn
- SalesReturnLine
- SalesReturnPayment

### Promotion / Reward

- Promotion
- PromotionItem
- PromotionComboRule
- RewardSettings
- CustomerRewardLedger
- CustomerRewardVoucher
- OrderRewardVoucher

### Shift

- POSShift
- POSShiftCashTransaction
- POSShiftCashDenomination
- POSShiftHandoverSlip
- POSShiftHandoverSlipDenomination
- POSShiftClosingSlip
- POSShiftClosingSlipDenomination
- POSTerminal
- POSTerminalDevice

### Inventory

- Warehouse
- InventoryBalance
- InventoryTransaction
- InventoryReservation
- InventoryValuationEntry
- InventoryCostLayer
- InventoryCostLayerAllocation
- StockDocument
- StockDocumentLine
- StockCountDocument
- StockCountLine
- StockTransferDocument
- StockTransferLine
- InventoryAdjustmentDocument
- InventoryAdjustmentLine
- NegativeInventoryLog
- OrderInventoryIssue
- OrderInventoryIssueLine
- OrderInventoryIssueAction
- OrderInventoryIssueLineAllocation

### Invoice

- InputInvoiceHead
- InputInvoiceDetail
- InvoiceHead
- InvoiceDetail
- StockDocumentInputInvoiceMap
- StockDocumentLineInputInvoiceMap

### Security / Audit

- User
- UserInStore
- Role
- Permission
- RolePermission
- AuditLog
- POSAuditLog
- AdminMenuItem

## Các entity cần nhớ khi sửa POS

### Order

Các field tiền quan trọng:

- `Subtotal`
- `DiscountTotal`
- `OrderDiscount`
- `GrandTotal`
- `PaidTotal`
- `BalanceDue`
- `ChangeDue`
- `VoucherDiscountTotal`
- `PromotionDiscountTotal`
- `ComboDiscountTotal`

Trạng thái:

- Draft
- OnHold
- Completed
- Cancelled
- Voided
- Refunded

### OrderLine

Các field đơn vị / quy đổi:

- `Quantity`
- `SellingUnitId`
- `ProductUnitConversionId`
- `Multiplier`
- `BaseQuantity`
- `UnitPrice`
- `LineDiscount`
- `LineTotal`

Các field promotion:

- `OriginalUnitPrice`
- `PromotionDiscount`
- `PromotionId`
- `PromotionName`
- `ComboPromotionId`
- `ComboAllocatedDiscount`
- `PromotionType`
- `PromotionBuyQuantity`
- `PromotionGiftQuantity`
- `IsPromotionGift`
- `GiftPromotionId`
- `GiftSourceLineId`
- `GiftPromotionName`
- `GiftPromotionNote`

## Các cấu hình index quan trọng đã thấy

- `Order`: unique `{StoreId, OrderNumber}` khi OrderNumber khác null.
- `Order`: index `{StoreId, POSShiftId, Status}`.
- `Order`: index `{StoreId, CompletedAtUtc}`.
- `InventoryBalance`: unique `{StoreId, WarehouseId, ProductVariantId}`.
- `ProductUnitConversion`: unique `{StoreId, ProductVariantId, UnitId}`.
- `ProductVariantUnitBarcode`: unique active `{StoreId, Barcode}` có filter `IsDeleted = 0 AND IsActive = 1`.
- `StockDocument`: unique `{StoreId, DocumentNo}`.
- `Promotion`: index theo StoreId, Type, Active, Start/End, IsDeleted.

## Cảnh báo khi sửa database

- Không tự ý xóa migration cũ nếu database đã chạy thật.
- Nếu thêm field cho POS/Promotion/Reward phải kiểm tra migration và default value.
- Nếu thêm quan hệ từ OrderLine phải tránh cascade delete nguy hiểm.
- Nếu query system không có tenant, phải tự kiểm tra StoreId.
