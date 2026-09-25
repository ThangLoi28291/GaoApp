# XML input stock — 2026-09-10

Status: READY FOR COORDINATOR REVIEW. User authorized the XML stock view and increase/decrease history; decrement on electronic invoice issuance, not POS sale. Implementation and local validation complete; independent review/CI acceptance not claimed.

Baseline/expected parent: db6a6c5e98b69472624eeb31aa6be2161576c3d2, branch fix/r2-4-c1-invoice-identity-uniqueness. Existing dirty work preserved; snapshot of edited existing files under Logs/xml-stock-baseline. No commit, migration, production database access or deployment.

Scope: shared documentary stock projection for issuance preflight and read-only balances/history. Receipt increases require confirmed receipt + active XML detail mapping + confirmed conversion evidence. Cap each XML detail globally across receipt links, allocate in receipt recognition order, and cap each receipt contribution by its mapped received quantity. Ignore unmapped/excluded rows. Uncertain issuance keeps a hold; successful issuance consumes; drafts and POS sales do not. Preserve correction/return workflows (no invented quantity reversal).

The ledger reconstructs source movements from currently valid receipt/XML evidence and invoice snapshots; it is not an immutable audit log of XML unlink/relink edits. Existing valid sources appear immediately without backfill. Monetary reconciliation differences do not create extra XML quantity. Old boolean-only XML flags without mapped details supply zero until mapping is completed. Tenant and legal owner checks apply to sources. No new physical inventory posting.

Allowed files: new DTO/interface/service/repository for InvoiceInputStockRead; existing InvoiceInputStockRepository, Application/Infrastructure DependencyInjection; new InvoiceInputStockController, its Index view, invoice-input-stock.css/js; AdminMenuSeeder, InventoryLedger/Index and Invoice/ViettelPayload links; InvoiceInputStockRepositoryTests plus dedicated XML read/query/security/SQL tests; Browser Program and invoice-input-stock.browser.cjs; this document and DECISION-LOG. Generated evidence only under Logs/TestResults. Other files locked.

Read evidence: InvoiceInputStockRepository, ViettelInvoiceIssueService, DraftInvoiceReturnSyncService, InputInvoiceXmlService, InputInvoiceReconciliationService/Policy/DTO/entities, StockDocumentService confirm, InventoryLedger controller/view/read service, menu seeder, existing input stock tests, disposable browser fixture.

Validation: targeted xUnit tests including mapped XML cap, duplicate receipt links, conversion, issuance versus hold/draft, tenant/owner boundaries, chronological balances before date filtering; disposable SQL query translation and authenticated Chrome UI with responsive/quick-view/error/filter checks. Build Web/tests with isolated artifact output. No real invoice provider or store database calls. Independent review and CI acceptance remain separate.

Rollback: revert only this task's edits against Logs/xml-stock-baseline; remove task-new source files. No schema/data rollback required. The stricter XML source calculation can expose shortages previously hidden by boolean-only mapping; complete source mapping rather than increasing physical stock.

## Implementation handoff

Exact source files added:

- GaoApp.Application/DTOs/Invoices/InvoiceInputStockReadDtos.cs
- GaoApp.Application/Interfaces/Repositories/Invoices/IInvoiceInputStockReadRepository.cs
- GaoApp.Application/Services/Invoices/InvoiceInputStockReadService.cs
- GaoApp.Infrastructure/Repositories/Invoices/InvoiceInputStockReadRepository.cs
- GaoApp.Web/Areas/Admin/Controllers/InvoiceInputStockController.cs
- GaoApp.Web/Areas/Admin/Views/InvoiceInputStock/Index.cshtml
- GaoApp.Web/wwwroot/Admin/css/invoice-input-stock.css
- GaoApp.Web/wwwroot/Admin/js/invoice-input-stock.js
- GaoApp.Tests/Invoices/InvoiceInputStockSqlTests.cs
- GaoApp.Tests.Browser/invoice-input-stock.browser.cjs

Existing files changed: InvoiceInputStockRepository; both DI files; AdminMenuSeeder; InventoryLedger/Index and Invoice/ViettelPayload links; InvoiceInputStockRepositoryTests fixtures and business tests; Browser Program dispatch; this document and DECISION-LOG. Existing snapshots confirm the menu delta is only three lines; the preflight delta removes duplicated quantity calculations and uses the shared projection. No other task source files were edited. Branch/HEAD unchanged from baseline.

### Final validation

1. `dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj --no-restore -c Release --artifacts-path Logs/label-printing-artifacts -v:q` — exit 0, 0 errors, 10 existing warnings in other tests (AcbInstallment, SalesReport, Pos UI contracts). Log: Logs/xml-stock-build-final.log.
2. `dotnet test GaoApp.Tests/GaoApp.Tests.csproj --no-build --no-restore -c Release --artifacts-path Logs/label-printing-artifacts --filter 'FullyQualifiedName~InvoiceInputStockRepositoryTests|FullyQualifiedName~InvoiceInputStockSqlTests|FullyQualifiedName~DraftInvoiceReturnSyncServiceTests' --logger 'console;verbosity=normal'` — exit 0, 26/26 passed. Log: Logs/xml-stock-tests-final.log. Includes populated disposable SQL projection, quantity caps, shared XML across warehouses, source exclusions, legal owner/store isolation, draft/hold/issued outcomes, hold-to-issued transition, repeated reads, flag independence, period opening balance and existing return behavior.
3. `dotnet Logs/label-printing-artifacts/bin/PosOffline.Browser/release/PosOffline.Browser.dll --invoice-input-stock` — exit 0. Authenticated real Razor and empty SQL API; controlled balance/ledger data at six widths (1440, 1366, 1024, 768, 390, 360), double-click, source link, product history, date validation, holds, error/retry. Log: Logs/xml-stock-browser-final.log. Screenshots: TestResults/invoice-input-stock/browser; inspected desktop ledger/balances and mobile dialog.
4. `node --check GaoApp.Web/wwwroot/Admin/js/invoice-input-stock.js` and scoped `git diff --check` — exit 0. Git only reports LF/CRLF notices.

Initial sandbox LocalDB startup was unavailable; final SQL/browser runs were automatically approved outside the sandbox and used the fixture's disposable database only. No real invoice was issued. No physical-stock mutation, migration, production-data check, deployment, commit, push or CI run was performed.

### Operational notes

- Restart/rebuild the application for controller/Razor/menu changes, then open `/admin/invoice-input-stock`. Access follows `inventory.transaction.view`, matching the physical ledger. Standard startup menu seeding adds the entry under Kho; direct links are also present on InventoryLedger and ViettelPayload.
- Incoming quantities require saved reconciliation conversion evidence tied to an active XML detail mapping. Conflicting conversion snapshots for the same XML detail contribute zero until reconciled. Missing/legacy boolean-only links are not inferred as invoice quantity.
- Running balances use all source history before applying date/type filters. Current balances always use all matching product/warehouse sources, even when searching a document. Dates are displayed/filtered in UTC+7.
- This release reconstructs the ledger from current valid documents. It does not introduce persisted mapping-edit movements, manual stock adjustments, transfers, or new correction/return accounting rules. Existing correction invoices do not consume twice.
- Projection reads source rows per store before pagination to preserve global XML caps and running balances. Large-store performance benchmarking remains outside this local functional validation.
