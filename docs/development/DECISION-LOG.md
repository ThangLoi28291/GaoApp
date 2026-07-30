# GaoApp Decision Log

## 1. Usage

Tài liệu này lưu các quyết định kiến trúc/nghiệp vụ dài hạn. Mỗi quyết định dùng format:

- **ID**
- **Date**
- **Status**: `Proposed`, `Accepted`, `Superseded`
- **Context**
- **Decision**
- **Consequences**
- **Source**
- **Evidence**

Không sửa lịch sử để che quyết định cũ. Khi thay đổi, tạo decision mới và đánh dấu decision cũ `Superseded`.

---

## GOV-001 — R1 completed baseline

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** R2 cần một expected parent ổn định để khảo sát và chia task.
- **Decision:** Dùng `integration/r1-ci-windows-runner` tại `6f909420092ce0f7d2a7100b8eb94fb964727c0c` làm baseline hoàn tất R1 cho bộ governance R2.
- **Consequences:** Mọi R2 task contract phải ghi base/expected parent rõ; không tự kéo thay đổi mới vào task đang chạy.
- **Source:** Coordinator task R2.0-A2.
- **Evidence:** Git pre-flight của R2.0-A2 và source map verified baseline.

## GOV-002 — R2 Supplier Ordering & Receiving scope

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Purchase ordering, receiving, approval, stock and costing có cùng transaction/data path.
- **Decision:** R2 bao phủ chuỗi Supplier Order → Receipt → Manager Approval → Confirm → Inventory/Cost. Không coi từng bước là feature cô lập khi thiết kế invariant.
- **Consequences:** Task có thể tách nhỏ nhưng dependency/call chain phải giữ end-to-end correctness.
- **Source:** Coordinator locked business scope.
- **Evidence:** `PurchaseOrderService`, `StockDocumentService.ApproveCommercialAsync`, `ApproveTrackedAsync`, `InventoryMovementService.CreateAsync`.

## LEGAL-001 — Store and LegalEntity are separate boundaries

- **Date:** 2026-07-31
- **Status:** Accepted
- **Context:** Một Store có thể vận hành nhiều pháp nhân; tenant isolation không đủ để xác định legal owner của kho/chứng từ/hóa đơn.
- **Decision:** `Store` là tenant/operational boundary. `LegalEntity` là legal-owner boundary bên trong Store. Một Store được có nhiều legal entity active; mọi lookup/link vẫn phải cùng Store và không được dùng StoreId thay cho legal-owner identity.
- **Consequences:** Task purchase/inventory/XML phải kiểm tra cả Store và LegalEntity; review phải coi cross-legal-entity association là data/correctness blocker ngay cả khi cùng Store.
- **Source:** Coordinator amendment R2.0-A2-CA1.
- **Evidence:** `BaseStoreEntity`, `AppDbContext.ApplyGlobalFilters`, `LegalEntity`, `LegalEntityConfiguration`.

## LEGAL-002 — Warehouse belongs to one LegalEntity

- **Date:** 2026-07-31
- **Status:** Accepted
- **Context:** Mỗi stock location phải có một legal owner xác định; current `StockDocument` có required `WarehouseId` nhưng không có `LegalEntityId`.
- **Decision:** Mỗi `Warehouse` thuộc đúng một `LegalEntity` trong cùng Store. Một receipt tại Confirm có đúng một warehouse và legal entity tương ứng; với current model, `receipt LegalEntity = receipt.Warehouse.LegalEntityId`. Không cho dùng warehouse của legal entity khác.
- **Consequences:** Service phải verify warehouse/owner/same Store. Không được suy ra receipt legal owner chỉ từ StoreId.
- **Source:** Coordinator amendment R2.0-A2-CA1.
- **Evidence:** `StockDocument`, `Warehouse`, `WarehouseConfiguration`, `StockDocumentConfiguration`.

## LEGAL-003 — Direct receipt LegalEntity/Warehouse selection

- **Date:** 2026-07-31
- **Status:** Accepted
- **Context:** Direct receipt cần convenience defaults nhưng không được làm yếu warehouse/legal-owner invariant.
- **Decision:** Hệ thống chọn default LegalEntity rồi default warehouse thuộc LegalEntity đó. Người dùng được đổi LegalEntity; danh sách warehouse phải lọc theo owner mới, warehouse cũ trở thành invalid và phải chọn warehouse hợp lệ trước khi tiếp tục. `WarehouseId` vẫn required.
- **Consequences:** UI phải re-filter/reselect; create/update service phải validate active LegalEntity và warehouse ownership. Không đổi `WarehouseId` thành nullable.
- **Source:** Coordinator amendment R2.0-A2-CA1.
- **Evidence:** `StockReceiptLegalEntityPolicy`, `StockDocumentService.CreateReceiptAsync`, `UpdateHeaderAsync`, `stock-document-management.js`, `StockReceiptLegalEntityTests`.

## LEGAL-004 — Purchase and receipt LegalEntity consistency

- **Date:** 2026-07-31
- **Status:** Accepted
- **Context:** PO lưu `LegalEntityId`, `ExpectedWarehouseId` và `SupplierId`; receipt-from-PO phải giữ context này.
- **Decision:** Một PO store-scoped thuộc đúng một LegalEntity. Supplier phải cùng Store; expected warehouse phải cùng Store và thuộc LegalEntity của PO. Receipt tạo từ PO giữ Store/LegalEntity/Supplier/Warehouse. Multi-PO receipt tương lai chỉ được gom PO cùng Store, LegalEntity và Supplier.
- **Consequences:** Mọi create/update/link/Confirm path phải reject cross-store hoặc cross-legal-entity combinations; line-level linkage không được làm mất legal-owner invariant.
- **Source:** Coordinator amendment R2.0-A2-CA1.
- **Evidence:** `PurchaseOrder`, `PurchaseOrderService.SaveAsync`, `StockDocumentService.CreateReceiptFromPurchaseOrderAsync`, `ValidateReceiptSourceAndShortages`.

### Schema boundary — not decided

Current `StockDocument`/`StockDocumentConfiguration` không có `LegalEntityId`; receipt legal entity được derive từ required `Warehouse.LegalEntityId`. Việc thêm direct/snapshot `LegalEntityId` chưa được quyết định và không phải schema decision của bộ tài liệu này. Nếu cần immutable historical legal-owner snapshot hoặc direct field, Coordinator phải mở task Level C riêng để chốt model, backfill, constraints, rollback và compatibility.

## PUR-001 — Purchase Order and receipt document model

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Source có `PurchaseOrder`; receipt đang là `StockDocument`.
- **Decision:** Giữ Purchase Order là chứng từ đặt nhà cung cấp. Trong R2, phiếu nhập mua tiếp tục dùng `StockDocument` với `Type=Receipt`; không tạo thêm một entity phiếu kho riêng.
- **Consequences:** Mở rộng state/link/cost trên model hiện có; tránh duplicate warehouse document.
- **Source:** Coordinator locked decision + current source.
- **Evidence:** `PurchaseOrder.cs`, `StockDocument.cs`, `StockDocumentType.Receipt`.

## PUR-002 — Multiple receipts per purchase order

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Nhà cung cấp có thể giao nhiều lần.
- **Decision:** Một PO có thể được nhận qua nhiều receipt. Giao diện/projection phải tách số đã đặt, số đang chờ duyệt, số đã Confirm, official remaining và số giao dư. Chỉ lượng từ receipt `Confirmed` làm giảm official remaining.
- **Consequences:** Pending approval và confirmed quantity phải được tách; retry/return/cancel không được giảm official remaining.
- **Source:** Coordinator locked business rules 2, 13, 14.
- **Evidence:** Current `PurchaseOrder.Receipts`, line received fields và `ApplyApprovedReceiptToPurchaseOrder`; pending aggregate còn thiếu.

## PUR-003 — Direct receipt

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Có hàng nhận không bắt nguồn từ PO.
- **Decision:** Cho phép tạo receipt không có PO, với direct-receipt reason theo policy hiện hữu.
- **Consequences:** `PurchaseOrderId`/`PurchaseOrderLineId` có thể null; Confirm vẫn phải thỏa supplier/warehouse/product/cost rules.
- **Source:** Coordinator locked business rule 3.
- **Evidence:** `StockDocumentService.CreateReceiptAsync`, `PurchaseReceiptSource.Direct`.

## PUR-004 — Line-level PO/receipt linkage and multi-PO receipt

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** R2 cần trace nhận hàng theo từng dòng và có thể gom các PO cùng supplier.
- **Decision:** Liên kết receipt với PO ở cấp từng dòng. Một receipt có thể chứa dòng từ nhiều PO nếu tất cả cùng Store, LegalEntity và Supplier; header không được là nguồn duy nhất của linkage.
- **Consequences:** Current single `StockDocument.PurchaseOrderId` không đủ cho target; cần task schema/workflow Level C. Mọi line phải chống cross-store/cross-legal-entity/cross-supplier/cross-order link.
- **Source:** Coordinator locked business decision.
- **Evidence:** Current `StockDocumentLine.PurchaseOrderLineId`; current `StockDocument.PurchaseOrderId` giới hạn một PO.

## PUR-005 — Draft supplier and required warehouse

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Nhân viên có thể chưa biết supplier khi nhập Draft; warehouse target vẫn là invariant hiện hữu.
- **Decision:** `SupplierId` được null ở Draft nhưng bắt buộc trước Confirm. `WarehouseId` vẫn bắt buộc, không đổi nullable. Draft chọn default LegalEntity rồi default warehouse của LegalEntity đó; nếu không có default thì yêu cầu chọn hoặc block rõ. Manager/Admin có thể đổi legal entity/kho trước Confirm, nhưng warehouse phải thuộc legal entity mới và cùng Store. Mỗi receipt khi Confirm có đúng một supplier, một legal entity và một warehouse đích.
- **Consequences:** SD1-F11 đóng là “Not a defect”. UI/service cần phân biệt Draft convenience với Confirm guard.
- **Source:** Coordinator correction.
- **Evidence:** `StockDocument.SupplierId` nullable, `WarehouseId` required; `StockReceiptLegalEntityTests`.

## PUR-006 — Submitted receipt immutability

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Source hiện cho edit `PendingApproval`.
- **Decision:** Sau khi employee submit, employee không được sửa. Chỉ sửa lại sau khi manager trả về và actor có permission phù hợp.
- **Consequences:** `EnsureEditable` và endpoint authorization phải được tách theo state/actor; F01 là blocker trước workflow expansion.
- **Source:** Coordinator locked business rule 9.
- **Evidence:** Current `StockDocumentService.EnsureEditable` cho Draft/PendingApproval/Rejected.

## PUR-007 — Confirm authority and Confirm-only posting

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Confirm ghi stock, valuation, cost layer và payable.
- **Decision:** Chỉ Manager/Admin hoặc equivalent approve permission được Confirm. Inventory chỉ tăng khi Confirm. Receipt `Confirmed` không sửa trực tiếp.
- **Consequences:** Confirm phải atomic, audited, concurrency-safe; mọi post-confirm correction cần workflow riêng.
- **Source:** Coordinator locked business rules 10–12.
- **Evidence:** `StockDocumentsController.ApproveCommercial`, `StockDocumentService.ApproveTrackedAsync`.

## PUR-008 — Receiving quantities and overdelivery

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Thực tế có giao thiếu hoặc dư; source đang chặn giao dư.
- **Decision:** Cho nhận thiếu và giao dư. Giao dư cần manager acceptance rõ.
- **Consequences:** Policy/UI/audit/test phải thêm overdelivery path; official remaining không âm và overdelivery hiển thị riêng.
- **Source:** Coordinator locked business rules 15–16.
- **Evidence:** `PurchaseReceiptPolicy.ValidateLine` và `Over_receipt_should_be_blocked` thể hiện current conflicting behavior.

## PUR-009 — Close and reopen outstanding quantities

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Supplier có thể không giao phần còn lại.
- **Decision:** Manager/Admin được đóng phần thiếu theo line hoặc toàn PO, bắt buộc reason. Có thể mở lại phần đã đóng nếu PO chưa hoàn tất.
- **Consequences:** Cần commands/audit/status calculation rõ; reopen không được sửa quantity đã Confirm.
- **Source:** Coordinator locked business rules 17–18.
- **Evidence:** Current line có short-close fields nhưng chưa có complete close/reopen workflow.

## PUR-010 — Next receipt quantity defaults

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Pre-filling actual received quantity bằng remaining dễ xác nhận nhầm.
- **Decision:** Receipt tiếp theo hiển thị remaining để tham khảo nhưng actual received quantity để trống cho nhân viên nhập.
- **Consequences:** UI DTO phải tách suggested remaining và actual input.
- **Source:** Coordinator locked R2 behavior.
- **Evidence:** Current create-from-order logic cần được review trong implementation task.

## UNIT-001 — Multi-unit receiving and base conversion

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Đơn vị đặt và đơn vị nhận có thể khác nếu có conversion.
- **Decision:** Cho phép order unit và receipt unit khác khi có factor hợp lệ. Stock posting luôn quy đổi về base unit theo cơ chế hiện có.
- **Consequences:** Giữ snapshot conversion/factor; validate active conversion và tenant/variant ownership.
- **Source:** Coordinator locked business rules 19–20.
- **Evidence:** `InventoryUnitResolver`, `StockDocumentLine.BaseQuantity`, `InventoryMovementFactory.CreatePurchaseReceipt`.

## UNIT-002 — Preserve InventoryUnitResolver

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Source đã có resolver/factor/base-quantity path hoạt động.
- **Decision:** Không viết lại cơ chế unit conversion hiện hữu nếu không có evidence cụ thể; R2 mở rộng quanh `InventoryUnitResolver`.
- **Consequences:** Task unit changes phải có regression tests cho base fallback và conversion.
- **Source:** Coordinator constraint.
- **Evidence:** `GaoApp.Application/Services/Inventory/InventoryUnitResolver.cs`.

## PRODUCT-001 — Product identity

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** “Mã sản phẩm” cần được diễn giải đúng với source.
- **Decision:** Technical identity là `ProductVariantId`; mã hiển thị/đối chiếu là `ProductVariant.Sku`. Tên chỉ hỗ trợ kiểm tra, không là khóa.
- **Consequences:** Merge, mapping và Confirm validate variant; không match bằng tên một cách tự động.
- **Source:** Coordinator correction.
- **Evidence:** `StockDocumentLine.ProductVariantId`, `SkuSnapshot`, `ProductVariant.Sku`.

## PRODUCT-002 — Free-text then resolve

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Employee có thể chỉ biết tên lúc đặt/nhận, nhưng inventory không thể ghi vào sản phẩm tạm.
- **Decision:** Cho phép free-text ở PO/Draft flow. Trước Confirm, mọi receipt line phải resolve sang `ProductVariantId` thật; missing/invalid variant block Confirm.
- **Consequences:** Resolution origin/audit phải giữ; không tạo inventory identity từ tên.
- **Source:** Coordinator locked business rules 7, 22.
- **Evidence:** `PurchaseOrderLine` supports FreeText; `StockDocumentLine.ProductVariantId` required.

## PRODUCT-003 — Receipt line merge key

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Gộp quá rộng làm mất commercial/unit identity.
- **Decision:** Chỉ gộp line khi cùng `ProductVariantId`, cùng conversion/unit, cùng unit price, cùng VAT/tax và cùng discount. Khác unit giữ line riêng.
- **Consequences:** Client/server merge phải dùng cùng canonical key; discount field cần được thêm trước khi rule hoàn chỉnh.
- **Source:** Coordinator locked business rule 21.
- **Evidence:** Current service merge có variant/unit/cost/tax nhưng chưa discount và chưa target alternate-unit flow.

## COST-001 — Manager-owned commercial fields

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Employee xác nhận vật lý; manager chịu trách nhiệm commercial.
- **Decision:** Employee nhập tên/số lượng; manager xử lý giá, VAT, discount, freight và additional cost trước Confirm. VAT và discount được lưu theo từng dòng.
- **Consequences:** Submitted physical fields immutable; approval payload không nhận product/unit/quantity.
- **Source:** Coordinator locked business rule 8.
- **Evidence:** `ApprovePurchaseReceiptCommercialRequest` cố ý không có product/unit/quantity.

## COST-002 — VAT cost option

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Current cost luôn dùng amount sau VAT.
- **Decision:** R2 phải có tùy chọn đưa VAT vào hoặc loại VAT khỏi giá vốn.
- **Consequences:** Cần persist choice, deterministic rounding và test cả hai path; current behavior là gap F06.
- **Source:** Coordinator locked business rule 34.
- **Evidence:** `PurchasePricingPolicy.CalculateBaseUnitCost` được gọi với `line.LineTotal` sau VAT.

## COST-003 — Freight/additional-cost capitalization

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Current cost luôn cộng allocated freight.
- **Decision:** Freight mặc định lưu riêng và không cộng giá vốn; toggle capitalize cost mặc định tắt. Khi bật, hệ thống đề xuất phân bổ theo giá trị line và manager được sửa.
- **Consequences:** Persist toggle/allocation basis/audit; validate allocation total; không thay đổi historical confirmed costs khi bổ sung XML.
- **Source:** Coordinator locked business rules 35–36.
- **Evidence:** Current `PurchasePricingPolicy` auto allocates và always includes freight in base cost.

## COST-004 — Price variance warning

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Giá nhập thay đổi cần được nhìn thấy.
- **Decision:** Bất kỳ thay đổi giá so với lần nhập gần nhất đều phải cảnh báo rõ. Không khóa yêu cầu bắt buộc nhập reason chỉ vì có price variance.
- **Consequences:** Warning phải là contract UI/server phù hợp; không tự thêm reason-required rule.
- **Source:** Coordinator correction and locked business rule 33.
- **Evidence:** Current UI chỉ hiển thị variance informational.

## COST-005 — Preserve payable regression, no AP expansion

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Confirm hiện tạo `PurchasePayable`, nhưng công nợ nhà cung cấp là phạm vi riêng.
- **Decision:** Giữ regression payable hiện có trong R2; không mở rộng accounts payable trong các task receipt R2.
- **Consequences:** Cost/Confirm changes phải không phá payable identity/atomicity; feature AP mới để task sau.
- **Source:** Coordinator locked business rule 24.
- **Evidence:** `StockDocumentService.CreatePayablesIfNeededAsync`, `PurchasePostingIdentity`.

## XML-001 — Post-confirm XML

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Hóa đơn điện tử có thể đến sau khi hàng đã Confirm.
- **Decision:** Cho Confirm không có XML và cho bổ sung XML sau Confirm; reconciliation có thể mang trạng thái `Waiting XML`. Bổ sung XML sau Confirm không tự thay đổi stock, quantity, price, cost hoặc tạo lại movement.
- **Consequences:** XML reconciliation tách khỏi posting; có thể cần waiting/incomplete reconciliation status.
- **Source:** Coordinator locked business rules 25–26.
- **Evidence:** Current XML upload không gọi inventory posting và không có status restriction.

## XML-002 — Invoice/receipt many-to-many

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Một invoice có thể bao nhiều receipt và ngược lại.
- **Decision:** Lưu invoice một lần và liên kết M:N với receipt.
- **Consequences:** Giữ/hoàn thiện `StockDocumentInputInvoiceMap`; mọi link phải store-safe và duplicate-safe.
- **Source:** Coordinator locked business rule 27.
- **Evidence:** `InputInvoiceHead`, `StockDocumentInputInvoiceMap`.

## XML-003 — Invoice duplicate identity

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** File hash chỉ phát hiện cùng bytes; business duplicate có thể khác serialization.
- **Decision:** Business identity là supplier tax code + invoice series + invoice number + invoice date. `XmlHash` là optional additional guard cho cùng file.
- **Consequences:** Cần normalized fields/unique strategy và migration data audit; current indexes non-unique và thiếu date.
- **Source:** Coordinator locked XML decision.
- **Evidence:** `InputInvoiceHeadConfiguration` current indexes.

## XML-004 — Parse detail lines and persistent product mapping

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Header totals không đủ đối chiếu/mapping hàng.
- **Decision:** XML phải đọc detail lines. Mapping persist theo supplier + XML item code + XML unit + GaoApp variant/conversion.
- **Consequences:** Current `InputInvoiceDetail` cần item code; mapping entity/index/tenant rules là Level C.
- **Source:** Coordinator locked business rules 28–29.
- **Evidence:** Current parser reads `HHDVu` but detail stores name/unit only.

## XML-005 — Missing item code and mapping confidence

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Một số XML thiếu item code hoặc tên thay đổi.
- **Decision:** Nếu không có XML item code, chỉ gợi ý theo tên và manager phải xác nhận lần đầu. Auto mapping tương lai chỉ dùng supplier + item code + unit. Tên/giá bất thường phải cảnh báo.
- **Consequences:** Không auto-match bằng tên; mapping confirmation/audit required.
- **Source:** Coordinator locked business rules 30 and mapping clarification.
- **Evidence:** Current source chưa có mapping entity/item code.

## XML-006 — Supplier resolution from XML

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** XML có seller tax code; Draft receipt có thể chưa có supplier.
- **Decision:** Trước Confirm, XML supplier có thể thay `SupplierId` chỉ khi tax code xác định duy nhất trong cùng store. Không đổi khi absent, ambiguous hoặc cross-tenant.
- **Consequences:** Supplier tax-code lookup phải store scoped; duplicate handling và audit bắt buộc.
- **Source:** Coordinator locked business rule 31.
- **Evidence:** Current `Supplier.TaxCode` nonunique; source chưa có XML supplier resolver.

## XML-007 — Reconciliation exceptions

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** XML totals/lines có thể lệch receipt.
- **Decision:** Manager được Confirm khi totals lệch nhưng phải ghi reason và giữ incomplete reconciliation status. Manager/Admin được ignore unmapped XML line với reason. Receipt line chưa resolve product thật luôn block Confirm.
- **Consequences:** Override/audit fields và status cần persist; XML mismatch không tự sửa stock/cost.
- **Source:** Coordinator locked business rules 32 and clarification.
- **Evidence:** Current match statuses không có approval reason/incomplete reconciliation workflow.

## XML-008 — Buyer LegalEntity resolution and invoice/receipt invariant

- **Date:** 2026-07-31
- **Status:** Accepted
- **Context:** XML có hai business identities khác nhau: seller tax code cho supplier và buyer tax code cho legal owner. Current parser lưu cả hai nhưng chưa resolve buyer sang LegalEntity.
- **Decision:** Seller tax code resolve `Supplier`; buyer tax code resolve `LegalEntity`, luôn trong cùng Store. Chỉ auto-resolve khi buyer tax code khớp đúng một LegalEntity; absent/ambiguous phải do manager xử lý. Invoice LegalEntity phải bằng receipt LegalEntity được derive từ warehouse. Mismatch là hard blocker: không được reason-override, Confirm hoặc tạo link hợp lệ.
- **Consequences:** Nếu XML trước Confirm xác định một LegalEntity khác, warehouse cũ invalid và manager phải chọn warehouse thuộc LegalEntity mới. Resolution/change phải audit. XML bổ sung sau Confirm không được đổi receipt legal entity, warehouse, stock, price hoặc cost.
- **Source:** Coordinator amendment R2.0-A2-CA1.
- **Evidence:** `InputInvoiceXmlService.ParseXmlToEntity` parse `NBan/MST` và `NMua/MST`; `InputInvoiceHead.BuyerTaxCode`; current upload/view DTO và service không có buyer LegalEntity resolver/guard.

## DATA-001 — Durable Confirm idempotency and concurrency

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** App-level `ExistsAsync` không đủ ngăn double posting đồng thời.
- **Decision:** Confirm và toàn bộ movement/valuation/cost/balance/PO/payable effects phải chạy trong một transaction. Confirm phải bảo đảm database-durable idempotency; hai người Confirm đồng thời chỉ một path tạo movement/cost/balance effects.
- **Consequences:** F03 và F12 là dependency Level C trước feature expansion; cần unique invariant/retry/concurrency tests.
- **Source:** Coordinator locked business rule 37.
- **Evidence:** `InventoryMovementService.CreateAsync`, `InventoryTransactionConfiguration`, `InventoryBalanceRepository.GetOrCreateAsync`.

## DATA-002 — Tenant-safe XML line mapping

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** `InputInvoiceDetail` không store scoped trực tiếp.
- **Decision:** Mọi detail mapping phải chứng minh detail thuộc invoice/link/receipt trong cùng Store và cùng LegalEntity; receipt LegalEntity được derive từ warehouse. Lookup ID đơn lẻ không đủ và không cho cross-LegalEntity mapping.
- **Consequences:** F02 là security/data blocker cần xử lý ngay sau docs; buyer LegalEntity resolution đầy đủ tiếp tục được theo dõi ở F13.
- **Source:** Coordinator security constraint.
- **Evidence:** `InputInvoiceRepository.GetInputInvoiceDetailAsync(int id)` và `InputInvoiceXmlService.UpdateLineMapAsync`.

## AUDIT-001 — Audit important overrides

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** R2 cho manager thực hiện các override có ảnh hưởng supplier, warehouse, quantity acceptance, cost và reconciliation.
- **Decision:** Return-for-revision, đổi supplier, đổi warehouse, chấp nhận overdelivery, sửa cost allocation, chấp nhận discrepancy và ignore XML line phải lưu actor, time và reason/note khi rule yêu cầu.
- **Consequences:** Header timestamps đơn lẻ không đủ; task audit phải định nghĩa event/field evidence và tenant-safe access.
- **Source:** Coordinator locked correctness/audit rule.
- **Evidence:** Current `StockDocument` có workflow actors nhưng `StockDocumentLine` chưa implement `IAuditTrackedEntity`.

## SCOPE-001 — Lot/expiry excluded from R2

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** Lot/expiry mở rộng đáng kể inventory identity và movement.
- **Decision:** R2 không triển khai lot hoặc expiry.
- **Consequences:** Không thêm batch/expiry fields trong receipt/movement R2 tasks.
- **Source:** Coordinator locked business rule 23.
- **Evidence:** Current receipt/inventory entities không có R2 lot/expiry workflow.
