# Viettel connection test — implementation handoff

- Date: 2026-09-08.
- Authorization: Human requested investigation and repair, then supplied demo credentials for live verification.
- Workflow: Manual BMAD discovery, bounded implementation, regression verification, handoff. No independent review or global TASK PASS claimed.
- Branch: `fix/r2-4-c1-invoice-identity-uniqueness`.
- HEAD: `db6a6c5e98b69472624eeb31aa6be2161576c3d2` (worktree already contains unrelated changes).
- Scope: `ViettelInvoiceAuthClient.cs`, new `ViettelCustomFieldsConnectionTests.cs`, this handoff. No changes to issue workflow, schema, database contents, or stored credentials.

## Confirmed cause

The live demo `getCustomFields` returned HTTP 200 and a JSON object containing `errorCode: null`, `description: null`, and a non-empty `customFields` array. Each metadata object contained `id`, `invoiceTemplatePrototypeId`, `keyTag`, `valueType`, `keyLabel`, `isRequired`, and `isSeller`.

The previous parser recognized `result` payloads and explicit success codes, but ignored the named `customFields` payload. It therefore returned `Viettel.BasicAuthAmbiguousResponse` before calling `getInvoices`. Issuing calls a different API and does not depend on this probe.

## Change and verification

Recognize a direct `customFields` array in the existing recognized containers. Require object entries. Reject invalid named payloads before the generic success-code fallback. Preserve explicit failure markers, strict JSON/duplicate-property validation, ambiguity rejection, and the subsequent invoice-list check. Null or missing payloads are not newly accepted.

Before the fix, the initial 20-case regression suite had 10 failures, including valid named payload rejection and invalid payload acceptance with an explicit success code. The final suite includes the observed live metadata shape with synthetic values.

Command actually run:

```powershell
dotnet test GaoApp.Tests/GaoApp.Tests.csproj --no-restore --filter 'FullyQualifiedName~ViettelCustomFieldsConnectionTests|FullyQualifiedName~ExternalHttpResiliencyTests' --verbosity quiet
```

Final result: 187 passed, 0 failed, 0 skipped; 21 new regression cases. Existing unrelated xUnit analyzer warnings remain. Scoped `git diff --check` passed.

Live verification through `ViettelInvoiceAuthClient.TestConnectionAsync`, using the supplied demo password only in memory: `getCustomFields` HTTP 200; `getInvoices` HTTP 200; connection result successful, provider totalRows 14,985. No invoice issuance was performed. Response values were redacted from diagnostic output. The final refinement removed provisional null-payload support; the observed array response is covered by the final regression suite.

## Runtime limitation and rollback

The local database's encrypted password refers to a Data Protection key missing from the current configured key directory. This is separate from the screenshot's parser error. Live API verification used the Human-supplied password in memory; it did not verify MVC using the encrypted database password. Re-entering and saving the demo password through the configuration form (or restoring its original key) is needed if that missing-key error occurs on application restart. No password is included here or in source.

Restart/rebuild the application to load the source fix. No commit, deployment, full-suite run, independent review, or existing BMAD report-task resumption was performed.

Rollback: remove only the added `customFields` recognition/validation blocks and helper from `ViettelInvoiceAuthClient.cs`, and the new regression file/handoff. Do not reset unrelated worktree changes.
