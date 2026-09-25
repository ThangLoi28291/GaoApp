# GaoApp automatic invoice issuance

## Scope

This change implements automatic issuance around the existing POS-created `InvoiceHead` drafts and the existing Viettel provider configuration. It does not copy old data, back up or restore SQL, call Viettel, install a Windows Service, or apply a database migration.

The migration file is source-only deployment input for the normal GaoApp Migrator process. Production rollout must be reviewed and executed separately.

## Runtime flow

1. POS continues creating the normal `InvoiceHead` draft. The worker never creates a second draft for a POS order.
2. `AutoInvoiceService` reads the persisted store settings and selects only eligible drafts within the configured local-date scope.
3. Selection is chronological by sale date, then sale time, then `InvoiceHeadId`.
4. Buyer-specific invoices are always single. Consumer invoices can be grouped only when sale date, store, legal entity, provider setting/snapshot, and warehouse set match.
5. Each cycle claims one source set through `AutoInvoiceOperationSources`, then calls the existing `IViettelInvoiceIssueService`. The filtered unique source index is the database backstop shared by manual and automatic issue requests.
6. On success, the operation and every source invoice/order/detail link are completed. On uncertain results, the operation remains `Unknown` and sources remain active until UUID lookup completes.

## Queue/error behavior

- A draft newer than `MinimumAgeMinutes` stays in the queue as `Chờ tới giờ phát hành`; it is not submitted early.
- A consumer draft below `SeparateAmountThreshold` stays in its same-day/group/configuration bucket until the target or the configured closing rule allows one group operation.
- A draft with a persisted error is shown in `Cần xử lý` and is excluded from the next automatic selection. A later eligible draft can continue in chronological order.
- A non-uncertain group failure is copied to every source draft, so the failed group is not rebuilt and submitted repeatedly.
- The Admin `Chạy một chu kỳ` action bypasses only the previous send-interval gate. It still respects Enabled, scope, minimum age, grouping, stock, and UUID safety rules.

## DataProtection credential repair

`C:\GaoAppData\DataProtectionKeys` is the single key-ring path for Web and Worker. If a database ciphertext references a key file that no longer exists, the password cannot be recovered from the ciphertext. The Admin must enter the Viettel password again in **Sửa cấu hình** and press **Lưu**; **Test kết nối** alone does not rewrite the stored secret. The save now verifies that the credential can be decrypted immediately.

After the active credential is valid, the worker clears only `InvoiceProvider.CredentialKeyUnavailable` draft errors in the selected scope and resumes the queue. Unit, stock, provider, and UUID errors remain for Admin review.

## XML stock and legacy POS drafts

The XML-stock screen and issuance preflight now use the same documentary movement projection. A legacy POS draft that has no `OrderLegalEntityAllocation` resolves its warehouse in this order: allocation warehouse, LegalEntity default warehouse, OrderLine/POS shift warehouse, then the Store's active default warehouse; if the Store has exactly one active warehouse, that single warehouse is also an unambiguous compatibility fallback. The service never chooses a warehouse merely because it happens to contain stock. If no valid warehouse can be resolved, the draft stays in **Cần xử lý** and reports that the sales warehouse is missing instead of presenting a misleading `kho #0 / available 0` result.

An `Invoice.InputInvoiceStockInsufficient` error is rechecked at the beginning of the next cycle. Once the XML mapping/warehouse issue is actually resolved, that draft re-enters the chronological queue automatically; unit/configuration/provider/UUID errors remain in **Cần xử lý** until explicitly handled.

## Persisted records

- `AutoInvoiceSettings`: enable/pause state, age/amount/group/interval/closing/scope rules and timezone.
- `AutoInvoiceOperations`: single/group operation history, status, UUID/error and actor information.
- `AutoInvoiceOperationSources`: all source `InvoiceHeadId`, `OrderId`, `InvoiceDetailId`, `OrderLineId` links and source snapshots.
- `AutoInvoiceWorkerStates`: heartbeat, last scan, current operation/invoice, next run and worker error.

`AutoInvoiceSettings` implements the existing audit marker, so web configuration changes use the existing audit pipeline with actor, timestamp, and changed values.

## Worker and administration

- `GaoApp.AutoInvoiceWorker` is a Windows Service host. It polls the service every five seconds; the persisted send interval controls actual issue cycles.
- `/Admin/AutoInvoice` shows today/month queue, waiting groups, errors, operation history, persisted configuration, heartbeat, last scan, current invoice and next run.
- Pausing changes only `AutoInvoiceSettings.IsEnabled`; POS draft creation and manual issue remain available.
- Error actions route to UUID lookup or the existing manual Viettel issue page.

## Rollout boundary

Build and unit tests do not run the demo, contact Viettel, install the service, or update production SQL. Before rollout, the reviewed migration must be applied through the existing migration process, the worker connection string configured, and the service installed under the server's approved deployment procedure.
