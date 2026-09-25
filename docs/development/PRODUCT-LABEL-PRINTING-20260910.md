# Product labels — implementation contract and handoff

User-authorized scope (2026-09-10): receipt product labels on XP-420B printers attached by USB to Windows Server. Each saved template independently configures individual label width/height, columns, gaps, margins, visible fields, barcode symbology and default quantity mode (one/received). Base unit only; current retail base-unit price. Fractional received quantities and multi-unit labels are deferred; never round into label counts silently.

Level C. Baseline/expected parent: db6a6c5e98b69472624eeb31aa6be2161576c3d2 on fix/r2-4-c1-invoice-identity-uniqueness. Existing dirty workspace belongs to preceding work and is preserved. No commit/push/deploy/live database changes authorized by this implementation. The conversation locks functional scope; implementation, migrations, disposable SQL tests and build artifacts are necessary within that scope.

Scope: new ProductLabel entities/configurations, Infrastructure/Printing services, additive migration and snapshot, new LabelPrinting admin controller/view/assets, permission catalog/menu, receipt view/JS entry points and approval prompt, Windows label service project and installation documentation, focused tests. Shared changes limited to registration/project references and receipt printing entry points. Receipt approval, inventory posting, POS receipt rendering and existing data remain independent.

State: receipt -> unique label task -> versioned quantities -> immutable print job -> server spool submission -> manual actual-output acknowledgement -> explicit task completion. Receipt changes require refresh; old output history survives. Reprints require a reason and do not increment original progress. Job request UUID and unique active job per task prevent retries/concurrent operators producing duplicates. Uncertain delivery is never auto-replayed.

Verification: isolated Release build; SQL Server disposable LocalDB upgrade/model/security/idempotency/concurrency tests; renderer barcode/layout tests; Chrome UI using disposable test database. Physical XP-420B USB calibration and Windows Server service account/driver installation need hardware acceptance. No claim of independent review or CI acceptance.

Rollback: stop the label service first. Retain label tables and history when disabling UI/service; additive schema can remain. Do not downgrade/drop tables containing operational print history. No live migrations executed here.

Status: READY FOR COORDINATOR REVIEW.

## Implementation handoff — 2026-09-10

**Status: READY FOR COORDINATOR REVIEW.** Code and automated verification complete; no independent review/CI or physical printer acceptance claimed. Branch and HEAD unchanged from baseline. No commit, push, merge, live database migration, service registration or physical print was performed.

### Final behavior

- `/admin/label-printing` contains receipt tasks, saved templates, server printer configuration and print history. The feature menu is added on normal Development startup without resetting any other menus; production menu reconciliation includes the same entry.
- Width/height describe each individual label. Column count, gaps and margins are independent saved controls. Quantity mode is saved with the template; the first template choice on an unplanned receipt applies its defaults automatically. Staff can override counts and apply defaults again explicitly.
- Receipts can be queued before approval; both normal and commercial approval UI offer label printing after successful approval. Purchase workbench, quick receiving and manager receipt pages expose entry links. Receipt list/detail show print progress.
- Current base-unit retail price uses the same fallback order as POS: positive base conversion price, positive variant price, product base price. Base barcode comes only from active base-unit conversion barcodes. Receipt source hashes prevent stale quantities/prices from silently reaching a new job.
- SQL request IDs and unique active task-job constraints prevent duplicate printing requests. Immutable payloads preserve products, price, template and printer calibration. Manual confirmation records actual accepted label quantities; reprints preserve original progress. Completion is independent of inventory approval/posting.
- Standalone Windows service uses the existing database, with explicit StoreId, local Windows printers, RAW TSPL and Vietnamese bitmap text. It never migrates schema or automatically replays an uncertain Sending/NeedsAttention job. `--list-printers` is a read-only setup check.

### Task-created / modified source files

- `GaoApp.Domain/Entities/ProductLabelPrinting.cs`
- `GaoApp.Infrastructure/Data/Configurations/ProductLabelPrintingConfiguration.cs`
- `GaoApp.Infrastructure/Printing/ProductLabelDesign.cs`
- `GaoApp.Infrastructure/Printing/ProductLabelRenderer.cs`
- `GaoApp.Infrastructure/Printing/WindowsLabelPrinter.cs`
- `GaoApp.Infrastructure/Printing/LabelPrintDispatcher.cs`
- `GaoApp.Infrastructure/Data/Seed/ProductLabelMenuSeeder.cs`
- `GaoApp.Infrastructure/Data/Seed/AdminMenuSeeder.cs`
- `GaoApp.Infrastructure/GaoApp.Infrastructure.csproj`
- `GaoApp.Infrastructure/Migrations/20260910040620_AddProductLabelPrinting.cs`
- `GaoApp.Infrastructure/Migrations/20260910040620_AddProductLabelPrinting.Designer.cs`
- `GaoApp.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `GaoApp.Application/Common/Security/PermissionCodes.cs`
- `GaoApp.Application/Common/Security/PermissionCatalog.cs`
- `GaoApp.Web/Services/Printing/ProductLabelService.cs`
- `GaoApp.Web/Areas/Admin/Controllers/LabelPrintingController.cs`
- `GaoApp.Web/Areas/Admin/Views/LabelPrinting/Index.cshtml`
- `GaoApp.Web/Areas/Admin/Views/Shared/_ProductLabelReceiptLink.cshtml`
- `GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Index.cshtml`
- `GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml`
- `GaoApp.Web/Areas/Admin/Views/PurchaseReceiving/Index.cshtml`
- `GaoApp.Web/Areas/Admin/Views/WarehouseReceiving/Detail.cshtml`
- `GaoApp.Web/wwwroot/Admin/js/printing/label-printing.js`
- `GaoApp.Web/wwwroot/Admin/js/printing/label-receipt-links.js`
- `GaoApp.Web/wwwroot/Admin/css/label-printing.css`
- `GaoApp.Web/wwwroot/Admin/js/stock-document-management.js`
- `GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js`
- `GaoApp.Web/Program.cs`
- `GaoApp.Web/Configuration/DatabaseStartupExtensions.cs`
- `GaoApp.LabelPrintServer/GaoApp.LabelPrintServer.csproj`
- `GaoApp.LabelPrintServer/Program.cs`
- `GaoApp.LabelPrintServer/appsettings.json` (empty connection string, StoreId 0; deployment template only)
- `GaoApp.LabelPrintServer/README.md`
- `GaoApp.sln`
- `GaoApp.Tests/Printing/ProductLabelRendererTests.cs`
- `GaoApp.Tests/Security/ProductLabelSqlServerTests.cs`
- `GaoApp.Tests.Browser/Program.cs`
- `GaoApp.Tests.Browser/label-printing.browser.cjs`
- `docs/product-label-printing-upgrade.sql`
- this handoff and appended `docs/development/DECISION-LOG.md` entry LABEL-20260910.

Read-only discovery covered StockDocument/StockDocumentLine/Product/ProductVariant/ProductUnitConversion/ProductVariantUnitBarcode, POS retail-price resolver, receipt template service/controller, existing auth/tenant/audit context, receipt approval caller/callee, menu service/repository/startup seed, and reusable SQL/browser fixtures. No inventory posting/cost/approval service implementation changed.

### Validation evidence

- `dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj --no-restore -c Release --artifacts-path Logs/label-printing-artifacts -v:q` — exit 0; 0 errors, 10 existing test warnings (unrelated AcbInstallment nullability and existing xUnit style warnings).
- `dotnet build GaoApp.LabelPrintServer/GaoApp.LabelPrintServer.csproj --no-restore -c Release --artifacts-path Logs/label-printing-artifacts -v:q` — exit 0, 0 warnings/errors.
- `dotnet vstest Logs/label-printing-artifacts/bin/GaoApp.Tests/release/GaoApp.Tests.dll '/TestCaseFilter:FullyQualifiedName~ProductLabel|FullyQualifiedName~Current_model_and_migration_snapshot_have_no_differences|FullyQualifiedName~StockDocumentManagementIndexModernUiContractTests|FullyQualifiedName~PurchaseReceiptPriceVarianceUiContractTests|FullyQualifiedName~ReceiptTemplateSqlServerTests' '/logger:trx;LogFileName=product-labels-final.trx' /ResultsDirectory:TestResults/product-labels` — **30 passed, 0 failed, 0 skipped**, 64 seconds. Fresh, disposable SQL Server LocalDB databases only. Evidence: `TestResults/product-labels/product-labels-final.trx`.
- `dotnet Logs/label-printing-artifacts/bin/PosOffline.Browser/release/PosOffline.Browser.dll --label-printing` — exit 0. Real installed Google Chrome headless; real Web/SQL, only installed-printer discovery mocked; no print service was running. Verifies menu, independent columns including 50x30 with 2 columns, invalid 3-column width, stored quantity mode, printer configuration, preapproval receipt entry, automatic 48-base-unit defaults, queue/cancel, persistence, no page errors, 390px layout.
- Screenshots inspected: `TestResults/product-labels/browser/template-50x30-two-columns.png`, `receipt-queued.png`, `templates-mobile.png`.
- `dotnet Logs/label-printing-artifacts/bin/GaoApp.LabelPrintServer/release/GaoApp.LabelPrintServer.dll --list-printers` — exit 0; real Windows printer enumeration works on the development machine. XP-420B was not in this machine's printer list. No physical data sent.
- JavaScript syntax checks passed for both new JS files. `git diff --check` on edited shared files exited 0; only line-ending notices. Existing dirty workspace preserved.
- Migration generated against the prior model: only four new tables plus indexes/FKs. Upgrade from `20260910012000_AddStoreReceiptIdentity` preserves existing catalog data; model/snapshot test passes. Idempotent SQL generated for review only, not applied to configured DB.

### Remaining operational acceptance

Install/configure the Windows service on the actual server with an account allowed to access SQL and USB printers, then calibrate gap/margins and scan real labels on XP-420B. Test paper-out, unplug/reconnect, reboot and two actual clients before production use. Preview and fake transport tests do not certify physical output. Lists currently load the newest 500 receipt tasks / 200 print jobs; older tasks remain available from their source receipt. Server queues are durable; server unavailable means physical printing waits.

Rollback remains feature disable/service stop while retaining tables and history. No destructive downgrade is recommended after real printing begins. Commit remains pending separate authorization; no task-pass or independent-review assertion.

## Revision B — selectable layouts and automatic barcode (2026-09-10)

User-authorized extension: offer different visual designs and select EAN-13 only for a valid EAN-13 product value; otherwise Code 128. This revision supersedes the original manual symbology configuration for new operations. Same baseline/branch and dirty-workspace preservation; no new schema, dependencies, migration or configured database changes. Bounded Level B rendering/template extension. Status: **READY FOR COORDINATOR REVIEW**.

### Implementation

- Four real raster designs: Cân đối (standard), Giá nổi bật (price-first), Giá nền đen (price-tag), Khung thanh lịch (framed). Razor uses one canonical layout catalog; gallery images and full previews use the same renderer as physical TSPL rows.
- Switching style changes only the layout key. Width, height, columns, margins, field visibility and quantity mode persist. Saved templates and task selectors display the style; copy/save preserves independent named templates.
- AUTO detection checks 13 ASCII digits and the EAN-13 check digit; other printable ASCII codes use Code 128. Exact product values survive rendering/decoding, including initial zeroes. Empty, unsupported and physically overwide codes are rejected before queue insertion. Mixed EAN-13/Code 128 products are rendered independently even within one printed row.
- Existing template DTOs, new previews, template saves and new jobs use AUTO. No bulk rewrite of saved data; existing job JSON keeps its old format/layout, old explicit encoders remain available and missing layout renders the original standard design. Job retries still return the original snapshot.
- Web and Label Print Server must be updated together, since the worker reads layout/AUTO from the job snapshot. No physical print or service installation/restart was performed here.

### Files changed in this revision

- GaoApp.Infrastructure/Printing/ProductLabelDesign.cs
- GaoApp.Infrastructure/Printing/ProductLabelRenderer.cs
- GaoApp.Web/Services/Printing/ProductLabelService.cs
- GaoApp.Web/Areas/Admin/Controllers/LabelPrintingController.cs
- GaoApp.Web/Areas/Admin/Views/LabelPrinting/Index.cshtml
- GaoApp.Web/wwwroot/Admin/css/label-printing.css
- GaoApp.Web/wwwroot/Admin/js/printing/label-printing.js
- GaoApp.Tests/Printing/ProductLabelRendererTests.cs
- GaoApp.Tests/Security/ProductLabelSqlServerTests.cs
- GaoApp.Tests.Browser/label-printing.browser.cjs
- GaoApp.LabelPrintServer/README.md
- docs/development/DECISION-LOG.md and this handoff

### Verification

- Isolated Release Web/tests/browser build: exit 0, 0 errors and 10 pre-existing test warnings. Isolated label worker build: exit 0, 0 warnings/errors. Build commands remain as recorded above.
- `dotnet vstest Logs/label-printing-artifacts/bin/GaoApp.Tests/release/GaoApp.Tests.dll '/TestCaseFilter:FullyQualifiedName~ProductLabel|FullyQualifiedName~Current_model_and_migration_snapshot_have_no_differences' '/logger:trx;LogFileName=label-layouts-auto-final.trx' /ResultsDirectory:TestResults/product-labels`: **54 passed, 0 failed, 0 skipped**, 54 seconds. Disposable SQL LocalDB only. Evidence: TestResults/product-labels/label-layouts-auto-final.trx.
- Renderer tests decode all four layouts at 35×22 / 50×30 and 203 / 300 DPI using EAN-13 and alphanumeric Code 128. Additional cases include leading zeroes, 8/12-digit values, incorrect 13-digit checksum, legacy EAN8, hidden fields, physical-fit rejection, distinct raster hashes and decoding both product types from actual packed TSPL rows.
- SQL tests verify a pre-existing explicit EAN13 template prints an alphanumeric product using AUTO, old job payloads remain byte-for-byte unchanged, layout saves persist, and retry still returns the original job. Existing permission, CSRF, tenant, version, queue concurrency, partial/reprint and stock-preservation tests remain passing. Model/snapshot matches.
- Google Chrome probe: exit 0, real Web/SQL with printer discovery mocked and no real output. Four loaded thumbnails, each live preview, selected-state accessibility, preserved paper/columns/quantity mode, save/copy persistence, actual job design, 48 base-unit defaults, cancellation and 390px responsive layout. No JavaScript page errors.
- Visually inspected TestResults/product-labels/browser/template-50x30-two-columns.png, layout-price-first-35.png, layout-price-tag-35.png, layout-framed-35.png and templates-mobile.png. Vietnamese labels and all four styles are visible; preview/print barcode decoding passes.

Rollback: retain the updated renderer for queued jobs created with new styles/AUTO or resolve those jobs before downgrading. Keep stored print history. Physical paper/calibration acceptance remains as described above; no independent review or CI assertion.

## Revision C — separate staff printing and admin configuration (2026-09-10)

User correction authorizes two separate workspaces instead of four tabs in one screen. This supersedes the combined-page description above. Scope: menu definitions/bounded seed, label page/controller/service DTO/JS, permission enforcement and related tests/docs. No other business workflows, database schema, queue dispatcher or renderer changes. Baseline/expected parent unchanged. Level C due to authorization boundaries. Status: **READY FOR COORDINATOR REVIEW**; no independent review or CI claim.

### Result and authorization

- **In tem sản phẩm** at /admin/label-printing requires system.productlabel.print. Only receipt tasks and print history panels are rendered. Staff enter directly via its menu, choose a queued receipt, saved template/printer, adjust counts, preview, print, acknowledge and complete. The template editor, printer configuration form and configuration tabs are absent from the staff HTML, including when an admin opens this page.
- **Cấu hình in tem** at /admin/label-printing-settings requires system.productlabel.manage. Its own menu/page renders only saved template designs and server printer configuration. Print tasks/history are absent. Accounts with Print can link to the printing area and perform a configuration test print; a Manage-only account cannot send/confirm/cancel jobs.
- Both roles can read saved templates/printers and preview a product. Configuration writes/discovery/layout-gallery previews require Manage. All operational receipt/task/job endpoints explicitly require Print. The existing any-permission authorization handler is reused; role grants are not expanded.
- Existing /admin/label-printing?task=... and receipt entry points still open the printing area. Unknown/inapplicable tab parameters cannot hide all panels. Page-specific initialization avoids accessing absent controls or fetching printer setup APIs from staff pages. Style names are returned by the server for staff template selectors.
- Normal Development startup and canonical Migrator menu seed add the settings item separately without modifying custom menus. No configured database was accessed or changed by this task. Restart the updated Web app for the new menu/page. No worker update is required for this page-only revision beyond the renderer requirements already recorded in revision B.

### Changed files

- GaoApp.Web/Areas/Admin/Controllers/LabelPrintingSettingsController.cs (new)
- GaoApp.Web/Areas/Admin/Controllers/LabelPrintingController.cs
- GaoApp.Web/Areas/Admin/Views/LabelPrinting/Index.cshtml (server renders the corresponding workspace only)
- GaoApp.Web/wwwroot/Admin/js/printing/label-printing.js
- GaoApp.Web/Services/Printing/ProductLabelService.cs (layout display name in template DTO)
- GaoApp.Infrastructure/Data/Seed/AdminMenuSeeder.cs
- GaoApp.Infrastructure/Data/Seed/ProductLabelMenuSeeder.cs
- GaoApp.Tests/Security/ProductLabelSqlServerTests.cs
- GaoApp.Tests.Browser/Program.cs
- GaoApp.Tests.Browser/label-printing.browser.cjs
- GaoApp.LabelPrintServer/README.md
- docs/development/DECISION-LOG.md and this handoff

### Verification and evidence

- Isolated Release Web/tests/browser build: exit 0, 0 errors, the same 10 pre-existing test warnings. JavaScript syntax checks and scoped git diff --check pass (line-ending notices only).
- `dotnet vstest Logs/label-printing-artifacts/bin/GaoApp.Tests/release/GaoApp.Tests.dll '/TestCaseFilter:FullyQualifiedName~ProductLabel|FullyQualifiedName~AdminMenuRecoverySeederTests' '/logger:trx;LogFileName=label-separate-workspaces.trx' /ResultsDirectory:TestResults/product-labels`: **62 passed, 0 failed, 0 skipped**. SQL LocalDB databases are temporary, fixture-created and fixture-cleaned. Evidence: TestResults/product-labels/label-separate-workspaces.trx.
- New permission test checks staff/config-only/unrelated roles, staff config-page 403, absent configuration HTML on the printing page, absent operational HTML in settings, configuration-only account denied task/job access, staff denied create/update/delete template or printer configuration, shared lookups allowed, and one menu per role.
- Existing label barcode/TSPL, old job preservation, CSRF/store isolation, quantity/concurrency/partial/reprint/stock-preservation and menu recovery/idempotency tests remain passing.
- `dotnet Logs/label-printing-artifacts/bin/PosOffline.Browser/release/PosOffline.Browser.dll --label-printing`: exit 0. Google Chrome uses a full-permission admin to configure templates/printers and queue a receipt, then a separate Print-only employee session opens that receipt from the independent menu, selects the saved template/printer, previews, queues and cancels a job. Confirms no settings menu/editor on staff page, direct settings request 403, no staff installed-printer/gallery fetches, four layouts/save/copy/AUTO persistence, and no page errors or root overflow at 390px.
- Screenshots visually inspected: TestResults/product-labels/browser/staff-print-workspace.png, staff-print-mobile.png, template-50x30-two-columns.png. No physical printer or deployed service is invoked.

Rollback: reverting this revision restores the combined UI without changing stored templates/tasks/jobs; remove/disable the new settings menu through normal menu management if no longer used. Do not delete operational tables or print history. No commit, push, merge, deployment or live migration performed.

## Revision D — Receipt printing toolbar only in manager pending review (2026-09-10)

User correction: the “Đưa vào in tem / Danh sách in tem / progress” toolbar belongs in the manager's pending-approval screen, not the employee receiving screen.

- Removed the toolbar partial and its script from WarehouseReceiving/Detail and PurchaseReceiving/Index. These employee entry screens omit the toolbar even for users who also hold printing or administrative permissions.
- StockDocumentManagement/Edit renders the toolbar and script only when `canApprove` is true: PendingApproval plus the corresponding direct-receipt or PO-receipt approval permission. The shared partial continues to require ProductLabel.Print. Draft, Rejected, Confirmed and Cancelled detail views do not render it; non-approvers do not see it on manager detail either.
- The existing prompt immediately after successful approval remains available from the pending manager page. The dedicated staff printing workspace, saved tasks/history, print permissions and server APIs are unchanged. This revision controls the location and visibility of the receipt toolbar; it introduces no new backend authorization policy.
- Scope: the three Razor views, development documentation and a timing correction in the existing browser probe. Existing dirty workspace and all receipt/barcode work are preserved. No new tests were added for this bounded presentation change.

Validation: isolated Release build succeeds with 0 errors and 10 existing unrelated test warnings. All 31 existing receiving UI/approval/architecture/provisional/exception regression checks pass (`TestResults/product-labels/receipt-label-visibility-regressions.trx`). Scoped whitespace checks pass.

The initial browser rerun timed out during the repeated barcode search, before submission. The existing probe now waits for the quantity dialog to fully close and sends keyboard events for repeated scans instead of only filling the reused Select2 input (which can retain Enter suppression). This adjusts test interaction only; receiving application JavaScript is unchanged in this revision.

The final real Google Chrome rerun passed against disposable SQL: employee quantity entry, autosave/retry/concurrency guards, actual submit modal and separate manager barcode review all remain functional without JavaScript page errors. Existing browser evidence is under `TestResults/receipt-barcode-proposals/browser/`, including the pending manager detail in `manager-review.png`. All employee receiving toolbar includes have been removed; manager inclusion is checked server-side before rendering.

Rollback: restore only the toolbar partial/script includes and pending-review gate changed by this revision; no schema or data rollback. No commit, deployment, live database access or physical printing. **READY FOR COORDINATOR REVIEW**; no independent review or CI claim.
