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
- **Implementation note (2026-08-03):** R2.0-C2 đã hoàn tất foundation cho dependency này; DATA-003 ghi contract durable identity/pre-lock hiện hành. DATA-001 được giữ nguyên như quyết định gốc và không có nghĩa mọi concurrency work tương lai đã hoàn tất.

## DATA-002 — Tenant-safe XML line mapping

- **Date:** 2026-07-30
- **Status:** Accepted
- **Context:** `InputInvoiceDetail` không store scoped trực tiếp.
- **Decision:** Mọi detail mapping phải chứng minh detail thuộc invoice/link/receipt trong cùng Store và cùng LegalEntity; receipt LegalEntity được derive từ warehouse. Lookup ID đơn lẻ không đủ và không cho cross-LegalEntity mapping.
- **Consequences:** F02 là security/data blocker cần xử lý ngay sau docs; buyer LegalEntity resolution đầy đủ tiếp tục được theo dõi ở F13.
- **Source:** Coordinator security constraint.
- **Evidence:** `InputInvoiceRepository.GetInputInvoiceDetailAsync(int id)` và `InputInvoiceXmlService.UpdateLineMapAsync`.
- **Implementation note (2026-08-03):** R2.0-C1 đã thay lookup ID đơn lẻ bằng `GetInputInvoiceDetailAsync(storeId, stockDocumentId, stockDocumentLineId, inputInvoiceDetailId)` với Store/receipt/line/LegalEntity ownership guard và no-mutation failure. F02 đã Closed; buyer-tax-code LegalEntity resolution vẫn là SD1-F13 Open.

## DATA-003 — Canonical durable inventory posting identity and balance pre-lock

- **Date:** 2026-08-03
- **Status:** Accepted
- **Context:** Historical SD1-F03/SD1-F12 showed that application-only duplicate lookup and query-then-add balance creation could not provide a durable concurrency invariant. R2.0-C2 introduced a database-backed movement identity, an atomic posting coordinator and operation-wide balance locking; the long-lived contract must distinguish this implemented foundation from future R2 feature work.
- **Decision:** The accepted contract is:

  1. New durable inventory movement requests use a deterministic SHA-256 idempotency identity produced by `InventoryIdempotencyKeyFactory`; callers must preserve the existing document/line/reference subkeys that feed that identity.
  2. Database unique protection is `StoreId + IdempotencyKey` for rows with a non-null key that are active/non-deleted.
  3. Historical rows may retain null keys. Migration `20260801110856_AddInventoryPostingIdempotency` does not invent/backfill keys and does not add a default.
  4. Multi-key callers materialize the complete operation key set before the first inventory/balance mutation.
  5. Balance keys lock in canonical `StoreId → WarehouseId → ProductVariantId` order after deduplication and Store/warehouse validation.
  6. Canonical lock order does not change business posting order, allocation order or durable reference/subkey identity.
  7. A non-empty `PreLockBalancesAsync` call requires an existing caller-owned active transaction; empty input is a no-op.
  8. Pre-lock does not start a transaction and does not commit/rollback. The outer caller owns those boundaries; a standalone single `CreateAsync` may use `InventoryPostingTransactionCoordinator` to own its transaction.
  9. No global automatic retry/replay is introduced. Known SQL Server lock/unique conflicts are surfaced as concurrency failures for the workflow caller to handle.
  10. Complex caller behavior is protected with relational SQL Server evidence for persistence/concurrency, caller behavior tests for key orchestration and source/AST contracts only for narrowly locked structural invariants. InMemory/recording-fake evidence is not SQL concurrency proof.

- **Consequences:** The operational consequences are:

  - Future multi-key inventory callers must adopt operation-wide key materialization and cannot bypass the active-transaction requirement.
  - Reservation rebuild must lock the old+new key union once before releasing old reservations and applying the new plan.
  - Mixed Sales Return must classify each line and lock one LegalEntity+legacy union; `NoRestock` contributes no balance key/movement and partial LegalEntity evidence fails closed.
  - Callers must preserve existing business order, document/line identity, source valuation identity and reference subkeys even though locks are acquired canonically.
  - This decision establishes a foundation, not a claim that all future inventory concurrency or R2 feature expansion is complete.

- **Source:** `GaoApp.Domain/Entities/InventoryTransaction.cs`; `GaoApp.Application/Services/Inventory/InventoryIdempotencyKeyFactory.cs`; `GaoApp.Application/Services/Inventory/InventoryMovementService.cs`; `GaoApp.Application/DTOs/Inventory/InventoryPostingLockKey.cs`; `GaoApp.Application/Interfaces/Common/IInventoryPostingTransactionCoordinator.cs`; `GaoApp.Infrastructure/Data/InventoryPostingTransactionCoordinator.cs`; `GaoApp.Infrastructure/Repositories/Inventory/InventoryBalanceRepository.cs`; `GaoApp.Infrastructure/Data/Configurations/InventoryTransactionConfiguration.cs`; `GaoApp.Infrastructure/Migrations/20260801110856_AddInventoryPostingIdempotency.cs`; caller services documented in `GAOAPP-SOURCE-MAP.md`.
- **Evidence:** C2A final reviewed commit `d1b06dce5343808493ebb7d1b599e01a06ae5113`; C2B1 final reviewed commit `3c99f19131a8c1af0bbfeeba94eb286c0081686c`; C2B2 final reviewed commit `0a5b2cf5734fd69dfa1e709de3984222c1ee4158`; `InventoryMovementSqlServerConcurrencyTests`, `InventoryPostingMigrationTests`, `DatabaseSchemaManifestTests`, `InventoryNonPosPostingContractTests`, `InventoryPosPostingContractTests`, `InventoryReservationServiceTests`, `OrderLegalEntityFinalizeServiceTests`, `OrderLegalEntityReversalServiceTests`, `InventoryRevaluationPostingContractTests`. Coordinator confirmed C2B2 Independent Review PASS and GitHub required checks 2/2 Success on PR #8 head `0a5b2cf5734fd69dfa1e709de3984222c1ee4158`; PR #8 remained open and unmerged at documentation time.

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

## POS-OFFLINE-001 — Nhật ký tại quầy và xác nhận thu tiền thủ công

- **Date:** 2026-09-09
- **Status:** Implemented, locally verified; pending coordinator review and deployment.
- **Context:** Người dùng yêu cầu POS tiếp tục bán khi máy chủ LAN treo/mất kết nối, mỗi quầy quản lý đơn riêng và thu ngân tự xác nhận tiền QR đã nhận.
- **Decision:** Ghi giỏ và hàng đợi bền vững trên IndexedDB trước khi báo thành công. Chuẩn bị màn hình offline bằng Service Worker/HTTPS và dữ liệu theo cửa hàng/quầy/nhân viên/ca. Gửi lại bằng UUID ổn định; ghi thao tác và kết quả trong một SQL transaction có khóa và unique key. Service con tham gia transaction; rollback của service con ngăn commit; sinh hóa đơn diễn ra sau commit thật. So khớp tổng tiền và danh sách hàng khi nhận khoản thu/chốt đơn. Dữ liệu lệch được giữ để đối soát. QR cũ của ACB dùng cùng khóa và liên kết khoản thu, thêm nguồn audit `OfflineManual = 8` mà không đổi các giá trị enum cũ.
- **Consequences:** Quầy cần chuẩn bị trực tuyến trong ca đã mở, phiên offline 24 giờ; server vẫn kiểm tra quyền, ca, giá và tồn kho khi đồng bộ. Các nghiệp vụ cần dữ liệu chung vẫn trực tuyến. Không xóa hàng đợi hoặc retry ledger để rollback. Cần triển khai schema và assets cùng phiên bản; local verification không thay thế review/CI/UAT tại cửa hàng.
- **Evidence:** Contract/handoff `POS-OFFLINE-20260909.md`; 223 test .NET, 51 test JavaScript; Edge + Web + SQL LocalDB disposable xác minh mất phản hồi sau commit, mở lại POS offline, QR thủ công qua UI, đồng bộ đúng hai đơn/hai khoản thu và lỗi lưu bộ nhớ. Không deploy hoặc sửa DB đang hoạt động.

## LABEL-20260910 — Product label printing from receiving documents

- **Date:** 2026-09-10
- **Status:** Accepted
- **Source:** User discussion and final correction: individual label size and column count are independent; quantity mode is configured in each saved template; remaining proposed scope accepted.
- **Decision:** XP-420B via USB on Windows Server. Clients submit durable SQL print jobs; a separate Windows service serializes spool submissions per printer. Store-scoped templates/tasks/jobs and print/manage permissions. Base-unit barcode and current retail price (base conversion, variant, product fallback); received counts use the receipt's stored base quantity. Multiple-unit and fractional weighing workflows deferred.
- **Consequences:** Physical paper gaps/margins and printer offsets are configurable, not inferred from label size. No silent EAN check-digit changes or barcode stretching. Preview/printing use the same Windows raster; CODE128/EAN13/EAN8/CODE39 have barcode validation. EAN13 is the initial format for small labels. Job snapshots preserve the submitted content; later receipt/catalog changes require review before another print. Manual output acknowledgement is required because spool acceptance is not physical output confirmation. Reprints do not increase original progress; uncertain jobs are never automatically resent.
- **Evidence:** `PRODUCT-LABEL-PRINTING-20260910.md`, SQL/renderer/security tests, Chrome probe; Windows Server/USB hardware acceptance remains an installation step.

## LABEL-20260910-B — Selectable layouts and per-product automatic barcode

- **Date:** 2026-09-10
- **Status:** Accepted; implementation ready for coordinator review.
- **Source:** User requests several distinct label designs and EAN-13 detection per product with Code 128 fallback.
- **Decision:** Four raster layouts (standard, price-first, price-tag, framed) with actual-renderer thumbnails. Layout is independent of label dimensions, column count, field visibility and default quantity mode. Every new preview/save/job uses AUTO: 13 ASCII digits with a valid EAN-13 check digit select EAN13, otherwise CODE128. Preserve the entire original barcode, including leading zeroes. Reject empty/unsupported/overwide codes before enqueue; never manufacture a check digit or narrow below two printer dots per module.
- **Compatibility:** Supersedes the original manual symbology setting for new operations, including existing templates read from JSON. Old job payloads are never normalized or rewritten; explicit formats and the original standard raster remain supported. Missing layout means standard. New settings remain JSON; no migration or live database update. Deploy Web and label worker together.
- **Evidence:** Renderer decoding, mixed-symbology TSPL, SQL legacy-template/job preservation and Chrome gallery/persistence probe in PRODUCT-LABEL-PRINTING-20260910.md.

## LABEL-20260910-C — Separate staff printing and administrator configuration

- **Date:** 2026-09-10
- **Status:** Accepted; implementation ready for coordinator review.
- **Source:** User correction: staff should enter a dedicated printing area, while administrators configure templates and printers in a separate area.
- **Decision:** Independent menu entries and pages: /admin/label-printing (Print permission, receipt tasks/history) and /admin/label-printing-settings (Manage permission, template/printer configuration). Server-render only the relevant panels on each page. Staff choose saved templates/printers and can never edit their configuration. Shared template/printer lookups and product preview accept Print or Manage; task/job actions require Print, configuration mutations and settings page require Manage. A Manage-only account can configure without receiving Print authority.
- **Compatibility:** Existing receipt links, tasks, templates, barcodes, printer settings, job history and queue semantics remain valid. Menu seed adds the missing settings entry without full menu recovery. No migration or configured database changes. This revision supersedes the earlier four-tab combined workspace.
- **Evidence:** 62 focused SQL/renderer/menu tests pass; real Chrome admin and Print-only employee sessions verify separate menus/pages, staff configuration denial, queue/cancellation, layout persistence and responsive views. See PRODUCT-LABEL-PRINTING-20260910.md revision C.

## RECEIPT-BC-20260910 — Review manufacturer aliases inside the receipt

- **Date:** 2026-09-10
- **Source:** User explicitly approved the proposed employee/manager workflow for existing product variants with internal pack/carton barcodes.
- **Decision:** Employee selects the existing conversion for an unknown manufacturer code and continues normal quantity entry. Pending aliases resolve only within that receipt. Receipt approver adds an additional Supplier/non-primary barcode or rejects the proposal; rejection preserves physical lines and does not prevent normal receipt confirmation. No new variant, conversion, or replacement of an internal barcode.
- **Integrity:** Atomic barcode/request/history transaction, store/source permission checks, active PO owner/lease, exact conversion checks, conflict detection and duplicate-submit/review handling. Multiple aliases per conversion remain supported. Existing barcode-normalization approval uses the same atomic resolver.
- **Evidence:** 31/31 SQL and receiving regression tests pass; final UTC assertion rerun 1/1; Chrome employee quantity entry and separate manager review pass. See RECEIPT-BARCODE-PROPOSALS-20260910.md. READY FOR COORDINATOR REVIEW; no live database or deployment changes.
## RECEIPT-BC-20260910-B — Product picker with separate unit cards

- **Date:** 2026-09-10
- **Source:** User asks to make manufacturer-code selection resemble the existing receiving quantity dialog in reference image 2.
- **Decision:** Autocomplete lists each variant once. Selecting a product shows its image/name and loads all active units as Hộp/Lốc/Thùng cards with current barcode and factor. The chosen card is highlighted and updates the barcode, unit and equivalent base quantity. Multiple-unit products require an explicit choice.
- **Implementation:** Shared responsive modal and a receipt-scoped, tenant-filtered unit lookup using the existing Update/Approve permissions. Full unit retrieval does not depend on autocomplete limits or an exact single-barcode match. Stale responses are ignored when changing or closing product selection.
- **Validation:** Six SQL/HTTP barcode workflow tests pass, including full-unit retrieval and permission/tenant checks. Chrome checks grouped search, three unit cards, switching Carton to Pack, selected conversion persistence, quantity entry and manager approval. Evidence recorded in RECEIPT-BARCODE-PROPOSALS-20260910.md revision B.

## RECEIPT-BC-20260910-C — Correct receiving versions and wait for autosave

- **Date:** 2026-09-10
- **Source:** User reports “RowVersion không hợp lệ.” when submitting a receipt from the warehouse receiving modal.
- **Decision:** Serialize inline versions as JSON; return version metadata with the visible lines partial after every successful add/edit/delete. Do not fetch a fresh token only at submission, since doing so would conceal unseen concurrent edits. Wait for pending line saves and block confirmation after a save failure until retry succeeds.
- **Evidence:** Chrome reproduced literal `&#x2B;` in a Base64 version before the fix. Updated Chrome probe passes the actual submit button, delayed/failed autosave, successful retry, add/delete and a second-client conflict, using isolated SQL data. Build and 31 receiving regression tests pass. Details in RECEIPT-BARCODE-PROPOSALS-20260910.md revision C.
- **Status:** READY FOR COORDINATOR REVIEW; no commit, deployment, schema or configured store database changes.

## LABEL-20260910-D — Show receipt label actions only during manager review

- **Date:** 2026-09-10
- **Source:** User asks to show the label toolbar only to managers in pending approval and hide it from employee receiving.
- **Decision:** Remove the toolbar and its script from both direct and PO receiving entry screens. Render it on StockDocumentManagement/Edit only for PendingApproval plus the appropriate receipt approval permission; the existing Print permission remains required. The separate staff printing workspace remains available under its own permission.
- **Scope:** Receipt presentation only; no new backend authorization policy, schema, queue, printer or data changes. Existing post-approval printing prompt remains available from the pending manager page.
- **Evidence:** Isolated build succeeds and 31 existing receiving regression checks pass. Browser-probe interaction adjustment and validation are recorded in PRODUCT-LABEL-PRINTING-20260910.md revision D. READY FOR COORDINATOR REVIEW.

## CUSTOMER-DISPLAY-20260910 — Redesign the customer-facing checkout

- **Date:** 2026-09-10
- **Source:** User requested a completely renewed, attractive and professional `/admin/pos/customer-display`, with online design research.
- **Decision:** Original forest/ivory/lime visual system inspired by documented Shopify and Lightspeed customer-display patterns. Separate welcome/promotional, itemized cart, cash, QR, partial-payment acknowledgment and finalized thank-you states. Use existing configured store identity, actual catalog images, unchanged server monetary fields and terminal SignalR events. Keep all cart lines accessible through scrolling; suppress animation/automatic scrolling with reduced-motion preference.
- **Integrity:** Presentation only; no new financial commands, payment policy, role grants, bank integration or schema changes. Failed reads retain the last displayed amounts with a status indicator. The existing promotion workflow remains, with failed-media fallback and cleanup on leaving idle.
- **Evidence:** Release build and 27 POS regression checks pass. Real Chrome/Razor/SignalR probe uses disposable SQL and controlled cart/promotion/QR data to verify display states, reset, interrupted reads, safe text, long carts and landscape/portrait layouts. Source references, exact files, scope and limitations are recorded in POS-CUSTOMER-DISPLAY-20260910.md.
- **Status:** READY FOR COORDINATOR REVIEW; no commit, production deployment or configured store database changes.

## CUSTOMER-DISPLAY-20260910-B — Keep promotions visible during checkout

- **Date:** 2026-09-10
- **Source:** User requests advertisements shrinking to one side while customers follow the sale, as seen in supermarkets.
- **Decision:** Active promotions use a 32% rail alongside cart lines and fixed totals. Keep one mounted image/video player across idle/cart transitions and continue duration rotation/countdown during selling. Expand fullscreen promotions only in idle. Without usable promotions, restore the latest-product panel. On wide displays, retain the rail during cash/QR/success; below 1200px payment uses the whole workspace, and below 700px the cart gets a compact ad strip above it.
- **Integrity:** Same active-promotion feed and server-only real-time notification contract. Unchanged updates/feed failures preserve current media; obsolete responses are ignored. No sale, amount, payment, permission or schema changes.
- **Evidence:** Release build, 27 POS regression checks and real Chrome/Razor/SignalR probe pass. Checks include seven cart sizes, eight QR sizes, live promotion updates/removal, feed failures, rotation/countdown, synthetic video playback without remounting across idle/cart/reset, and failed-media fallback. Reviewed screenshots and exact scope are in POS-CUSTOMER-DISPLAY-20260910.md Revision B.
- **Status:** READY FOR COORDINATOR REVIEW; no commit, deployment or configured store database changes.

## CUSTOMER-DISPLAY-20260910-C — Emphasize the latest updated product

- **Date:** 2026-09-10
- **Source:** User requests stronger visual emphasis for the just-selected product.
- **Decision:** Style the existing changed cart row as a pale-lime card with an outline, dark leading edge, larger thumbnail, bolder/larger name and amount, and a contrasting update label that remains visible on phones. Preserve existing latest-row ordering and update detection. Single entry animation respects reduced-motion preferences.
- **Scope:** CSS and handoff documentation only; no application logic, amount or data changes.
- **Evidence:** Release build and unchanged Chrome customer-display probe pass. Desktop, compact and phone screenshots with/without advertisements were inspected; details in POS-CUSTOMER-DISPLAY-20260910.md Revision C.
- **Status:** READY FOR COORDINATOR REVIEW; no commit or deployment.

## CUSTOMER-DISPLAY-20260910-D — Preserve the product card when ads are active

- **Date:** 2026-09-10
- **Source:** User clarifies that advertisements must not replace the separate current-product panel.
- **Decision:** Supersede Revision B's replacement layout. Show the product card, cart/totals and ads together: three columns at 1400px+, product above ads beside the cart on smaller monitors, and compact stacked sections on phones. Keep the image, name, quantity, unit price and line total visible. Existing latest-row emphasis and continuous media playback remain.
- **Scope:** CSS, existing browser probe assertions and documentation. No markup duplication, media reparenting, runtime JavaScript or backend changes.
- **Evidence:** Release build and extended Chrome/Razor/SignalR probe pass. All product details fit inside their card at seven cart sizes without overlapping ads/cart; QR checks pass at eight sizes. Responsive and product-update screenshots were inspected. See POS-CUSTOMER-DISPLAY-20260910.md Revision D.
- **Status:** READY FOR COORDINATOR REVIEW; no commit, deployment or configured store data changes.

## CUSTOMER-DISPLAY-20260910-E — Remove decorative copy and increase contrast

- **Date:** 2026-09-10
- **Source:** User requests a less pale display and only essential information, explicitly removing the numbered welcome instruction and cart/payment/completed steps.
- **Decision:** Remove steps, generic slogans, redundant labels and decorative captions. Keep store/terminal/time, connectivity, customer/product/transaction details, advertisements and useful QR instructions. Use a stronger gray-green background, white panels, darker secondary text and a clearer current-product highlight. Remove obsolete markup CSS and the unused journey helper.
- **Scope:** CustomerDisplay Razor/CSS/JS and documentation only. Preserve Revision D's product/cart/ad layout, current product data and continuous media behavior. No financial, permission, schema or advertisement-setting changes.
- **Evidence:** Release build and unchanged Chrome/Razor/SignalR probe pass; inspected idle, cart, phone and QR screenshots. Selected system text/background pairs have measured contrast from 4.86:1 to 10.09:1; user-configured advertising colors are outside this measurement. See POS-CUSTOMER-DISPLAY-20260910.md Revision E.
- **Status:** READY FOR COORDINATOR REVIEW; no commit, deployment or configured store data changes.

## POS-ORDERS-PAGE-20260910 — Redesign the order management workspace

- **Date:** 2026-09-10
- **Source:** User requests a complete redesign of `/admin/pos/orders-page` with clearer layout/colors and modern interactions.
- **Decision:** Replace the eleven-column table and stacked actions with six grouped columns, clear status/amount presentation, page-scoped monetary summaries, URL-restored filters, responsive order cards, a read-only receipt drawer and a compact keyboard-accessible action popover. Preserve full-detail ownership of refund/void, existing print URL and the ACB launcher.
- **Scope:** Orders Razor/CSS/JS, an isolated Chrome probe/runner dispatch and documentation. Existing API permissions, financial commands, controllers, shared layouts and database schema remain unchanged.
- **Evidence:** Final Release build succeeded; Chrome passed seven viewport sizes, real fixture login/empty API, controlled list/receipt workflows, cancellation, filters/paging, error/retry, keyboard/focus and action destination checks. Screenshots visually reviewed; only disposable SQL/Web and synthetic orders were used. Physical printing, bank operations and actual refunds are outside these presentation checks. See POS-ORDERS-PAGE-20260910.md.
- **Status:** READY FOR COORDINATOR REVIEW; no commit, deployment or configured store data changes.

## POS-ORDERS-PAGE-20260910-B — Shared theme and double-click preview

- **Date:** 2026-09-10
- **Source:** User approves the orders layout and asks for double-click quick preview and colors matching other POS pages, referencing shift history.
- **Decision:** Retain the layout, use inherited GaoApp theme colors and the shared POS navigation styling, and reserve green/amber/red for statuses. Double-clicking row content opens one receipt; nested actions keep their own behavior. Preserve single-click/keyboard code preview and prevent the second click from immediately dismissing its drawer.
- **Evidence:** Release build and extended Chrome probe passed, including real double-click gestures, one receipt request, print/action exclusion, dialog focus restoration and the existing seven viewport/functional scenarios. Updated desktop/phone screenshots reviewed; disposable fixture only. See POS-ORDERS-PAGE-20260910.md Revision B.
- **Status:** READY FOR COORDINATOR REVIEW; no backend, financial, shared stylesheet, database, commit or deployment changes.

## POS-ORDERS-PAGE-20260910-C — Product name and code in quick preview

- **Date:** 2026-09-10
- **Source:** User requests larger prominent product names and codes on the small secondary line.
- **Decision:** Use a scoped 16px bold product name and replace repeated variant names with the recorded scanned barcode, falling back to barcode then SKU. Preserve string identifiers, amounts, quantities and shared colors.
- **Evidence:** Syntax/whitespace checks, Release build and unchanged Chrome scenarios passed; fixture examples and desktop/phone screenshots verify code/leading-zero presentation and wrapping. See POS-ORDERS-PAGE-20260910.md Revision C.
- **Status:** READY FOR COORDINATOR REVIEW; no backend, real-store data, financial, commit or deployment changes.

## XML-INPUT-STOCK-20260910 — Documentary XML balances and source history

- **Source:** User confirms stock decreases on electronic invoice issuance and requests a view following the existing inventory ledger.
- **Decision:** Add `/admin/invoice-input-stock` with current balances and chronological source movements, shared admin colors, filters and double-click preview. Use the same source projection for issuance preflight. Confirmed mapped receipt evidence is capped by converted XML quantity and mapped receipt quantity; an XML detail shared by receipts is allocated once globally in recognition order. Boolean-only flags provide no quantity. Issuing/uncertain failures hold quantity; successful issuance consumes; POS sales/drafts do neither. A product flag change cannot erase previous invoice consumption.
- **Boundaries:** Read-only projection of currently valid source documents, not an immutable mapping-edit audit ledger. No physical inventory postings, schema/migration, backfill or real-store writes. Existing correction/return workflows retained. Queries are scoped to Store and warehouse legal owner; the existing issuance store lock is retained.
- **Evidence:** See `XML-INPUT-STOCK-20260910.md` for exact files, final build/test/browser commands, limitations and rollback snapshots.
- **Status:** READY FOR COORDINATOR REVIEW; no commit, push, deployment or independent-review claim.

## DELIVERY-20261006-A — Web delivery before Android and GPS

- **ID:** DELIVERY-20261006-A
- **Date:** 2026-10-06
- **Status:** Accepted
- **Context:** User agrees to operate delivery with simple milestone updates before using a phone app or continuous location tracking.
- **Decision:** Deliver the complete in-store Web workflow first: POS delivery bill with QR/readable code, picking and shortages, goods handover, assigned courier and departure, reported delivery outcomes/return, goods-and-money reconciliation, settlement at any authorized counter in the same store, customer shortage follow-up and the office monitor. Authorized staff can enter outcomes for the courier based on a phone report or when the courier returns; retain both the recording actor and assigned courier. Android and GPS are a separate later release.
- **Consequences:** Roadmap D00–D12 has independent Web acceptance and release gates. No Android SDK, APK, phone or GPS is required for those gates; QR lookup also supports typed order codes. M00–M03 remain deferred and untested. The office monitor shows recorded milestones, departure time, elapsed time and last update, without implying an observed live location. Charges cover delivered goods only, and the original stock/money/idempotency/tenant checks remain mandatory.
- **Source:** User confirms: “Chốt theo phương án này chia lại các bước thực hiện cho tôi”.
- **Evidence:** [Delivery roadmap](DELIVERY-ROADMAP-20261006.md), including phase-specific tests and both manual-update scenarios. This records an accepted plan; no delivery implementation, functional tests, schema/database changes, commit or deployment occurred in this documentation update.

## DELIVERY-20261006-B — Independent delivery and actual delivery settlement

- **Date/source:** 2026-10-06; user requests continuing D01 from the agreed roadmap. This permits model/design work; D00 physical device/printing verification remains pending.
- **Status:** Implemented D01 contract; local policy TEST PASS, READY FOR COORDINATOR REVIEW. No immutable-commit independent review/CI or runtime acceptance.
- **Decision:** Independent DeliveryOrder, stable code/version and immutable creator terminal/shift/cart/source warehouse/legal owner; atomically detach unpaid current POS cart in D03. Final sale belongs to authorized current counter/shift in the same Store, linked to original dispatch/cost fragments. Normal POS guards remain strict. Source shift may close before next-day settlement.
- **Goods/money:** Reserve actual picking, issue W1 at physical handover, phone reports do not receive stock or cash, accept actual good returns into W1, settle only actually delivered quantity without a second stock deduction. Track missing items separately with no receivable; supplements are new approved deliveries. Preserve FIFO fragment costs and precision; unapproved damage/loss and cash discrepancy block reconciliation.
- **Price/dates:** Freeze approved line amounts and deterministic proportional order-discount allocation, prorate on actual delivered quantity, round cash prices AwayFromZero to whole VND. Cost unit (18,6), fragment amount (18,4). Server UTC audit, UTC+7 operation dates; revenue on settlement date, movements on actual operation dates, money on settling shift; separately retain reported customer time.
- **Minimum release scope:** Cash, verified bank transfer without a new payment QR, or credit for the frozen eligible active HaveDebt customer. One method per settlement, no advance/prepayment. Mixed payments and advances were asked as optional extensions; no answer is treated as consent to add them. Conditional combo/buy-gift/voucher and additional tax/fee payloads must be explicitly rejected before cart transfer until supported, never silently dropped. Existing invoice tax workflow remains separate. These are implementation limits, not a claim the user separately approved every extension/exclusion.
- **Authorization/reporting:** Fresh Store/source/owner/capability checks; separate picker/courier/proxy/receipt/cash/bank/finalize authorities. Proxy retains actor, assigned courier, source and server time. Elapsed time and outcome reports never finalize money. No direct courier reassignment after departure or changing reports after result lock.
- **Consequences:** D02–D09 must implement durable constraints, command receipts/versions/locks, after-commit outbox, custody-to-sale costing/profit/returns adapters and integration tests. D01 policies do not prove SQL concurrency/rollback, Web endpoints or physical printing. No schema/data/config changes, commit or deployment in D01.
- **Evidence:** [D01 contract and handoff](DELIVERY-D01-20261006.md); 259/259 tests, 0 failed/skipped, isolated Release solution build and source manifest in Logs/delivery-roadmap/D01/run-20261006-01/. D02 remains unstarted.

## DELIVERY-D02-20261006 — Durable delivery foundation

- **Date/source:** 2026-10-06; user yêu cầu tiếp tục D02. Quyết định D01 giữ nguyên, dòng “unstarted” ở trên là lịch sử trước bước này.
- **Decision:** SQL Delivery aggregate độc lập POS; composite Store/origin FKs, rowversion, append-only revision/journal/dispatch fragment, normalized request hash + stored command outcome, transaction SQL locks và durable outbox/consumer receipts. Catalog 17 capability permissions, fresh membership/quyền/source checks. QR identifier không cấp quyền. Chưa có per-user warehouse ACL trong app; D02 dùng các kho/chủ thể active của Store sau module permission, không giả định ACL mới.
- **API/scope:** Authenticated list/detail/code-token lookup/history và recipient edit chỉ Created; whitelist + CSRF, actor/Store/time server. Foundation create chỉ server service trong transaction caller, chưa public creation/POS transfer. Journal/fragment chưa posting. Worker chỉ durable checkpoint, D10 dùng consumer riêng và nối monitor sau. Không gọi tiền/kho/receivable hoặc nới POS guards.
- **Quota:** HTTP tests phát hiện rate limiter gốc chạy trước authentication. Revision 2 dispatch riêng delivery lookup sau authorization để 30 lượt/phút phân theo Store Host/actor; các policies hiện hữu giữ vị trí cũ. Regression login/monitor/permissions đạt.
- **Recovery:** Migration thêm 8 Delivery tables/9 principal alternate keys/7 audit-origin triggers, không backfill POS. SQL fresh/old-data upgrade/downgrade/re-upgrade đạt; Down mất Delivery records, vận hành phải backup/forward fix và giữ idempotency/history/outbox, không tự downgrade dữ liệu thật. Recovery plan chờ independent review/CI trước release.
- **Evidence/status:** [D02 contract/handoff](DELIVERY-D02-20261006.md), 29/29 SQL/HTTP/concurrency/rollback/restart tests + 302/302 hồi quy; 0 fail/skip cuối. READY FOR COORDINATOR REVIEW, không commit/deploy hoặc migrate GaoAppDb; Web hiện tại chưa dùng D02. D03 và thiết bị/in D00 chưa hoàn thành.

## DELIVERY-D03-20261006 — POS chuyển giỏ và phiếu giao A5

- **User decision:** làm D03; máy code chưa có máy in giao nên chọn **A5 portrait**, browser print riêng với hóa đơn POS nhiệt. Không tạo QR chuyển khoản từ phiếu giao.
- **Implementation:** form người nhận trong thanh toán POS; server snapshot/version/fingerprint, fresh quyền/Store/ca/quầy/kho; một transaction D02 create/history/outbox, nhả tất cả reservation thực, nguồn Cancelled ghi rõ `[DELIVERY]`, giỏ rỗng mới và stored outer response. Chưa xuất/thu/nợ/điểm/hóa đơn. Same key có replay sau đóng ca/restart; actor/quầy/payload khác conflict. Browser giữ intent qua reload khi response mất, online-only, không queue hoặc in thành công trước xác nhận.
- **Source protection:** D03 migration chỉ 3 triggers nguồn Delivery Cancelled + EF trigger metadata, Down giữ POS/Delivery/history. Không thay POSService/payment/offline/auth middleware. Các thống kê ca đếm Cancelled vẫn bao gồm giỏ nguồn chuyển giao; khi chốt adapter báo cáo D08/D11 cần phân biệt ý nghĩa với hủy giỏ thông thường.
- **Bill/lookup:** Delivery.View và fresh source scope, dùng quầy khác không có ca; QR/token/mã tay cùng hồ sơ, A5/lề 10 mm/tiêu đề lặp/đơn dài, escaped HTML. Tên người/quầy tạo in lại được sau nghỉ hoạt động, không nới quyền actor hiện tại. Không QZ/thermal/drawer. D04 nhận soạn dùng hồ sơ này sau.
- **Evidence/status:** [D03 contract/handoff](DELIVERY-D03-20261006.md); D03 cuối 38/38 sau bố cục A5, 369/369 D01/D02/D03+security, 48/48 POS regression + 10/10 bank pending QR, hai browser/SQL đạt (D03 cuối 8 nhóm, có tab phụ không được tạo giao). A5 short1/long28lines3pages, QR decode, text trong lề, đã xem PDF renders. 28 file đúng allowlist, SQL fixture dọn và HEAD giữ nguyên. Không commit/deploy/migrate GaoAppDb. READY FOR COORDINATOR REVIEW/local auto gate; in giấy/đầu đọc và review độc lập/CI còn pending, không full D03/TASK PASS.
## DELIVERY-D03-RELEASE-20261006 — hoàn thiện bản phát hành

- User giao hoàn thành phần D03 còn lại sau lần kiểm tra trạng thái; giữ A5 và nghiệm thu thiết bị tại tiệm để sau khi có máy in/đầu đọc.
- Published smoke phát hiện catalog/preflight chưa nhận unique constraints/trigger D02–D03. Sửa verification theo định nghĩa chính xác, fingerprint và trạng thái trigger; không bỏ preflight hoặc whitelist SQL/trigger bất kỳ. SQL Server mở rộng năm CHECK của delivery/report; chỉ nhận cặp toàn biểu thức tương đương đã kiểm tra, test vẫn reject thay giới hạn, số học hoặc nhóm AND/OR. Nghiệp vụ và migrations không đổi.
- Preview máy code dùng bản phát hành tại localhost:7051 và GaoAppDb đã có migration; tắt migration/seed/bootstrap/outbox/dọn ảnh nền, không deploy tại tiệm. Handoff nguồn + release + SQL/browser auto pass; không nhận là independent review/GitHub CI/TASK PASS. Phạm vi 161 source files hiện có được đưa ra cho user chọn trước khi commit/push nhánh riêng và dùng agent review.
- Evidence: `DELIVERY-D03-20261006.md`, `Logs/delivery-roadmap/D03/run-20261006-02/`.
