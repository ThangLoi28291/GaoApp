# GaoApp Backlog Findings

## 1. Purpose and classification

Tài liệu này ghi các finding có evidence nhưng không được tự ý sửa ngoài Task Contract. Mỗi finding có:

- ID
- Date
- Classification
- Severity
- Module
- Description
- Evidence
- Impact
- Target task
- Status
- Scope decision

Severity dùng `Critical`, `High`, `Medium`, `Low`. Classification dùng taxonomy trong workflow.

## 2. Priority order before R2 feature expansion

Các blocker phải được xử lý theo thứ tự dependency:

1. `SD1-F02` — tenant/link validation của XML detail.
2. `SD1-F03` — durable movement idempotency.
3. `SD1-F12` — InventoryBalance get-or-create race.
4. `SD1-F01` — Pending receipt immutability.

F02 là security/data blocker. F03/F12 bảo vệ inventory correctness. F01 phải xong trước khi mở rộng employee/manager workflow. `SD1-F13` phải được thực hiện sau F02 và trước khi mở rộng XML reconciliation/linking theo legal owner.

## 3. Findings

### SD1-DOC-01 — Missing long-lived governance documents

- **Date:** 2026-07-30
- **Classification:** Missing
- **Severity:** Medium
- **Module:** Development governance
- **Description:** Baseline không có bốn tài liệu source map, development workflow, decision log và backlog findings.
- **Evidence:** `docs/development` không tồn tại tại expected parent `6f909420092ce0f7d2a7100b8eb94fb964727c0c`.
- **Impact:** Knowledge/decisions nằm rải rác; task dễ sai base, scope và source call chain.
- **Target task:** R2.0-A2
- **Status:** In progress
- **Scope decision:** Chỉ đóng sau khi Coordinator review và merge đủ bốn tài liệu; việc file đã được tạo trên working branch chưa tự đóng finding.

### SD1-F01 — Pending receipt remains editable

- **Date:** 2026-07-30
- **Classification:** Pre-existing defect
- **Severity:** High
- **Module:** Purchase receipt workflow / authorization
- **Description:** Service edit guard cho phép `PendingApproval`, trái với R2 rule employee không được sửa sau submit trừ khi manager trả lại.
- **Evidence:** `GaoApp.Application/Services/Inventory/StockDocumentService.cs`, `EnsureEditable` accepts `Draft`, `PendingApproval`, `Rejected`; các add/update/delete line call sites dùng guard này. `git blame` quy source path về commit trước expected parent.
- **Impact:** Submitted physical quantity/product data có thể thay đổi trong lúc manager duyệt; audit/approval snapshot không đáng tin.
- **Target task:** R2.1-B1 — Lock submitted receipt and formalize return-for-revision.
- **Status:** Open — blocker before workflow expansion
- **Scope decision:** Không sửa trong R2.0-A2; task sau docs, dependency sau F02/F03/F12 theo integration plan.

### SD1-F02 — XML detail lookup does not prove store/invoice/receipt linkage

- **Date:** 2026-07-30
- **Classification:** Pre-existing defect
- **Severity:** Critical
- **Module:** Supplier invoice XML / tenant security
- **Description:** Khi map receipt line tới XML detail, service lấy `InputInvoiceDetail` chỉ bằng ID. Detail không store scoped trực tiếp và code không kiểm tra detail thuộc invoice đã link với receipt/store hiện tại. Tenant-safe mapping còn phải bảo đảm invoice và receipt cùng LegalEntity; receipt LegalEntity được derive từ required `Warehouse.LegalEntityId`, và không được có cross-LegalEntity map dù hai bên cùng Store.
- **Evidence:** `InputInvoiceXmlService.UpdateLineMapAsync` gọi `InputInvoiceRepository.GetInputInvoiceDetailAsync(id)`; repository query `InputInvoiceDetails.FirstOrDefaultAsync(x => x.Id == id)`. Receipt line được store-checked nhưng XML detail relation và same-LegalEntity invariant không được chứng minh. `StockDocument` không có `LegalEntityId`; legal owner hiện đi qua `Warehouse.LegalEntityId`.
- **Impact:** Có khả năng cross-tenant/cross-invoice/cross-legal-entity data association và sai legal owner của dữ liệu đối chiếu.
- **Target task:** R2.0-C1 — Tenant-safe XML detail mapping guard.
- **Status:** Open — immediate security/data blocker
- **Scope decision:** Task đầu tiên sau docs; không chờ XML feature expansion. Guard phải không cho tạo cross-LegalEntity map; buyer-tax-code resolution đầy đủ được theo dõi tiếp ở F13.

### SD1-F03 — Inventory movement idempotency is application-only/non-unique

- **Date:** 2026-07-30
- **Classification:** Pre-existing defect
- **Severity:** Critical
- **Module:** Inventory transaction / Confirm concurrency
- **Description:** `SkipIfExists` gọi `ExistsAsync` trước insert, nhưng business-reference index không unique. Hai transaction đồng thời có thể cùng không thấy record rồi cùng insert.
- **Evidence:** `InventoryMovementService.CreateAsync`; `InventoryTransactionRepository.ExistsAsync`; `InventoryTransactionConfiguration` tạo non-unique index trên store/reference/reference-line/type. `git blame` cho thấy code có trước expected parent.
- **Impact:** Duplicate quantity movement, valuation entry, FIFO layer và inventory value khi double submit/concurrent Confirm.
- **Target task:** R2.0-C2 — Durable receipt posting idempotency and concurrent Confirm.
- **Status:** Open — blocker
- **Scope decision:** Phải sửa trước mở rộng Confirm; cần migration, unique identity và real relational concurrency test.

### SD1-F04 — Invoice hash and business identity are not database-unique

- **Date:** 2026-07-30
- **Classification:** Existing but incomplete
- **Severity:** High
- **Module:** Supplier invoice XML / schema
- **Description:** Source dedupe hash ở application layer; indexes cho hash/business fields không unique. Current business index thiếu invoice date so với locked identity.
- **Evidence:** `InputInvoiceXmlService.UploadXmlAsync` + `InputInvoiceRepository.GetByXmlHashAsync`; `InputInvoiceConfiguration` non-unique indexes; `InputInvoiceHead.InvoiceDate`.
- **Impact:** Concurrent upload hoặc XML khác serialization có thể tạo duplicate invoice records/links.
- **Target task:** R2.4-C1 — Invoice identity normalization and uniqueness.
- **Status:** Open
- **Scope decision:** Gộp với schema XML expansion sau khi F02 hoàn tất; audit duplicate data trước migration.

### SD1-F05 — Post-confirm XML does not refresh HasInputInvoice

- **Date:** 2026-07-30
- **Classification:** Existing but incomplete
- **Severity:** Medium
- **Module:** Supplier invoice XML / product metadata
- **Description:** `ProductVariant.HasInputInvoice` được set trong receipt Confirm nếu line map đã tồn tại; upload/link XML sau Confirm không refresh flag.
- **Evidence:** `StockDocumentService.ApproveTrackedAsync` gọi `MarkVariantsHasInputInvoiceAsync`; `InputInvoiceXmlService.UploadXmlAsync` chỉ tạo invoice/maps/save.
- **Impact:** Metadata/search/report có thể không phản ánh invoice bổ sung sau Confirm.
- **Target task:** R2.4-B3 — Post-confirm XML reconciliation metadata.
- **Status:** Open
- **Scope decision:** Fix không được làm thay đổi stock/cost hoặc tạo movement.

### SD1-F06 — Current inventory cost always includes after-VAT merchandise and freight

- **Date:** 2026-07-30
- **Classification:** Existing but incomplete
- **Severity:** High
- **Module:** Purchase costing / inventory valuation
- **Description:** Current Confirm tính base cost từ line total sau VAT cộng freight allocation. Locked target yêu cầu VAT configurable và freight default không capitalize.
- **Evidence:** `StockDocumentService.ApproveTrackedAsync`; `PurchasePricingPolicy.CalculateBaseUnitCost`; `PurchasePricingPolicyTests.Base_unit_cost_should_include_after_vat_merchandise_and_freight_after_conversion`.
- **Impact:** Giá vốn R2 sẽ sai policy nếu giữ nguyên; thay đổi trực tiếp có thể ảnh hưởng valuation/payable và dữ liệu lịch sử.
- **Target task:** R2.3-C1 — Cost basis and capitalization policy.
- **Status:** Open
- **Scope decision:** Level C riêng, không trộn với UI-only approval task.

### SD1-F07 — Paid direct receipt can Confirm without SupplierId

- **Date:** 2026-07-30
- **Classification:** Pre-existing defect
- **Severity:** High
- **Module:** Purchase receipt / supplier
- **Description:** Commercial approval cho direct receipt đã trả ngay có thể dùng payee name và supplier null; R2 bắt buộc supplier trước Confirm.
- **Evidence:** `StockDocumentService.ApproveCommercialAsync` supplier/payment validation; `_CommercialApprovalWorkbench.cshtml` mô tả payee dùng khi trả ngay và chưa có supplier.
- **Impact:** Confirmed receipt có thể thiếu supplier, làm yếu XML matching, price history và reporting.
- **Target task:** R2.1-B2 — Confirm prerequisites and supplier selection.
- **Status:** Open
- **Scope decision:** Giữ nullable ở Draft; block ở Confirm. Không làm SupplierId DB non-null nếu Draft còn cho phép null.

### SD1-F08 — Missing overdelivery, alternate receipt unit, close/reopen workflow

- **Date:** 2026-07-30
- **Classification:** Missing
- **Severity:** High
- **Module:** Purchase order receiving
- **Description:** Source chặn over-receipt, receipt-from-PO giữ nguyên unit/conversion, và chỉ có short-close trong Confirm; chưa có full manager close/reopen commands.
- **Evidence:** `PurchaseReceiptPolicy.ValidateLine`; `PurchaseReceiptPolicyTests.Over_receipt_should_be_blocked`; `StockDocumentService.CreateReceiptFromPurchaseOrderAsync`; PO line short-close fields.
- **Impact:** Không đáp ứng giao dư, nhận khác đơn vị, đóng/mở lại phần thiếu.
- **Target task:** R2.2-B/C series — quantity projections, alternate units, managed overdelivery, close/reopen.
- **Status:** Open
- **Scope decision:** Tách tasks để tránh cùng sửa service/policy/entity/migration.

### SD1-F09 — StockDocumentLine audit coverage is incomplete

- **Date:** 2026-07-30
- **Classification:** Existing but incomplete
- **Severity:** High
- **Module:** Audit / receipt lines
- **Description:** Header có audit/workflow actors nhưng `StockDocumentLine` không implement audit tracking contract; line-level override/change history chưa đầy đủ.
- **Evidence:** `StockDocument` implements `IAuditTrackedEntity`; `StockDocumentLine` chỉ kế thừa `BaseEntity`; `AuditSaveChangesInterceptor`.
- **Impact:** Khó chứng minh ai đổi quantity/product/unit/price/discount/allocation hoặc accepted override.
- **Target task:** R2.1-C3 — Receipt audit events and sensitive overrides.
- **Status:** Open
- **Scope decision:** Thiết kế event/action audit rõ; không chỉ thêm interface nếu không bảo vệ field-level meaning.

### SD1-F10 — Price variance is informational only

- **Date:** 2026-07-30
- **Classification:** Existing but incomplete
- **Severity:** Medium
- **Module:** Purchase pricing UI/workflow
- **Description:** UI hiển thị last price và variance text nhưng chưa có explicit warning contract/server evidence cho mọi thay đổi giá.
- **Evidence:** `_CommercialApprovalWorkbench.cshtml` elements `commercial-last-price`, `commercial-price-variance`; `purchase-receipt-approval.js` client calculation.
- **Impact:** Manager có thể bỏ qua biến động giá; behavior khác nhau nếu client code không chạy.
- **Target task:** R2.3-B2 — Durable price variance warning.
- **Status:** Open
- **Scope decision:** Warning bắt buộc; **không** tự thêm rule bắt buộc reason cho price variance.

### SD1-F11 — WarehouseId required in Draft

- **Date:** 2026-07-30
- **Classification:** Closed — Not a defect
- **Severity:** Low
- **Module:** Purchase receipt / warehouse
- **Description:** Discovery ban đầu đặt câu hỏi có nên cho warehouse null ở Draft.
- **Evidence:** `StockDocument.WarehouseId` required; default warehouse helper/tests; Coordinator correction.
- **Impact:** Không có defect theo target đã khóa.
- **Target task:** N/A
- **Status:** Closed
- **Scope decision:** Giữ WarehouseId required. Auto-select default; nếu không có thì yêu cầu chọn/block rõ. Không tạo migration nullable.

### SD1-F12 — InventoryBalance GetOrCreate race

- **Date:** 2026-07-30
- **Classification:** Pre-existing defect
- **Severity:** Critical
- **Module:** Inventory balance / concurrency
- **Description:** Repository query balance rồi add nếu thiếu. Hai Confirm cho cùng store/warehouse/variant khi chưa có balance có thể cùng add và va unique key hoặc không có retry deterministic.
- **Evidence:** `InventoryBalanceRepository.GetOrCreateAsync`; unique index trong `InventoryBalanceConfiguration`; `InventoryMovementService.CreateAsync` call site. `git blame` cho thấy implementation có trước expected parent.
- **Impact:** Một hoặc cả hai Confirm có thể fail; transaction/retry behavior chưa được contract-test, có nguy cơ partial operational failure dù DB rollback.
- **Target task:** R2.0-C2 — Durable receipt posting idempotency and inventory concurrency (same task as SD1-F03).
- **Status:** Open — blocker
- **Scope decision:** Xử lý sau durable movement idempotency, trước receipt feature expansion; test trên relational provider.

### SD1-F13 — XML buyer LegalEntity resolution is missing

- **Date:** 2026-07-31
- **Classification:** Missing
- **Severity:** High
- **Module:** Supplier invoice XML / LegalEntity ownership
- **Description:** Parser lưu buyer tax code từ XML nhưng upload/link/view workflow không resolve buyer sang một LegalEntity duy nhất trong cùng Store, không xử lý absent/ambiguous match và không enforce invoice LegalEntity bằng receipt LegalEntity được derive từ warehouse.
- **Evidence:** `InputInvoiceXmlService.ParseXmlToEntity` gán `InputInvoiceHead.BuyerTaxCode` từ `NMua/MST`; `InputInvoiceXmlDtos` và `InputInvoiceViewDtos` không expose buyer LegalEntity; `InputInvoiceXmlService.UploadXmlAsync` và repository call chain không có buyer-tax-code-to-LegalEntity lookup hoặc invoice/receipt legal-owner guard.
- **Impact:** Hóa đơn có thể được liên kết/đối chiếu mà legal owner chưa được chứng minh; không có workflow an toàn để đổi legal entity/warehouse trước Confirm hoặc block mismatch.
- **Target task:** R2.4-C2 — XML buyer LegalEntity resolution and invoice/receipt legal-owner guard.
- **Status:** Open — dependency after SD1-F02
- **Scope decision:** Không sửa trong task tài liệu. Thực hiện sau security blocker F02; lookup phải same-Store/exactly-one, seller/buyer identities tách biệt, mismatch không được reason override, thay đổi trước Confirm phải audit và không đổi confirmed receipt.

## 4. Maintenance

- Finding mới phải có file/method evidence trước khi thêm.
- Nếu Git history chứng minh pre-existing, ghi commit/blame evidence; không gắn nhãn regression cho task hiện tại.
- Chỉ Coordinator hoặc task được ủy quyền mới đổi status sang `Closed`.
- Khi finding được sửa, liên kết task/commit/review/CI evidence và giữ nguyên lịch sử mô tả.
