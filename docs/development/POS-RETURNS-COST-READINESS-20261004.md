# POS returns: cost readiness and refund validation

## Confirmed incident

Order `1799586`, trace `0HNP216HEGQCB:00000006`, failed on 2026-10-04 at 18:26:25.
`App_Data/Logs/web-20261004.log` identifies the inner failure as an unresolved
provisional sale cost in `SaleValuationCostPolicy.RequireFinalUnitCost`.
`SalesReturnService` and the full refund wrapper both classified this inventory
prerequisite as a technical failure (`POS_PARTIAL_RETURN_FAILED` and
`POS_REFUND_FAILED`). Changing the refund payment method cannot resolve it.

## Implemented behavior

- Unresolved restock cost has a specific business error and HTTP 400:
  `POS_RETURN_COST_PENDING` or `POS_RETURN_COST_UNAVAILABLE`. The response includes
  a safe message, the affected order/line and an action hint for the manager.
- Eligibility includes per-line restock readiness, checked by reading the same
  durable cost evidence as the actual stock reversal. Both return dialogs show
  which item needs attention before submission. The posting transaction still
  checks current evidence; the preview is not authorization to post stock.
- Ordinary sales support `RefundOnly` with payment and no returned lines. No
  inventory or cost reversal is posted. Credit/deposit orders use the combined
  return flow to preserve debt/deposit reconciliation.
- Reject invalid payment methods, unsupported return types, payments in
  `ReturnOnly`, and goods in `RefundOnly`. Suggested refund unit price includes
  the original line discount.
- Full refund rejects previous completed returns, including returns without a
  refund payment. Receipt reads occur after the owned posting transaction has
  committed and disposed.
- Cost checks and return dialog cleanup invalidate requests when hiding starts,
  so a previous modal animation cannot clear a newly reopened form.

## Implemented awaiting-restock workflow

The user approved receiving goods and refunding immediately while waiting for
resolved cost. A returned line can now use `PendingRestock = 2`. Full refund has an
explicit `AllowPendingRestock` choice; only lines lacking resolved cost are held.
Partial returns have three distinct choices: restock, awaiting restock, and no
restock. The return history marks received goods still awaiting stock receipt.

`SalesReturnRestockFragments` persists the exact original sale sources and held
base quantities for both legacy and allocated orders. Allocated sources also have
`ReturnPendingRestock` reversal records. Held quantities consume return eligibility
but post neither a stock movement nor a cost mirror. Refunds, deposits, debt,
invoice draft quantities and rewards remain part of the original return transaction.

Managers open `/admin/pos/returns/pending-restock` from the order detail page.
The list groups the same item/warehouse into one row and shows cost readiness.
Completing a return requires `pos.order.view` and `inventory.stockdocument.approve`,
with antiforgery protection and tenant isolation. It locks the original order and
all source balances, checks all costs before writing any stock, then posts each
reserved source once into its original warehouse. Completion records the actual
cost, stock transaction, time and actor; it does not post any further refund,
deposit, debt or reward adjustment. It also works after the cashier closes their
shift. Repeated or competing completion commands are idempotent. Reports keep
the original cost while goods are held and reduce it only after a real receipt.

`NoRestock` remains distinct: it represents goods that will not return to saleable
stock, not a pending receipt. Missing source history is not replaced with a guessed
cost. Managers must resolve actual sale cost before completion.

## Database update

Apply migration `20261004122812_AddPendingSalesReturnRestock`, or the equivalent
idempotent SQL in `docs/pos-return-pending-restock-upgrade.sql`, to the target
application database before enabling pending returns. It only adds the custody
table and indexes; no existing stock or payments are rewritten. The EF snapshot
matches the current model. This task has applied migrations only to disposable
test databases, not the database used by the live incident.

Older databases can continue ordinary returns until the new table is installed.
Choosing a pending return before migration yields a clear database-update message
and rolls back the entire return. Complete the migration before using the new
manager page. No extra role permission seed is required.

## Validation

- Web project builds successfully.
- 57 focused inventory/return contract and SQL integration tests pass, including
  full/partial returns, actual refund tender, transfer surplus, refund-only,
  rollback on provisional cost, and successful restock after actual receipt
  resolves provisional cost.
- 37 additional tests pass for deposit returns, credit/debt reconciliation,
  invoice draft synchronization, valuation policy and the full POS workflow.
- `GaoApp.Tests.Browser/pos-returns.browser.cjs` passes against actual Razor modal
  markup and production browser assets with synthetic API responses. It covers
  warnings, prevented restock submission, discounted price, refund-only, tender,
  prior return history, credit return-only, explicit NoRestock, mobile layout and
  stale responses. Screenshots are generated under `TestResults/pos-returns`.
- Pending workflow validation: 104 relevant tests pass, followed by six focused
  cases (107 distinct tests overall). They cover legacy/allocated custody, actual
  cost resolution, completion after shift close, concurrent completion, correct
  refund totals, tenant/permission/antiforgery protection, missing-schema rollback,
  deposit/debt regressions and report provenance.
- `pos-pending-restock.browser.cjs` uses actual HTML captured from the real
  controller and production JavaScript, with synthetic writes. It verifies cost
  readiness, one submission, whole-card removal/count updates, conflict retry and
  mobile layout. Neither browser suite submits to the live application database.

```powershell
dotnet build GaoApp.Web/GaoApp.Web.csproj --no-restore -v quiet
dotnet test GaoApp.Tests/GaoApp.Tests.csproj --no-restore --filter 'FullyQualifiedName~POSReturnsSqlServerTests|FullyQualifiedName~SaleCostReversalIntegrationTests|FullyQualifiedName~SaleCostReversalSqlServerTests|FullyQualifiedName~InventoryPosPostingContractTests|FullyQualifiedName~ReturnableValuationFragmentServiceTests' -v quiet
dotnet test GaoApp.Tests/GaoApp.Tests.csproj --no-restore --filter 'FullyQualifiedName~CustomerDepositSqlServerTests|FullyQualifiedName~CustomerReceivableSqlServerTests.Bank_collection_and_returns|FullyQualifiedName~FullWorkflowSqlServerTests.Real_login_sale_payment_finalize_return_void|FullyQualifiedName~DraftInvoiceReturnSyncServiceTests|FullyQualifiedName~SaleValuationCostPolicyTests' -v quiet
cd GaoApp.Tests.Browser
$env:GAO_RETURNS_TEST_OUTPUT='../TestResults/pos-returns'
node pos-returns.browser.cjs
node pos-pending-restock.browser.cjs
```
