# GaoApp Source Map

## 1. Purpose and verification baseline

Tài liệu này là bản đồ điều hướng source cho các thay đổi dài hạn của GaoApp. Mục tiêu là giúp Coordinator, Implementation Agent và Independent Reviewer đi từ nghiệp vụ đến đúng project, entity, service, endpoint, view, cấu hình dữ liệu và test mà không phải đọc tuần tự toàn repository.

Tài liệu mô tả **source đang tồn tại**, không mặc định rằng thiết kế hiện tại đã đáp ứng R2. Các yêu cầu đích đã khóa nằm trong `DECISION-LOG.md`; các khoảng trống và rủi ro đã biết nằm trong `BACKLOG-FINDINGS.md`.

| Thuộc tính | Giá trị |
|---|---|
| Source root | `D:\Datacode` |
| Last verified branch | `fix/r2-0-c2-durable-inventory-posting` |
| Last verified commit | `0a5b2cf5734fd69dfa1e709de3984222c1ee4158` |
| Last verified date | 2026-08-03 |
| Runtime/build/test during verification | Không chạy; đây là task tài liệu Level A |

> Cảnh báo cập nhật: bản đồ này phải được kiểm tra lại sau mọi thay đổi schema, workflow, endpoint, permission hoặc Confirm posting. Không dùng commit ở trên như bằng chứng cho source mới hơn.

Baseline hiện tại bao gồm governance R2.0-A2 (`3086b86daf7c8ae5bfc2868c6badb3da3542f339`, merge `ae7bf491f2e75fa1b8009a600d064eac2b4883b9`), C1 tenant-safe XML detail mapping (`f0ebd2b4a864d18605806fb85b833dc547fa9f76`, merge `d9b4b06c2a8f35bc0021c8f41a88361307e1d634`) và ba chặng C2: C2A atomic posting core `d1b06dce5343808493ebb7d1b599e01a06ae5113`, C2B1 non-POS adoption `3c99f19131a8c1af0bbfeeba94eb286c0081686c`, C2B2 POS/Return/LegalEntity/Reservation/Revaluation adoption `0a5b2cf5734fd69dfa1e709de3984222c1ee4158`. Tại thời điểm cập nhật tài liệu, C2B2 đã có Independent Review PASS và GitHub required checks 2/2 Success trên head này; PR #8 vẫn open và unmerged. Đây là evidence của implementation C2, không phải verdict cho task documentation C2C.

## 2. Solution and dependency direction

`GaoApp.sln` gồm sáu project:

| Project | Vai trò hiện tại | Project references chính |
|---|---|---|
| `GaoApp.Domain` | Entity, enum và business state nền | Không tham chiếu project GaoApp khác |
| `GaoApp.Application` | DTO, interface, policy và application service | `GaoApp.Domain` |
| `GaoApp.Infrastructure` | EF Core, repository, migration, tenant/audit persistence | `GaoApp.Application`, `GaoApp.Domain` |
| `GaoApp.Web` | MVC/Razor, API controller, JavaScript và composition root | `GaoApp.Application`, `GaoApp.Infrastructure` |
| `GaoApp.Migrator` | .NET 8 console/Generic Host dùng cho migration, pre-flight và seed/bootstrap có kiểm soát | `GaoApp.Application`, `GaoApp.Infrastructure` |
| `GaoApp.Tests` | xUnit contract/unit/in-memory tests | `GaoApp.Domain`, `GaoApp.Application`, `GaoApp.Infrastructure`, `GaoApp.Web` |

Dependency registration liên quan:

- `GaoApp.Application/DependencyInjection.cs`: đăng ký `IStockDocumentService`, `IPurchaseOrderService`, `IInventoryMovementService`, `IInputInvoiceXmlService`.
- `GaoApp.Infrastructure/DependencyInjection.cs`: đăng ký repository cho purchase order, stock document, inventory balance/transaction/valuation/cost layer và các dependency persistence khác.
- `GaoApp.Infrastructure/Data/AppDbContext.cs`: áp dụng configurations từ assembly, global soft-delete/tenant filters và tenant ownership guard khi ghi.

Migration startup theo source:

- Entry point là `GaoApp.Migrator/Program.cs`; project không khởi động `GaoApp.Web` và chỉ gọi `AddInfrastructure(configuration)` cho graph persistence cần thiết.
- Configuration được nạp từ `appsettings.json`, file theo environment và environment variables tại `AppContext.BaseDirectory`; Serilog và các option seed/bootstrap được đăng ký tại composition root này.
- `MigrationRunner.RunAsync` gọi `MigrationExecutionPipeline.RunAsync`. Pipeline validate configuration, chạy compatibility/schema/security-metadata pre-flight, rồi `EfCoreDatabaseMigrationExecutor.MigrateAsync` gọi `AppDbContext.Database.MigrateAsync`.
- Sau migration, pipeline có thể chạy production bootstrap, demo seed và mandatory security seed theo option; provisioning work được bao trong transaction do pipeline điều phối.
- Các file entry/configuration chính: `GaoApp.Migrator/GaoApp.Migrator.csproj`, `Program.cs`, `MigrationRunner.cs`, `MigratorWebHostEnvironment.cs`, `appsettings.json`, `appsettings.Production.json`; pipeline/executor nằm trong `GaoApp.Infrastructure/Data/Migrations/`.
- Luồng trên chỉ được xác minh bằng đọc source trong task tài liệu; Migrator không được build hoặc chạy.

## 3. Purchase Order

### 3.1 Current source

Header:

- Entity: `GaoApp.Domain/Entities/PurchaseOrder.cs`, `PurchaseOrder`.
- Store scoped qua `BaseStoreEntity`.
- `SupplierId`, `ExpectedWarehouseId` và `LegalEntityId` đang là bắt buộc.
- Có `SourcePurchaseRequestId`, `SourceConversionKey`, audit actor/timestamp, workflow note và `RowVersion`.
- `Receipts` là collection `StockDocument`, nhưng mỗi receipt hiện vẫn có một `PurchaseOrderId` ở header.

Lines:

- Entity: `GaoApp.Domain/Entities/PurchaseOrderLine.cs`, `PurchaseOrderLine`.
- Hỗ trợ `PurchaseItemKind.Catalog` và `PurchaseItemKind.FreeText`.
- `ProductVariantId`, `UnitId`, `ProductUnitConversionId` có thể null cho free-text.
- Lưu snapshot tên hàng, SKU, đơn vị, thuế và `ConversionFactor`.
- Theo dõi `OrderedQuantity`, `ReceivedQuantity`, `ShortClosedQuantity`; `PendingQuantity` được tính từ ba giá trị này và không âm.
- Có trạng thái nhận riêng ở dòng và metadata đóng thiếu.

Workflow/service:

- `GaoApp.Application/Services/Purchases/PurchaseOrderService.cs`.
- `SaveAsync`: tạo/cập nhật order; yêu cầu supplier, expected warehouse, legal entity và lý do nếu không đi từ purchase request.
- Chỉ `Draft` và `ReturnedForRevision` được sửa dữ liệu thương mại.
- `SubmitAsync`, `ApproveAsync`, `ReturnForRevisionAsync`, `RejectAsync`, `MarkSentAsync`, `CancelAsync` điều khiển state.
- Receipt không được tạo trong `PurchaseOrderService`; action `PurchaseOrdersController.CreateReceipt` gọi trực tiếp `StockDocumentService.CreateReceiptFromPurchaseOrderAsync`.
- `GaoApp.Application/Services/Purchases/PurchaseOrderWorkflowPolicy.cs` chứa guard edit/review/cancel.

Web:

- Controller: `GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs`.
- Views: `GaoApp.Web/Areas/Admin/Views/PurchaseOrders/`, đặc biệt `Details.cshtml` cho hành động workflow/khởi tạo receipt.
- Permission chính: `Purchase.Order.View`, `ViewCost`, `Create`, `Update`, `Approve`, `Close`, `Cancel`, `Print`.

Persistence:

- `GaoApp.Infrastructure/Data/Configurations/PurchaseOrderConfiguration.cs`.
- Unique order number theo store; unique source conversion key có filter; rowversion ở header và line.
- Repository: `GaoApp.Infrastructure/Repositories/Purchases/PurchaseOrderRepository.cs`.

### 3.2 Current behavior versus R2 target

Đã có:

- Order nhà cung cấp, nhiều receipt nối về một order qua collection.
- Free-text line và quy trình resolve sang catalog.
- Theo dõi received/short-closed/pending theo dòng.
- Short-close lúc Confirm receipt và lưu reason/actor/time.

Chưa đáp ứng R2:

- Receipt hiện gắn một `PurchaseOrderId` ở header, nên chưa gom dòng từ nhiều PO tương thích cùng Store/LegalEntity/Supplier vào một receipt.
- Source hiện chặn over-receipt; R2 yêu cầu cho giao dư với quản lý chấp nhận.
- Chưa có tổng riêng “đang chờ duyệt” so với “đã Confirm” để tính official remaining.
- Chưa có thao tác đóng/mở lại phần còn thiếu độc lập ở dòng/toàn order theo model R2.
- Receipt tạo từ PO hiện sao chép cùng conversion/unit; chưa hỗ trợ nhận bằng đơn vị khác dù có factor hợp lệ.

## 4. Purchase Receipt

### 4.1 Document model

Phiếu nhập mua không có entity kho thứ hai. Chứng từ đang chạy là:

- `GaoApp.Domain/Entities/StockDocument.cs`, với `StockDocumentType.Receipt`.
- `GaoApp.Domain/Entities/StockDocumentLine.cs`.
- `PurchaseReceiptSource`: `LegacyDirect`, `Direct`, `PurchaseOrder`.

Header:

- `WarehouseId` bắt buộc và đại diện đúng một kho đích.
- `SupplierId` nullable.
- `PurchaseOrderId` nullable; nếu có thì chỉ một order/header.
- Không lưu `LegalEntityId` trực tiếp; create/confirm validate legal entity qua warehouse được chọn và PO khi có. Header có status, rowversion và submit/approve/confirm actor/time.
- Có VAT, subtotal, freight, payment/payee và revision request fields.

Line:

- `ProductVariantId` bắt buộc.
- `PurchaseOrderLineId` nullable, tạo liên kết PO-receipt ở cấp dòng.
- Lưu `UnitId`, `ProductUnitConversionId`, `Factor`, `Quantity`, `BaseQuantity`.
- Lưu giá trước/sau VAT, VAT amount, line total và freight allocation.
- Lưu snapshot tên sản phẩm/SKU/barcode/đơn vị/thuế.
- Có shortage disposition/reason.
- Entity kế thừa `BaseEntity` nhưng không implement `IAuditTrackedEntity`.

### 4.2 Creation and editing

Service: `GaoApp.Application/Services/Inventory/StockDocumentService.cs`.

- `CreateReceiptAsync` tạo receipt trực tiếp; request yêu cầu legal entity để kiểm tra warehouse, cùng warehouse và direct-receipt reason. Supplier có thể null.
- `CreateReceiptFromPurchaseOrderAsync` chỉ nhận PO ở `Approved`, `SentToSupplier` hoặc `PartiallyReceived`; kế thừa supplier và expected warehouse, giữ legal-entity coherence qua order/warehouse validation, rồi tạo các line được chọn từ PO.
- `AddLineAsync`, `AddLineByBarcodeAsync`, `UpdateLineAsync`, `DeleteLineAsync` xử lý direct receipt lines.
- Direct line bắt buộc variant thật. Merge hiện tại dựa trên `ProductVariantId`, `UnitId`, `UnitCost`, `TaxId`; không dùng tên làm identity.
- `EnsureEditable` hiện cho `Draft`, `PendingApproval` và `Rejected`, vì vậy Pending vẫn sửa được trong source hiện tại.
- `SubmitForApprovalAsync` chuyển `Draft`/`Rejected` sang `PendingApproval`.
- Revision request có thể được manager resolve theo hướng trả về `Rejected` hoặc bỏ qua.
- `EnsureEditable` từ chối `Confirmed`; UI/workbench cũng chuyển sang read-only.

Web:

- MVC page controller: `GaoApp.Web/Areas/Admin/Controllers/StockDocumentManagementController.cs`.
- API controller: `GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs`.
- Main view: `GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml`.
- Commercial partial: `GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml`.
- Client scripts: `GaoApp.Web/wwwroot/Admin/js/stock-document-management.js` và `purchase-receipt-approval.js`.
- Lưu ý đường dẫn thực của purchase approval JavaScript nằm ở `GaoApp.Web/wwwroot/Admin/js`, không nằm dưới `Areas/Admin/wwwroot`.

### 4.3 Commercial approval

DTO:

- `GaoApp.Application/DTOs/Inventory/PurchaseReceiptApprovalDtos.cs`.
- `ApprovePurchaseReceiptCommercialRequest` dùng DataAnnotations, không có FluentValidation validator riêng.
- Payload nhận rowversion, note, supplier/payment, danh sách line price/tax và freight allocation.
- Payload cố ý không nhận product, unit hoặc quantity.

Rules:

- `GaoApp.Application/Services/Purchases/PurchasePricingPolicy.cs`.
- Giá manager nhập là giá trước VAT.
- `UnitCost`/`LineTotal` hiện phản ánh merchandise sau VAT.
- Base unit cost hiện lấy `(LineTotal sau VAT + FreightAllocation) / BaseQuantity`.
- Freight auto allocation theo giá trị dòng sau VAT, cho sửa manual nhưng tổng allocation phải khớp.
- Chưa có discount line.
- Last purchase price được hiển thị và tính variance ở client; chưa có server-side warning contract bắt buộc.

Khoảng trống đích:

- R2 cần supplier có thể null ở Draft nhưng bắt buộc trước Confirm.
- R2 giữ `WarehouseId` bắt buộc: draft phải tự chọn default warehouse hoặc block rõ nếu không có; không đổi WarehouseId thành nullable.
- R2 yêu cầu Pending immutable đối với employee.
- R2 yêu cầu tùy chọn có/không đưa VAT vào giá vốn và mặc định freight lưu riêng, chỉ capitalize khi bật.

## 5. Confirm call chain

Đường đi hiện tại của thao tác “Duyệt và ghi sổ”:

1. UI: `GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml` render `_CommercialApprovalWorkbench.cshtml` và nạp `purchase-receipt-approval.js`.
2. Client: `openCommercialApprovalConfirmation()` kiểm tra form; `submitCommercialApproval()` POST tới `/admin/api/stock-documents/{id}/approve-commercial`.
3. API: `GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs`, `ApproveCommercial`.
4. Authorization: `HasWorkflowPermissionAsync` yêu cầu `Purchase.Receipt.Approve` hoặc permission inventory approve tương ứng.
5. Request validation: `ApprovePurchaseReceiptCommercialRequest` DataAnnotations; controller/model binding, rồi business validation trong service.
6. Service entry: `GaoApp.Application/Services/Inventory/StockDocumentService.cs`, `ApproveCommercialAsync`.
7. Commercial persistence: service validate rowversion/status/line set/supplier/prices/tax/freight, cập nhật line/header, rồi gọi private `ApproveTrackedAsync`.
8. Transaction: `IStockDocumentRepository.BeginTransactionAsync`; caller sở hữu commit/rollback và mọi posting sau đây nằm trong cùng transaction của repository/DbContext.
9. Pre-lock: `InventoryMovementService.PreLockBalancesAsync` materialize toàn bộ khóa Store/warehouse/variant của các active line và khóa theo thứ tự canonical trước movement đầu tiên.
10. Cost: `PurchasePricingPolicy.CalculateBaseUnitCost(line.LineTotal, line.FreightAllocation, line.BaseQuantity)`.
11. Movement request: `GaoApp.Application/Services/Inventory/InventoryMovementFactory.cs`, `CreatePurchaseReceipt`, tạo `InventoryTransactionType.PurchaseReceipt`, reference type `StockDocument`, giữ document/line identity và `SkipIfExists=true`.
12. Inventory posting: `GaoApp.Application/Services/Inventory/InventoryMovementService.cs`, `CreateAsync`, tham gia transaction đang active qua `InventoryPostingTransactionCoordinator`.
13. Durable identity: `InventoryIdempotencyKeyFactory` tạo key canonical SHA-256; repository kiểm tra key hiện tại và legacy identity, còn database bảo vệ uniqueness bằng `StoreId + IdempotencyKey` cho active rows.
14. Balance: `InventoryBalanceRepository.LockAndGetOrCreateAsync` dùng SQL Server transaction-scoped locking.
15. Ledgers/layers: tạo `InventoryTransaction`, inbound `InventoryValuationEntry`, `InventoryCostLayer`, liên kết entry/layer, xử lý provisional revaluation rồi cập nhật `InventoryBalance`.
16. Receipt effects: cập nhật `ProductVariant.HasInputInvoice` nếu mapping có mặt tại thời điểm Confirm; `ApplyApprovedReceiptToPurchaseOrder`; tạo payable nếu cần.
17. Final state: set `StockDocumentStatus.Confirmed`, approved/confirmed actor/time; `SaveChangesAsync`; commit transaction.
18. Failure: catch rollback; conflict SQL Server được coordinator chuyển thành `ConcurrencyException`, và API có thể map concurrency exception thành HTTP 409.

Current safeguards:

- Confirm retry sau commit trả về nếu document đã `Confirmed`.
- Request mang `RowVersion`.
- Posting, durable movement identity, balance lock và các effect của receipt nằm trong transaction do caller sở hữu.
- `SkipIfExists` dùng durable key và kiểm tra payload của movement đã tồn tại; database filtered unique index là hàng rào cuối cho concurrent insert.
- Toàn bộ balance key được pre-lock trước movement đầu tiên; thứ tự lock không thay đổi thứ tự business line/posting.

## 6. Inventory and costing

Entities:

- `InventoryTransaction`: quantity ledger, warehouse/variant/reference, before/after quantities và cost snapshots.
- `InventoryBalance`: on-hand, reserved, inventory value, average/last inbound cost; unique theo store + warehouse + variant.
- `InventoryValuationEntry`: value ledger, inbound/outbound/revaluation/provisional tracking.
- `InventoryCostLayer`: FIFO inbound layer và remaining quantity.
- `InventoryCostLayerAllocation`: allocation giữa outbound/provisional và layers.
- `NegativeInventoryLog`: log nghiệp vụ âm kho.

Services/repositories:

- `InventoryMovementService.CreateAsync`: một entry point chính cho quantity/value posting.
- `InventoryMovementFactory.CreatePurchaseReceipt`: adapter receipt sang movement request.
- Repositories trong `GaoApp.Infrastructure/Repositories/Inventory/`.
- Configurations trong `GaoApp.Infrastructure/Data/Configurations/Inventory*Configuration.cs`.

### 6.1 Durable inventory posting foundation — current implemented behavior

Durable movement identity:

- `GaoApp.Domain/Entities/InventoryTransaction.cs` có `IdempotencyKey` kiểu `byte[]?`. `GaoApp.Application/Services/Inventory/InventoryIdempotencyKeyFactory.cs` tạo key SHA-256 32 byte từ canonical identity có version, `StoreId`, `WarehouseId`, `ProductVariantId`, `TransactionType`, `ReferenceType`, `ReferenceId`, nullable `ReferenceLineId` và nullable `ReferenceSubKey`. String được normalize Unicode NFC nhưng vẫn phân biệt hoa/thường; null và empty subkey không bị đánh đồng.
- `GaoApp.Infrastructure/Data/Configurations/InventoryTransactionConfiguration.cs` map key thành nullable `varbinary(32)`. Nullable là chủ ý để giữ nguyên historical rows và các transaction được tạo theo path non-idempotent; không có default và migration không phát minh hoặc backfill key cho dữ liệu lịch sử.
- Filtered unique index `UX_InventoryTransactions_StoreId_IdempotencyKey_Active` bảo vệ `StoreId + IdempotencyKey`, chỉ cho row có key, active và non-deleted: `[IdempotencyKey] IS NOT NULL AND [IsDeleted] = 0`.
- Migration hiện tại là `GaoApp.Infrastructure/Migrations/20260801110856_AddInventoryPostingIdempotency.cs`, cùng `GaoApp.Infrastructure/Migrations/20260801110856_AddInventoryPostingIdempotency.Designer.cs` và `GaoApp.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`. Migration chỉ add nullable column/index; `Down` chỉ drop index/column.
- `InventoryMovementService.CreateAsync` dùng key khi request có `SkipIfExists`; `GaoApp.Infrastructure/Repositories/Inventory/InventoryTransactionRepository.cs` tải row keyed hoặc legacy identity, rồi service so canonical identity/quantity/unit cost và trả skipped cho retry tương thích. Database unique index vẫn là durable protection khi hai transaction cạnh tranh.

Transaction và balance locking:

- `GaoApp.Application/Interfaces/Common/IInventoryPostingTransactionCoordinator.cs` và `GaoApp.Infrastructure/Data/InventoryPostingTransactionCoordinator.cs` cung cấp `HasActiveTransaction` và `ExecuteAsync`. `CreateAsync` tham gia transaction caller nếu đã có; nếu là single-movement standalone call thì coordinator có thể mở, commit/rollback transaction của chính nó. SQL lock timeout/deadlock và duplicate-key conflict của các index đã biết được chuyển thành `ConcurrencyException`; không có global automatic retry/replay.
- `GaoApp.Application/DTOs/Inventory/InventoryPostingLockKey.cs` là đúng bộ ba `StoreId`, `WarehouseId`, `ProductVariantId`; public pre-lock contract nằm tại `GaoApp.Application/Interfaces/Services/Inventory/IInventoryMovementService.cs`.
- `InventoryMovementService.PreLockBalancesAsync` kiểm tra collection/null/ID, materialize toàn bộ input, dedupe rồi sort `StoreId → WarehouseId → ProductVariantId`. Empty batch là no-op. Batch không rỗng bắt buộc caller đã mở transaction; pre-lock không tự mở transaction, không commit và không rollback.
- Trước balance lock đầu tiên, pre-lock tải toàn bộ warehouse distinct và xác minh warehouse tồn tại, đúng Store của từng key. Vì vậy invalid Store/warehouse bị từ chối trước mutation/partial balance locking.
- `GaoApp.Infrastructure/Repositories/Inventory/InventoryBalanceRepository.cs`, `LockAndGetOrCreateAsync`, chỉ chạy trên SQL Server và trong active transaction. Query dùng `WITH (UPDLOCK,HOLDLOCK,ROWLOCK)` trên exact Store/warehouse/variant key, sau đó đọc cả soft-deleted identity, từ chối identity bị soft-delete hoặc tạo/save balance zero mới.
- Canonical lock order chỉ điều phối concurrency. Caller vẫn post theo business order sẵn có, ví dụ transfer out trước transfer in, POS theo order line, và Sales Return theo persisted return line/allocation order.

### 6.2 Current caller adoption map

| Operation | Current pre-lock/posting behavior | Source |
|---|---|---|
| Purchase receipt Confirm | Materialize active receipt-line keys sau khi caller mở transaction; pre-lock một batch trước movement đầu tiên; giữ line order và receipt document/line identity. | `GaoApp.Application/Services/Inventory/StockDocumentService.cs`, `ApproveTrackedAsync` |
| Stock count Confirm | Materialize các line có difference khác 0, pre-lock trong caller transaction, rồi post gain/loss theo `LineNo`. | `GaoApp.Application/Services/Inventory/StockCountService.cs`, `ConfirmAsync` |
| Stock transfer Confirm | Union cả source và destination key cho mọi line; pre-lock một lần rồi giữ business order transfer out trước transfer in. | `GaoApp.Application/Services/Inventory/StockTransferService.cs`, `ConfirmAsync` |
| Adjustment approval | Build tất cả movement request trước khi mở transaction, pre-lock toàn batch rồi post theo document line order. | `GaoApp.Application/Services/Inventory/InventoryAdjustmentDocumentService.cs`, `ApproveAsync` |
| POS finalize | Legacy path materialize mọi active line và pre-lock một batch; multi-LegalEntity path pre-lock allocation keys trước movement. Cả hai nằm trong transaction ngoài cùng của `POSService.FinalizeAsync`. | `GaoApp.Application/Services/Orders/POSService.cs`, `ApplyInventoryForFinalizeAsync`; `GaoApp.Application/Services/Orders/OrderLegalEntityFinalizeService.cs`, `ApplyIfEnabledAsync` |
| POS void | Legacy path khám phá mọi outbound valuation fragment trước union pre-lock; multi-LegalEntity reversal pre-lock mọi source warehouse/variant trước movement. Subkey/source valuation identity và fragment order được giữ nguyên. | `GaoApp.Application/Services/Orders/POSService.cs`, `ApplyInventoryForVoidAsync`; `GaoApp.Application/Services/Orders/OrderLegalEntityReversalService.cs`, `ReverseVoidIfAllocatedAsync` |
| Sales Return | Parent `SalesReturnService.CreateAsync` lập một operation-wide union từ LegalEntity batch và legacy Restock plans, pre-lock đúng một lần rồi apply theo persisted return-line order. | `GaoApp.Application/Services/Orders/SalesReturnService.cs`; `GaoApp.Application/Services/Orders/OrderLegalEntityReversalService.cs` |
| Reservation reserve/release/consume | Mỗi operation materialize exact active/new reservation keys và pre-lock trước khi đổi `ReservedQty`. Non-empty operation phải được caller bao bằng active transaction. | `GaoApp.Application/Services/Inventory/InventoryReservationService.cs` |
| Reservation rebuild | Materialize union old reservation keys + new plan keys, pre-lock một lần, rồi release old và reserve new trong cùng held batch. | `GaoApp.Application/Services/Inventory/InventoryReservationService.cs`, `RebuildForOrderAsync` |
| Revaluation | Pre-lock exact inbound-layer Store/warehouse/variant trước direct balance access và trước ghi revaluation transaction/valuation. | `GaoApp.Application/Services/Inventory/InventoryRevaluationService.cs`, `ResolveByInboundLayerAsync` |

Sales Return có contract riêng cần giữ:

- Mỗi persisted return line được classify độc lập thành legacy hoặc LegalEntity từ allocation/valuation evidence; document có thể mixed.
- `NoRestock` không đóng góp balance key và không tạo inventory movement, nhưng vẫn có return/reversal ledger phù hợp.
- Evidence LegalEntity partial hoặc không nhất quán fail closed trước pre-lock/movement.
- Parent tạo một union LegalEntity + legacy cho toàn operation. Prepared LegalEntity line apply không pre-lock lồng; parent path không gọi direct per-line helper `ReverseSalesReturnLineIfAllocatedAsync`.
- Canonical lock order không sắp xếp lại business line. Persisted line order, allocation order, source valuation identity và các legacy/LegalEntity subkey hiện hữu được bảo toàn.

Đơn vị giá vốn:

- Receipt line lưu quantity theo đơn vị nhận và `BaseQuantity`.
- Movement dùng `BaseQuantity`, nên balance/layer đi theo đơn vị gốc.
- `UnitCost` của layer là base-unit cost precision 18,6; monetary totals thường 18,2 hoặc ledger value 18,4.

## 7. Unit conversion and product identity

Core entity/config:

- `GaoApp.Domain/Entities/ProductUnitConversion.cs`.
- `GaoApp.Infrastructure/Data/Configurations/ProductUnitConversionConfiguration.cs`.
- Unique conversion theo store + variant + unit.
- Factor precision 18,4; có `IsBaseUnit`, `IsDefaultForSale`, `IsActive`.
- Chưa có filtered unique index bảo đảm đúng một base/default conversion mỗi variant.

Resolver:

- `GaoApp.Application/Services/Inventory/InventoryUnitResolver.cs`.
- Resolve conversion đang active cho variant/unit; nếu không có thì dùng base-unit fallback theo rule hiện hữu.
- Service chụp `Factor` vào order/receipt line và tính `BaseQuantity`.

Identity:

- Technical identity và merge key hiện dùng `ProductVariantId`.
- Mã hiển thị là `ProductVariant.Sku`, được snapshot ở line.
- Tên chỉ là snapshot/hỗ trợ kiểm tra, không phải khóa.
- Không thay thế hoặc viết lại `InventoryUnitResolver` nếu chưa có bằng chứng source/rule sai; R2 phải mở rộng quanh cơ chế này.

## 8. Store, LegalEntity and Warehouse boundary

### 8.1 Current source model and behavior

- `Store` là tenant/operational boundary. Các entity nghiệp vụ kế thừa `BaseStoreEntity` được lọc theo current store qua `AppDbContext.ApplyGlobalFilters`; `ValidateTenantOwnership`/`ValidateTenantOwnershipAsync` bảo vệ ownership khi ghi.
- `LegalEntity` là legal-owner boundary bên trong một Store. `GaoApp.Domain/Entities/LegalEntity.cs` kế thừa `BaseStoreEntity`; một Store có thể có nhiều legal entity đang active.
- `Warehouse` thuộc đúng một `LegalEntity` qua `Warehouse.LegalEntityId`. `WarehouseConfiguration` dùng composite foreign key `(StoreId, LegalEntityId)` tới legal entity, nên warehouse và owner legal entity phải cùng Store.
- `StockDocument` không có `LegalEntityId`; receipt legal entity hiện được suy ra duy nhất từ `StockDocument.Warehouse.LegalEntityId`. `WarehouseId` vẫn bắt buộc.
- Direct-receipt form options được dựng bởi `StockReceiptLegalEntityPolicy.BuildFormOptions`: ưu tiên legal entity active có `IsDefaultForPurchase`, nếu không có thì lấy theo priority/code; mỗi legal entity cung cấp default warehouse và danh sách warehouse active của chính nó.
- `stock-document-management.js` lọc lại warehouse khi chọn legal entity, chọn default warehouse phù hợp nếu có và loại warehouse cũ khỏi option khi đổi legal entity. `CreateReceiptAsync`/`UpdateHeaderAsync` gọi `EnsureWarehouseSelectable` để server xác minh warehouse active thuộc legal entity được chọn.
- `PurchaseOrder` lưu trực tiếp `LegalEntityId`, `ExpectedWarehouseId` và `SupplierId`. `PurchaseOrderService.SaveAsync`/commercial update tải các đối tượng trong current store và chặn expected warehouse không thuộc legal entity đã chọn. Receipt tạo từ PO giữ supplier và expected warehouse; Confirm kiểm tra receipt warehouse legal entity bằng order legal entity.

### 8.2 Locked R2 boundary

- Store tiếp tục là tenant/operational boundary; LegalEntity là legal-owner boundary. Không dùng StoreId thay cho legal-owner identity.
- Một Store được có nhiều legal entity active. Mỗi warehouse thuộc đúng một legal entity và cùng Store.
- Một receipt tại Confirm thuộc đúng một legal entity và đúng một warehouse; invariant hiện tại là `receipt legal entity = receipt.Warehouse.LegalEntityId`.
- Direct receipt chọn default legal entity trước, rồi default warehouse của legal entity đó. Người dùng được đổi legal entity; danh sách warehouse phải được lọc lại, warehouse cũ trở thành invalid, và phải chọn warehouse hợp lệ trước khi tiếp tục. `WarehouseId` vẫn required.
- Một PO store-scoped thuộc đúng một legal entity. Supplier phải cùng Store; expected warehouse phải cùng Store và thuộc legal entity của PO. Receipt tạo từ PO phải giữ Store/LegalEntity/Supplier/Warehouse này.
- Nếu tương lai một receipt gom nhiều PO, tất cả PO phải cùng Store, LegalEntity và Supplier.
- Không có quyết định thêm `LegalEntityId` trực tiếp vào `StockDocument`. Source hiện dùng derived legal entity qua warehouse; direct field hoặc immutable snapshot chỉ được quyết định trong một task Level C riêng sau khi đánh giá schema/data-history.

### 8.3 XML legal identities and current gaps

- Seller tax code trên XML là identity dùng để resolve `Supplier`; buyer tax code là identity dùng để resolve `LegalEntity`. Hai tax code có vai trò khác nhau và đều phải được lookup trong cùng Store.
- Target chỉ tự resolve buyer legal entity khi tax code khớp đúng một legal entity trong Store. Thiếu hoặc ambiguous phải do manager xử lý.
- Nếu buyer legal entity từ XML khác legal entity hiện tại của receipt trước Confirm, warehouse cũ không còn hợp lệ; manager phải chọn warehouse thuộc legal entity mới trước Confirm và thay đổi này phải được audit.
- Invoice/receipt invariant là: invoice legal entity được resolve từ buyer tax code phải bằng receipt legal entity được suy ra từ warehouse. Mismatch không phải warning có thể ghi reason để bỏ qua; không được Confirm hoặc tạo link hợp lệ cho đến khi sửa receipt warehouse/legal entity hoặc invoice.
- Receipt đã Confirm không được đổi legal entity/warehouse do XML bổ sung sau đó.
- C1 đã bảo vệ explicit detail mapping: `InputInvoiceRepository.GetInputInvoiceDetailAsync` chứng minh detail thuộc invoice head cùng Store, invoice đã link với đúng receipt, target là receipt, receipt line thuộc receipt, warehouse thuộc Store và có LegalEntity; cùng invoice head không được đang link sang receipt của LegalEntity khác. `InputInvoiceXmlService.UpdateLineMapAsync` còn kiểm tra ownership của receipt line và line map trước khi lookup detail.
- Guard failure trả thông báo business an toàn chung, không lộ foreign detail ID và không mutate map trước khi từ chối. Đây là current implemented behavior của R2.0-C1.
- C1 không triển khai SD1-F13: source vẫn chỉ parse/lưu `InputInvoiceHead.BuyerTaxCode`; upload/view DTO chưa mang buyer LegalEntity, chưa có buyer-tax-code resolver, chưa audit resolution/change và chưa gán LegalEntity identity cho invoice. Guard C1 không được diễn giải thành buyer LegalEntity resolution.

## 9. Supplier and warehouse

Supplier:

- `GaoApp.Domain/Entities/Supplier.cs`, store scoped.
- `GaoApp.Infrastructure/Data/Configurations/SupplierConfiguration.cs`.
- `Code` và `Name` unique trong store.
- `TaxCode` có max length nhưng không có unique index.
- Chưa có entity mapping supplier từ XML.

Warehouse:

- `GaoApp.Domain/Entities/Warehouse.cs`, store scoped, thuộc một legal entity.
- Mỗi store có thể có nhiều warehouse.
- `IsDefault` tồn tại nhưng không có unique constraint bảo đảm một default.
- `StockReceiptLegalEntityPolicy`/form option path chọn default theo legal entity; source test kiểm tra default/switch và cross-legal-entity guard.
- Receipt header luôn có đúng một `WarehouseId`; movement mỗi line sử dụng warehouse này.
- Current model không hỗ trợ một receipt/movement workflow trải trên nhiều kho.

## 10. Supplier invoice/XML

Entities:

- `InputInvoiceHead`: store scoped, thông tin mẫu số/ký hiệu/số/ngày, người bán/mua (gồm `SellerTaxCode` và `BuyerTaxCode`), totals, `OriginalFileName`, `XmlFilePath`, `XmlHash`; không có `LegalEntityId`.
- `InputInvoiceDetail`: line number, item name, unit, quantity, price, amount, VAT; không store scoped trực tiếp và không có item code.
- `StockDocumentInputInvoiceMap`: M:N giữa receipt và invoice head.
- `StockDocumentLineInputInvoiceMap`: mapping receipt line tới optional invoice detail và match status.

Service/API:

- `InputInvoiceXmlService.UploadXmlAsync` nhận bytes, SHA-256, parse XML và liên kết document.
- Parser đọc header/totals, seller tax code từ `NBan/MST`, buyer tax code từ `NMua/MST` và các `HHDVu` detail lines.
- `StockDocumentsController.UploadInputInvoiceXml` là upload endpoint.
- Không có status guard cấm upload sau Confirm; upload sau Confirm không gọi inventory/cost posting.

Storage/dedupe:

- Service lưu parsed entity, filename và hash.
- Source không lưu raw XML bytes/blob/MediaAsset; `XmlFilePath` có trên entity nhưng upload service không gán.
- Hash và business identity chỉ có non-unique indexes; dedupe chính là app-level `GetByXmlHashAsync`.
- Business identity hiện thiếu invoice date trong index và không được enforce unique.

Mapping/gaps:

- Chưa có persistent mapping theo supplier + XML item code + XML unit + GaoApp variant/conversion.
- Detail không lưu XML item code.
- Chưa tự resolve supplier bằng tax code.
- Chưa resolve buyer tax code sang legal entity trong cùng Store; buyer fields cũng chưa được đưa ra upload/view DTO.
- Explicit detail mapping hiện có C1 ownership guard cho Store/receipt/line và từ chối invoice head đã link qua nhiều receipt khác LegalEntity. Tuy nhiên invoice vẫn chưa có LegalEntity được resolve từ buyer tax code, nên invariant invoice LegalEntity = receipt LegalEntity chưa thể được chứng minh theo SD1-F13.
- Chưa có manager-reason workflow cho mismatch/ignored line.
- `GetInputInvoiceDetailAsync(storeId, stockDocumentId, stockDocumentLineId, inputInvoiceDetailId)` dùng relational joins/guards thay vì lookup ID đơn lẻ; rejected mapping không mutate line map.
- `ProductVariant.HasInputInvoice` chỉ được refresh trong Confirm; XML bổ sung sau Confirm không refresh flag này.

## 11. Authorization, audit and tenant isolation

Authorization:

- Permission catalog: `GaoApp.Application/Common/Security/PermissionCatalog.cs`.
- Seed role/permission: `GaoApp.Infrastructure/Data/Seed/SecuritySeedData.cs`.
- Purchase order và purchase receipt có permission tách theo View/Create/Update/Approve/Close/Cancel.
- Admin được seed đầy đủ; Manager có approve; warehouse staff được View/Create/Update receipt nhưng không approve.
- Controller kiểm tra permission theo workflow/source trước khi gọi service.

Audit:

- `AuditSaveChangesInterceptor` ghi audit cho entity thuộc contract audit và có danh sách sensitive entity names.
- `PurchaseOrder`/`StockDocument` có workflow actor/time.
- `StockDocumentLine` không implement `IAuditTrackedEntity`, nên line-level before/after audit chưa hoàn chỉnh.
- Các override R2 như return, đổi supplier, đổi warehouse, chấp nhận overdelivery/mismatch và bỏ qua XML line phải có audit rõ.

Tenant isolation:

- `AppDbContext.ApplyGlobalFilters` áp soft-delete cho `BaseEntity` và store filter cho `BaseStoreEntity`.
- `ValidateTenantOwnership`/`ValidateTenantOwnershipAsync` kiểm tra ownership trước khi ghi và khóa StoreId.
- Repository purchase/receipt phần lớn query theo current store/global filter.
- `InputInvoiceDetail` không kế thừa `BaseStoreEntity`; mọi lookup phải đi qua head/map store-scoped hoặc kiểm tra quan hệ đầy đủ.

## 12. Current status models

`PurchaseOrderStatus`:

- `Draft`
- `PendingApproval`
- `ReturnedForRevision`
- `Rejected`
- `Approved`
- `SentToSupplier`
- `PartiallyReceived`
- `FullyReceived`
- `ShortClosed`
- `Cancelled`

`PurchaseOrderLineReceiptStatus`:

- `NotReceived`
- `PartiallyReceived`
- `FullyReceived`
- `ShortClosed`

`StockDocumentStatus`:

- `Draft`
- `PendingApproval`
- `Confirmed`
- `Rejected`
- `Cancelled`

Other relevant enums:

- `PurchaseReceiptSource`: `LegacyDirect`, `Direct`, `PurchaseOrder`.
- `PurchaseShortageDisposition`: `None`, `WaitForBackorder`, `ShortClose`.
- `InputInvoiceMatchStatus`: `None`, `Matched`, `QuantityMismatch`, `AmountMismatch`, `QuantityAndAmountMismatch`, `Excluded`.
- `InventoryTransactionType.PurchaseReceipt = 10`.

Không có status riêng trong source hiện tại cho “waiting XML” hoặc “incomplete reconciliation”.

## 13. Existing tests

Project: `GaoApp.Tests` (xUnit). Các test dưới đây đã được đọc từ source, **không được chạy trong task tài liệu này**.

### Purchase workflow and receipt

- `Purchases/PurchaseReceiptPolicyTests.cs`: full/partial/second receipt, short-close metadata/reason, missing disposition/reason guard, over-receipt blocked, completed line guard, aggregate order status, payable identity.
- `Purchases/PurchasePricingPolicyTests.cs`: VAT math/rounding, auto/manual freight balance, current landed/base cost behavior, quantity rounding.
- `Purchases/PurchaseOrderWorkflowPolicyTests.cs`: editable/terminal states, authorized review, cancellation guards, source-commercial command contract.
- `Purchases/PurchaseOrderTenantIsolationTests.cs`: store filters, cross-store write guard, model constraints.
- `Purchases/ProvisionalPurchaseItemContractTests.cs`: catalog default, nullable catalog keys cho free text, origin retained after resolution, normal product sellability.

### Inventory/invoice/concurrency

- `Inventory/StockReceiptLegalEntityTests.cs`: default legal entity/warehouse, inactive/cross-entity guards, selected warehouse movement.
- `LegalEntities/LegalEntityFoundationTests.cs`: Store/LegalEntity/Warehouse foundation, legal-entity ownership fields và model relationship.
- `Invoices/InvoiceInputStockRepositoryTests.cs`: confirmed+mapped input stock, issued quantity subtraction, insufficient input stock.
- `Inventory/InputInvoiceXmlTenantGuardTests.cs`: cross-Store/unlinked/wrong-receipt-line/cross-LegalEntity detail bị từ chối, generic safe failure, rejected map không mutation và valid mapping thành công. Đây là C1 tenant/ownership evidence; không phải evidence cho buyer-tax-code LegalEntity resolution.
- `Observability/ConcurrencyAndCancellationContractTests.cs`: `Stock_documents_approve_concurrency_should_return_409` bảo vệ controller mapping, không chứng minh DB idempotency.
- `Products/ProductVariantRepositorySearchTests.cs`: pending catalog product searchable for stock but not POS và repository graph behavior.

### Durable posting relational SQL Server evidence

- `Inventory/InventoryMovementSqlServerConcurrencyTests.cs` dùng SQL Server LocalDB và hai DbContext độc lập để chứng minh concurrent same identity tạo đúng một posting, distinct movements dùng chung initially-missing balance không lost update, committed retry không tạo dependents mới, rollback xóa staged effects và inverse input batches vẫn acquire canonical order không deadlock.
- `Configuration/InventoryPostingMigrationTests.cs` migrate fresh/baseline LocalDB, xác minh nullable `varbinary(32)`, no default/no historical backfill, filtered unique index và duplicate-key rejection.
- `Configuration/DatabaseSchemaManifestTests.cs` khóa manifest của nullable column, exact key order/filter/uniqueness và phân biệt baseline prefix trước migration với current prefix.
- `Inventory/InventoryPosPostingContractTests.cs`, test `Mixed_sales_return_should_persist_legacy_and_legal_entity_restock_after_one_union_prelock`, là mixed Sales Return LocalDB evidence cho union pre-lock, durable subkeys, two persisted return transactions và final balances.
- `Inventory/InventoryNonPosPostingContractTests.cs` có adjustment LocalDB success/rollback/retry tests: `Adjustment_document_success_should_persist_exact_posting_effects_in_order`, `Adjustment_document_second_movement_failure_should_rollback_first_real_posting`, `Approved_adjustment_document_retry_should_not_create_new_postings`.

### Caller orchestration and structural evidence

- `Inventory/InventoryIdempotencyKeyFactoryTests.cs`: golden SHA-256 vector, Unicode normalization, null/empty boundaries, case, field coverage/order và length validation.
- `Inventory/InventoryNonPosPostingContractTests.cs`: pre-lock validation/order, purchase receipt/stock count/transfer/adjustment orchestration, transfer out-before-in và narrow source/AST contracts cho adjustment identity/call ordering.
- `Inventory/InventoryPosPostingContractTests.cs`: POS finalize/void và Sales Return planning/union/order/subkey contracts, gồm pure legacy, pure LegalEntity, mixed, NoRestock và partial-evidence failure.
- `Inventory/InventoryReservationServiceTests.cs`: reserve/release/consume/rebuild key orchestration; rebuild old+new union; active-transaction failure before mutation.
- `Inventory/OrderLegalEntityFinalizeServiceTests.cs`: LegalEntity allocation lock batch và movement order/identity.
- `Inventory/OrderLegalEntityReversalServiceTests.cs`: void/return source restoration, NoRestock, prepared batch và no nested pre-lock.
- `Inventory/InventoryRevaluationPostingContractTests.cs`: exact pre-lock before direct balance access, transaction guard và allocation math/identity.

Evidence boundary:

- Chỉ các SQL Server LocalDB tests ở trên là relational persistence/concurrency evidence.
- EF InMemory, recording repository/service và fake-based tests chứng minh caller behavior/key orchestration; chúng không chứng minh SQL Server lock/concurrency.
- Source/AST contracts chỉ bảo vệ các structural invariant hẹp như call ordering/identity shape; chúng không thay thế behavior hoặc relational evidence.

Remaining missing or insufficient coverage/behavior for future R2:

- Pending receipt immutable theo actor/permission.
- Multi-PO receipt; alternate receiving unit; managed overdelivery; close/reopen.
- VAT-in-cost toggle, freight capitalization toggle, discount.
- XML business-identity unique và post-confirm refresh.
- Seller supplier-tax-code matching, buyer LegalEntity resolution/same-legal-entity guard và XML product mapping.

## 14. Related migration and schema evidence

EF migration baseline:

- `GaoApp.Infrastructure/Migrations/20260726073029_InitialProductionBaseline.cs`
- `GaoApp.Infrastructure/Migrations/20260726073029_InitialProductionBaseline.Designer.cs`
- `GaoApp.Infrastructure/Migrations/20260801110856_AddInventoryPostingIdempotency.cs`
- `GaoApp.Infrastructure/Migrations/20260801110856_AddInventoryPostingIdempotency.Designer.cs`
- `GaoApp.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`

Relevant tables:

- `PurchaseOrders`, `PurchaseOrderLines`, `PurchaseOrderActions`, `PurchasePayables`
- `StockDocument`, `StockDocumentLine`
- `InventoryTransactions`, `InventoryBalances`, `InventoryValuationEntries`, `InventoryCostLayers`, `InventoryCostLayerAllocations`
- `ProductUnitConversion`
- `Suppliers`, `LegalEntities`, `Warehouses`
- `InputInvoiceHead`, `InputInvoiceDetail`, `StockDocumentInputInvoiceMap`, `StockDocumentLineInputInvoiceMap`

Important constraint observations:

- Balance key is unique by store/warehouse/variant.
- Durable inventory movement protection là filtered unique index `StoreId + IdempotencyKey` cho keyed active/non-deleted rows; historical rows có thể giữ null key. Legacy reference-field index vẫn tồn tại cho query/compatibility nhưng không phải durable uniqueness boundary.
- Supplier tax code is not unique.
- Legal entity tax code is unique per Store for non-empty, non-deleted rows.
- Warehouse uses a same-Store composite foreign key to its required legal entity.
- Input invoice hash/business identity indexes are not unique.
- Warehouse default is not uniqueness-enforced.
- Product conversion is unique by store/variant/unit, but one-base-unit is not DB-enforced.

## 15. Historical resolved gaps, remaining limitations and verification boundaries

Historical gaps resolved by R2.0-C2:

- Trước C2, durable movement protection chỉ dựa vào application lookup và balance creation chưa có deterministic transaction-scoped protocol. C2A thiết lập canonical key, migration/index, transaction coordinator và SQL Server lock/get-or-create; C2B1 áp dụng cho non-POS callers; C2B2 áp dụng cho POS, Return, LegalEntity, Reservation và Revaluation.
- Các mô tả lịch sử này giải thích SD1-F03/SD1-F12; chúng không phải current open blockers tại expected parent `0a5b2cf5734fd69dfa1e709de3984222c1ee4158`.

Remaining limitations/non-blocking notes:

- Không chạy ứng dụng, build, test hoặc database query; runtime behavior ngoài call chain đọc từ source chưa được chứng minh.
- Không xác minh dữ liệu production, mức độ duplicate thực tế hoặc execution plan/index contention.
- Public `InventoryReservationService.ReserveForOrderAsync` hiện không có registered production runtime caller trong source search; method đã có active-transaction/pre-lock contract nhưng activation của caller là future integration work.
- `InventoryRevaluationService.ResolveByInboundLayerAsync` có thể pre-lock lại exact key đã được outer purchase-receipt flow giữ. Đây là thao tác cùng transaction/key và không phải blocker C2C, nhưng là điểm cần theo dõi khi tối ưu contention.
- Không có global automatic retry/replay; caller nhận conflict để quyết định reload/retry theo workflow.
- C2C chỉ reconciliation tài liệu; không thay đổi runtime, test, project, migration/schema, database hoặc CI.
- SD1-F01 pending receipt immutability vẫn Open.
- SD1-F13 buyer-tax-code LegalEntity resolution/invoice-owner invariant vẫn Open; C1 guard không đóng finding này.
- Role tùy biến có thể được cấu hình ngoài seed; source chỉ chứng minh permission checks và seeded defaults.
- Chưa có quyết định schema cụ thể cho multi-PO receipt, close/reopen, cost toggles hoặc XML mapping; các task Level C phải chốt model/migration riêng.
- Chưa quyết định thêm direct/snapshot `LegalEntityId` vào `StockDocument`; current invariant tiếp tục derive legal entity từ required warehouse.
- `XmlFilePath` có trong model nhưng source path đọc không cho thấy storage write; cần quyết định retention/storage trước khi triển khai.
- Payables hiện là regression cần giữ, nhưng mở rộng công nợ nhà cung cấp nằm ngoài R2.

## 16. Maintenance rule

Khi một task thay đổi bất kỳ mục nào dưới đây, Implementation Handoff phải chỉ ra có cần cập nhật source map hay không:

- entity/status/schema/migration;
- controller route hoặc request DTO;
- Confirm transaction/cost/movement chain;
- permission/role policy;
- tenant/audit behavior;
- test class bảo vệ contract;
- vị trí file hoặc project responsibility.

Không cập nhật tài liệu bằng phỏng đoán. Mọi thay đổi bản đồ phải dẫn tới file/method/test/migration đã tồn tại ở expected parent hoặc trong allowed diff của task.
