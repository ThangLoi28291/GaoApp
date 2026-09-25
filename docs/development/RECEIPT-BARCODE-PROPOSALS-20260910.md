# Receipt barcode proposals — 2026-09-10

## Contract

User approved implementing the discussed receiving workflow: an employee encountering an unknown manufacturer barcode chooses an existing variant and existing unit conversion (for example milk / pack / carton). A receipt manager explicitly approves adding the alias or rejects it. Rejection does not invalidate correctly recorded physical receipt quantities. Existing internal and primary barcodes are preserved. No product or conversion creation is part of this command.

Level C. Base branch: `fix/r2-4-c1-invoice-identity-uniqueness`; base/expected parent: `db6a6c5e98b69472624eeb31aa6be2161576c3d2`. Existing dirty workspace changes are preserved. No commit, merge, deployment, live database access or unrelated inventory rewrite is authorized by this handoff.

## Behavior and decisions

- Shared “Mã hãng mới” panel and modal on WarehouseReceiving/Detail, StockDocumentManagement/Edit and PurchaseReceiving/Index. Unknown scan opens selection of an existing product/unit. A toolbar button also supports manual entry. Truly unknown products still use the existing provisional-item flow where enabled.
- Proposal persistence and physical quantity entry are separate explicit steps. Saving a proposal opens the existing quantity workflow; it never changes inventory or adds a receipt quantity implicitly. Cancelled quantity entry can leave a proposal; approval requires a positive receipt line with the matching variant, conversion/unit and factor. The manager can reject an orphan proposal.
- Before approval, exact aliases are scoped to their receipt, and only Pending proposals resolve. After approval, the active catalog barcode resolves on subsequent receipts. Exact active matches support alphanumeric Code 128 input as well as numeric barcodes. Leading zeroes are preserved; codes are 1–64 printable ASCII characters without whitespace, with no inferred or repaired check digit.
- Multiple codes for the same conversion are allowed. Duplicate Pending submissions within one receipt are idempotent. Pending use of the same code for a different conversion is rejected. A code already approved from another receipt for the same conversion can be acknowledged without adding another barcode.
- Direct receipts use Inventory.StockDocument.Update / Approve; PO receipts use Purchase.Receipt.Update / Approve. Read accepts the corresponding View, Update or Approve capability. Proposal mutation for PO receipts also requires the active owner and current lease token. New catalog grants are not automatically assigned to roles. Inline receipt review deliberately uses the receipt approver's permission; the existing barcode-normalization screen keeps its Catalog.Barcode.Update permission.
- Employees propose in Draft/Rejected. Inline receipt managers review in PendingApproval/Confirmed. Pending proposals do not block inventory confirmation and are never automatically approved by it. Decisions remain available on the receipt after confirmation. The existing catalog-normalization screen retains its broader Catalog.Barcode.Update authority to review conversions earlier and without a matching receipt line.
- Each proposal stores product/unit/factor, request actor/time/note, decision actor/time/note and created barcode ID. A newly assigned barcode adds an append-only barcode history entry. Approval marks it Supplier and non-primary; internal and primary barcode records remain unchanged.
- Same-store checks cover receipt, warehouse, legal entity, supplier, PO, product and unit. Changed conversion snapshots, inactive products/units, codes assigned elsewhere, disabled codes and historical reservations are rejected with an actionable message.

## Storage and concurrency

Reuses `ProductBarcodeVerificationRequests`, `ProductVariantUnitBarcode`, and `ProductVariantBarcodeHistory`. No migration or live database update. Existing request snapshots use three decimal places: conversions needing finer precision are explicitly rejected instead of silently rounded (pack/carton examples have integer factors).

Mutations use a database transaction and transaction-owned application lock per store. Receipt and conversion update locks protect review against simultaneous receiving/conversion changes. Barcode insertion, audit history and request resolution commit together. The active barcode unique index remains the final guard against concurrent catalog writers. Existing normalization approvals delegate to the same atomic resolver, eliminating its previous split-save behavior.

Legacy “missing barcode” discovery/submission is retained for compatibility. The new manufacturer-code workflow uses its own receipt-scoped API and is not subject to the legacy one-handled-request-per-conversion filter.

## Scope and validation

New application interface/DTO, Infrastructure receipt barcode service, receipt-scoped controller, shared Razor partial/JS, three receiving view/lookup integrations, normalization resolver delegation, DI registration, SQL integration tests and a Chrome probe. Inventory posting, purchase-order allocation, label printing and POS remain handled by their existing services.

Validation uses disposable LocalDB databases and temporary Web instances from `FullApplicationFixture`; never the configured store database. Commands:

```powershell
dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj --no-restore -c Release --artifacts-path Logs/label-printing-artifacts -v:q
dotnet vstest Logs/label-printing-artifacts/bin/GaoApp.Tests/release/GaoApp.Tests.dll '/TestCaseFilter:ReceiptBarcodeProposalSqlServerTests' '/logger:trx;LogFileName=receipt-barcode-proposals.trx' /ResultsDirectory:TestResults/receipt-barcode-proposals
dotnet Logs/label-printing-artifacts/bin/PosOffline.Browser/release/PosOffline.Browser.dll --receipt-barcode-proposals
```

Final verification:

- Isolated Release build: 0 errors. A full build reports 10 pre-existing warnings in unrelated tests.
- 31/31 tests passed: 6 dedicated SQL/HTTP scenarios plus 25 receiving UI/architecture/provisional/exception-boundary regression tests. Evidence: `TestResults/receipt-barcode-proposals/receipt-barcode-proposals.trx`.
- After the final UTC-kind serialization correction, the affected pack/carton scenario was rerun with explicit UTC assertions: 1/1 passed. Evidence: `TestResults/receipt-barcode-proposals/receipt-barcode-utc-verification.trx`.
- Real headless Google Chrome: employee unknown scan opens the modal; selecting Lốc displays 1 Lốc = 4 Hộp and the internal barcode; saving keeps the leading-zero manufacturer code; repeat scanning the pending alias opens quantity entry; adding 3 packs changes the physical receipt from 2 to 5 packs / 20 base units. Employee submits the receipt; a separate manager browser session approves the alias; active catalog lookup resolves the exact pack conversion. No JavaScript page errors.
- Desktop and 390-pixel mobile UI inspected. Evidence: `TestResults/receipt-barcode-proposals/browser/employee-select-pack.png`, `employee-mobile.png`, `manager-review.png`.
- SQL assertions cover concurrent duplicate proposal/review, multiple aliases per conversion, internal/primary preservation, barcode history, rejection followed by successful commercial confirmation and correct stock, tenant/permission/CSRF guards, PO ownership/lease, stale factors, orphan proposals, catalog conflicts and legacy normalization compatibility.
- JavaScript syntax checks and whitespace diff checks passed. Physical multi-device LAN deployment and scanner hardware are not part of these tests. Chrome exercised keyboard input equivalent to barcode-scanner input.

## Rollback

Revert only this task's application/controller/view/JS changes as a reviewed patch; preserve pre-existing workspace edits. No schema rollback. Persisted pending requests and approved barcode/history rows should be retained, not deleted. An erroneous approved alias should be deactivated through the existing catalog management workflow with audit history.

## Handoff

**READY FOR COORDINATOR REVIEW.** No independent review or GitHub CI pass is claimed. Existing dirty worktree retained; no commit or deployment. Build/run the updated app and reload the receipt page to use the feature; no new migration is required.

## Revision B — Product selection and unit cards

User requested that the new-code selector look like the existing quantity dialog (reference image 2). Search now lists each variant once. Selecting a product loads its complete active unit set and shows its catalog image (with a missing-image fallback), product name, existing barcode, chosen unit, factor and equivalent base-unit quantity. Hộp/Lốc/Thùng are separate buttons with a visible selection. Multiple-unit products require an explicit unit choice; a sole valid unit is selected automatically.

The receipt-scoped `products/{variantId}/units` read endpoint uses the same source-specific Update/Approve permissions as product search and filters store, active product/conversion/unit. It loads all available units even if autocomplete was truncated or matched one exact barcode. Changing products invalidates any outstanding unit response and clears the old selection. The proposal still proceeds into the existing quantity-entry workflow.

Scoped modal CSS supports desktop and a scrollable mobile layout. Validation extends existing SQL/HTTP tests with complete-unit lookup and access checks, and the Chrome probe with a single product result, three unit cards, carton-to-pack switching, responsive layout, persisted pack choice and the existing quantity/review flow.

Revision B verification: isolated Release build succeeds; 6/6 dedicated SQL/HTTP tests pass (`TestResults/receipt-barcode-proposals/receipt-barcode-unit-cards.trx`); final Chrome probe passes with no page errors. Desktop and 390px mobile screenshots were inspected, including the footer width and no horizontal content overflow. Screenshots: `employee-select-pack.png` and `unit-cards-mobile.png` under `TestResults/receipt-barcode-proposals/browser/`. JavaScript syntax checks pass. **READY FOR COORDINATOR REVIEW**, without commit, deployment or schema changes.

## Revision C — Receiving submission RowVersion fix

User reported “RowVersion không hợp lệ.” in the employee's submit-for-approval modal. Reproduced in real Chrome against a disposable SQL fixture: a Base64 version containing `+` (`AAAAAAAAC+A=`) was rendered by Razor as the literal JavaScript string `AAAAAAAAC&#x2B;A=`. Both WarehouseReceiving/Detail and StockDocumentManagement/Edit now emit the version through `Json.Serialize`.

Warehouse receiving also retained the initial page version after adding, editing or deleting lines. The lines partial now carries its document ID and version as HTML data attributes (including when empty). The client validates that metadata and replaces the visible lines and version together. Older overlapping refresh responses cannot replace a newer snapshot. Submission never fetches a fresh version on its own; the server's existing rejection of unseen concurrent changes remains in place.

The confirmation button waits for in-flight line saves and their refresh. A failed save blocks submission until a successful retry; duplicate in-flight submission is guarded. Source scope is the two existing Razor pages, receiving lines partial and receiving JS, plus the existing isolated browser harness and this documentation. Server concurrency checks, permissions, receipt posting and schema are unchanged.

Validation on 2026-09-10:

- Isolated Release build: 0 errors / 0 warnings on this incremental build.
- Real Google Chrome regression passed: exact Base64 in both views; actual UI add/delete and quantity blur autosave; deliberately delayed save disables confirmation; failed save blocks and retry recovers; another browser's manager edit produces the expected concurrency conflict; another locally saved edit refreshes the snapshot and the actual “Gửi quản lý duyệt” button succeeds without page reload. The existing manufacturer-alias approval flow also passes with no JavaScript page errors.
- The earlier Chrome probe submitted via API using a freshly fetched version and therefore did not cover this defect. Revision C replaces that shortcut with the real employee modal and checks the transmitted version. Its fixture deterministically creates a `+` version only in its disposable database. No configured store data is accessed.
- 31/31 existing receiving UI, approval UX, architecture, provisional and exception-boundary regression tests passed. Evidence: `TestResults/receipt-barcode-proposals/receiving-rowversion-regressions.trx`.
- JavaScript syntax and scoped whitespace checks passed. Browser evidence: `receiving-concurrent-edit.png` and `manager-review.png` under `TestResults/receipt-barcode-proposals/browser/`; the manager screenshot shows the receipt in Chờ duyệt with its submit audit event. `receiving-submitted.png` captures the redirect while the list is loading, not the final list contents.

**READY FOR COORDINATOR REVIEW.** No commit, deployment, migration or live database changes. Build/run the updated application and reload the receipt page to load the corrected scripts. Rollback is limited to this revision's view/JS changes and test additions; retain earlier barcode work and receipt data.

## Revision D — Autofocus manufacturer-code entry (2026-09-10)

User requested immediate typing in the new-code modal. After Bootstrap's `shown.bs.modal` event, a prefilled scanned code opens the product selector and focuses its native search input. Opening “Bổ sung mã hãng” without a code focuses the code field first; Enter on a nonempty code opens product search. The selector closes when the modal is dismissed, so subsequent openings start cleanly. Focus is applied after the modal animation/focus trap, without a delayed timer that could steal focus later.

Scope: shared receipt barcode JavaScript, the existing Chrome probe interaction and documentation. The probe's previous manual click on product search is replaced by waiting for focus and typing through the keyboard. No new test suite, API, permission, quantity, barcode persistence or schema changes. Existing data and prior work are preserved.

Verification: JavaScript syntax and scoped whitespace checks pass. The real Google Chrome probe passes against disposable SQL, including typing the product name directly after the scanned-code modal opens, selecting a unit, saving the proposal, quantity entry, submission and separate manager review. No JavaScript page errors. This static-script change does not require a server assembly rebuild; reload the receipt page to receive its versioned script.

Rollback: revert only this revision's focus handlers and browser interaction change. No database rollback. **READY FOR COORDINATOR REVIEW**, without commit or deployment.
