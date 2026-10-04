# Correcting tender on finalized POS sales and received deposits

Page: `/admin/pos-shift/requests?tab=payment`. The shared **Yêu cầu nghiệp vụ** page has two primary tabs: **Điều chỉnh thu/chi** and **Đổi phương thức thanh toán**.

Cashiers can open the payment tab from shift reconciliation, a finalized order's detail page, the deposit journal or **Yêu cầu nghiệp vụ → Đổi phương thức thanh toán**. Sub-tabs separate requests, order receipts and received deposits. Managers inspect and approve or reject requests. The default dialog shows the receipt, original/proposed tender, reason and expected cash before/after. Full shift figures, reference entry and history are expandable. A reason is mandatory; a bank reference is optional for both sale and deposit corrections.

The legacy cash/payment page routes redirect to the shared page with the appropriate primary tab and preserve shift, receipt, request and status filters. Existing JSON API routes stay under `/admin/pos-shift/cash-adjustments` and `/admin/pos-shift/payment-adjustments`. Switching primary tabs preserves each tab's local filters and list without another page load; request IDs never carry into the other request type. This navigation change adds no migration or financial operation.

## Scope and authorization

- Viewing requires `pos.shift.view`. Creating requires `pos.shift.reconcile` and ownership of the sale's shift. A cashier can request corrections for their open or closed shifts, and withdraw their pending requests.
- Approval, rejection and final reconciliation require an explicit administrator membership in the selected store. Wildcard permissions do not grant administrator status. Other employees cannot inspect another employee's requests. All queries use explicit store predicates.
- Corrections are allowed for active payments on completed or refunded sales. Draft/held/voided/cancelled sales, cancelled payments and debt collection receipts are excluded.
- Automatic ACB sessions and non-manual QR confirmations linked to a payment protect it from being relabelled. This check includes archived records. Their actual bank evidence must be investigated through the bank reconciliation workflow. Manually entered transfers and manual QR confirmations can use this correction workflow.

## Financial behavior

Only the method and optional reference change. The original received amount, payment ID, client request ID, payment timestamp, order amount, paid total, balance due, deposit balance, refund amounts and inventory remain unchanged. Cash clears the reference. The original method/reference/provider are retained in the request; a corrected order payment's provider is marked `POS_ADJUSTMENT`.

Received deposits (`CustomerDepositEntry.Kind = Receive`) can change between cash and manual transfer, even after part of the deposit has been used or refunded. The original entry amount, timestamp, idempotency payload, customer, purpose and balance remain unchanged. The selected active manual bank account is captured when requesting a transfer and rechecked on approval. No bank reference input is required. Other deposit ledger operations (Apply/Refund/Return/Void) cannot use this correction endpoint.

Approval changes the receiving shift's CashIn by the full receipt amount when its method changes. It creates, retires or restores the receipt's linked cash voucher in the same transaction. It does not move CashSales/NonCashSales. A receipt has at most one linked cash voucher, including its archived state, so cash → transfer → cash does not duplicate money. Legacy cash receipts are matched only by the exact store, shift, deposit note and receive reason; unmatched, mismatched or ambiguous evidence is blocked. Generic cash voucher corrections cannot edit deposit-generated vouchers. Existing pending generic requests must be rejected/withdrawn before using the deposit workflow.

The effect on the shift is calculated using the existing sale allocation rules: the sales budget is `max(0, grand total − deposit)`, non-cash payments cover that budget first, and cash covers its remainder. Debt collections are excluded. Approval moves only the resulting allocation between cash and non-cash sales. For example, changing a 250,000 cash receipt on a 215,000 sale to a transfer preserves the 250,000 receipt and shifts 215,000 of sale allocation. A method change can legitimately have zero effect on the cash bucket when another transfer already covers the sale.

Creating, rejecting or withdrawing a request never changes money. Approval applies the allocation delta once in the same database transaction as the payment change and request history. Closing counts, received cash and original closing-slip snapshots remain immutable. Approval on a closed shift sets `NeedsCashReconciliation`. The existing cash reconciliation operation now marks both cash voucher and payment corrections reconciled together, with a manager's note.

## Concurrency and history

- One pending request per sale prevents competing corrections to a mixed-tender allocation.
- One pending request per received deposit prevents competing reclassifications. Deposit entry and cash voucher versions, the captured bank account and the reviewed shift version are rechecked at approval.
- Request creation uses a durable client request ID. Replaying identical data returns the same request; reuse with different data is rejected.
- Approval checks the payment and order versions captured when the request was created, plus the shift version inspected by the manager. Changed evidence requires rejection and a fresh request. Changed shift totals require refreshing the approval detail.
- Every approval advances the shift's version, including reference-only changes with zero cash delta, so an old reconciliation screen cannot silently acknowledge a later correction.
- Order locks precede shift locks, matching sale/debt/refund operations. Retried or simultaneous approval applies the change once.
- The correction history is included in the reconciliation page's **Điều chỉnh** tab. Before/after shift snapshots, requester, reviewer, reasons, dates and reconciliation notes are retained.

## Deployment

Migration `20261004055012_AddPOSPaymentAdjustmentRequests` adds the request table and indexes. It does not modify payment amounts or shift values during migration. Deploy the updated Migrator and apply this migration before serving the updated Web application; the reconciliation and shared correction services query the new table.

Follow-up migration `20261004103622_AddDepositPaymentAdjustments` allows the same approval table to target either an order payment or a received deposit (exclusive check constraint), adds deposit receipt/voucher links and one-pending-per-deposit indexes. It does not reclassify legacy money during deployment. Its rollback refuses to discard existing deposit correction history. Apply both migrations before running this Web version.

An idempotent script for this migration alone is saved at `TestResults/pos-payment-adjustments/AddPOSPaymentAdjustmentRequests.sql` for inspection. It assumes the preceding migration `20261003193000_AllowPurchaseReceiptFixedAmountDiscount` is already applied. Use the repository's standard Migrator/preflight procedure for a live database. No live business database was changed while implementing this feature.

The follow-up migration script is at `TestResults/pos-deposit-payment-adjustments/AddDepositPaymentAdjustments.sql`; it assumes the first payment-adjustment migration is applied. Current compact UI previews are in `TestResults/pos-deposit-payment-adjustments/` and `TestResults/pos-reconciliation-simple/`.

## Validation

- `POSPaymentAdjustmentSqlServerTests`: real Web/middleware/services/SQL tests for ownership/admin access, idempotent and simultaneous approval, full overpayment preservation, capped allocation, closed-shift snapshots, combined reconciliation, stale order/shift guards, rejected/withdrawn requests, bank evidence protection including archived records, deposits, credit balances and refunds.
- `POSDepositPaymentAdjustmentSqlServerTests`: receive corrections in both directions without reference input, partial consumption/refund balance preservation, original-transfer voucher creation, legacy evidence checks, store/owner boundaries, repeated/concurrent approval, closed slip/count preservation, combined reconciliation and changed entry/bank/shift guards.
- Existing cash adjustment and employee reconciliation SQL tests passed, along with the EF model/snapshot consistency check and endpoint authorization coverage.
- `node GaoApp.Tests.Browser/payment-adjustments.browser.cjs`: actual Razor body and production JS/CSS with synthetic APIs; compact default information, optional reference, deposit request/approval, immutable amount, server-calculated preview, failed-response retry, double submission, cashier/manager actions, required rejection note, protected bank receipts, closed-shift reconciliation, escaped content and mobile overflow.
- The existing reconciliation browser fixture and its 5 Node tests passed after integration.
- `POSRequestsPageSqlServerTests` and endpoint authorization coverage passed (3 tests): actual shared Razor page, legacy redirects preserving query parameters, employee/administrator modes, forbidden access and unchanged JSON routes.
- `node GaoApp.Tests.Browser/pos-requests.browser.cjs`: shared tabs, lazy loading, retained independent filters, request ID isolation, direct receipt scopes, existing forms, keyboard selection and mobile layout. UI screenshots are in `TestResults/pos-requests/` (`GAO_REQUESTS_TEST_OUTPUT`). Payment/deposit and reconciliation browser regressions also passed.

Set `GAO_PAYMENT_ADJUST_TEST_OUTPUT` to save UI screenshots. Verification images are in `TestResults/pos-payment-adjustments/`.

Follow-up validation on 04/10/2026: 21 SQL/model tests passed (order corrections, deposit corrections, deposit operations, cash corrections and reconciliation), plus both browser probes and all 5 reconciliation Node tests. The UI probes also verify local display of SQL UTC timestamps without a timezone suffix.
