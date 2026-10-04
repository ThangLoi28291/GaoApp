# Employee shift reconciliation

`/admin/pos-shift/reconciliation` provides a dedicated page for a cashier who holds one till throughout a shift. It is available in the POS navigation and from **Kiểm tra lệch ca** in the closing popup and mismatch confirmation.

The employee can read their own shift. An explicit store administrator can inspect another cashier's shift in the same store. `pos.shift.view` is required; a wildcard permission alone does not bypass the ownership check. All evidence queries are scoped to the selected store and shift, including queries that retrieve cancelled history. An optional `shiftId` selects a historical shift. Without it, the page selects the current terminal's open shift.

## Evidence and totals

- The top cards compare expected cash with the saved closing count or the current browser count. The formula is opening cash + cash sales + cash in − cash out − cash refunds.
- The page independently calculates expected cash from the detailed records and reports differences from the shift counters. A concurrent payment or correction may require refreshing. The page never fixes a discrepancy automatically.
- Orders include items, the full amount actually received in each method, each payment's time, author and reference, cancelled payments, deposits used, original credit granted, surplus and the amount allocated to the shift's sales. Debt collections are excluded from original sale allocation.
- Cash receipts/payments include reason, note, author, cancelled history and pending correction requests.
- Refunds belong to the shift that paid them, including refunds for sales from an earlier shift.
- Deposits include the customer and related sale. Debt collections include the customer and allocation to each original order. Cash deposit receipts/refunds and cash debt collections are already represented by cash vouchers and are not counted twice.
- QR history and adjustment history expose metadata needed for checking. Raw QR images, provider response JSON and credentials are not returned.
- Bank verification requires a linked ACB session with a bank confirmation source, received/completed status, and matching order, payment amount and provider reference. Manual transfers and offline confirmations remain clearly labelled for review. A created QR alone is not evidence of received money.
- Day, amount, text and payment-method filters affect detail rows only. The totals remain for the whole shift, including a shift spanning several calendar days. Detail lists display 25 rows per page.

## Closing count and corrections

Before leaving the closing popup, `pos.shift.close-draft.js` saves the count, denomination quantities and note to `sessionStorage`. The key includes store, cashier, terminal and shift. The draft expires after 24 hours. Returning to the same open shift restores all entered values. A closed or changed shift cannot receive that browser draft; a successful closing clears it. If draft storage fails, navigation stops and the closing popup keeps the entered count.

Opening reconciliation, expanding details, filtering and refreshing perform no financial mutations. Cash voucher correction links use the existing request/approval workflow. Pending requests show their possible effect without changing expected cash. Finalized sale payments now link to the method/reference correction workflow documented in [POS-PAYMENT-ADJUSTMENTS-20261004.md](POS-PAYMENT-ADJUSTMENTS-20261004.md); its history also appears in the correction tab.

## Validation

- `dotnet test GaoApp.Tests/GaoApp.Tests.csproj --no-restore --filter FullyQualifiedName~PosShiftReconciliationSqlServerTests`: real SQL Server coverage for ownership/store permissions, full overpayment amounts, cancelled payments, cross-shift refunds, customer/debt allocations, pending requests, bank evidence and read-only behavior.
- `node --test GaoApp.Tests/Ui/pos-shift-reconciliation.test.cjs`: 5 checks for draft isolation/failure, receipt flags, filters and escaped transaction contents.
- `node GaoApp.Tests.Browser/pos-shift-reconciliation.browser.cjs`: actual view bodies, JS and CSS with synthetic APIs; desktop/mobile layout, per-payment details, filters, request links, both discrepancy buttons, draft restoration, failed storage, changed/closed shifts and denied access. Zero financial writes and zero browser errors.
- The existing shift UI contracts and endpoint security coverage passed (12 checks), and `pos-shift-cash.browser.cjs` passed its existing cash, printing and drawer cases.

Set `GAO_RECON_TEST_OUTPUT` to save browser screenshots. Local verification images are in `TestResults/pos-shift-reconciliation/`.
