# WEB02 independent review — final v2

Date: 2026-10-31, Asia/Bangkok. Reviewer: `storefront_review`, independent of application author `storefront_code` and tester `storefront_tests`.

**PASS within WEB02 scope. No remaining P0/P1/P2 finding was identified in the reviewed final source, tests and independent probes.** WEB02 may pass the review gate; the coordinator owns closing the stage and starting WEB03.

## Identity and actual evidence

Reviewed against AGENTS.md, W02T01–W02T04, WEB00 contract revision 4 and WEB01 report. Final input: application v2 (18 production files), independent tests v4 (8 Web02 files plus the authorized one-property Web01Support compatibility edit), isolated build graph under `.artifacts/storefront-rollout/build`.

Reviewer independently checked all 18 production hashes, 9 test-source hashes, 6 graph DLL hashes and 4 probe-copy DLL hashes: **0 mismatches**. Actual TRX result nodes and counters were read, including per-file SHA256; no case was inferred from source discovery alone. See [final-evidence-v2.json](final-evidence-v2.json).

| Actual independent execution | Passed | Failed | Skipped |
|---|---:|---:|---:|
| WEB02 publication, catalog/price, security, kg/media, SQL migration, HTTP/CSRF/Razor body | 138 | 0 | 0 |
| WEB01 category regression | 170 | 0 | 0 |
| Selected existing schema regression | 8 | 0 | 0 |
| Existing media library regression, including local SQL scale fixture | 26 | 0 | 0 |
| Total | **342** | **0** | **0** |

Tester final identity/cleanup report confirms the same frozen source/build identity. Reviewer compared before/after database inventories directly: the same five pre-existing GUID fixture databases remain. Both reviewer-owned GUID databases were independently checked in `sys.databases`: **0 remain**. See [final-db-inventory-comparison.json](final-db-inventory-comparison.json) and [probe-db-cleanup-verification.txt](probe-db-cleanup-verification.txt).

## Findings resolved and rechecked

- **R1, P1 — storefront-only media cleanup:** fixed in `MediaLibraryService.cs:43` and :144. Nondeleted StorefrontMedia references now participate in usage detection, library status/product associations and both existing cleanup phases, including hidden publications. On frozen v1 DLLs, reviewer reproduced Used=false → Scheduled → catalog image disappears → storage deletion request. On v2, usage is true, the image remains visible, expired linked cleanup returns Kept and there are zero storage deletion requests.
- **Related media claim transaction:** `StorefrontProductService.cs:121`, :127, :225 and `StorefrontProductValidation.cs:41` use store configuration lock → shared cleanup lock, fresh asset validation, deadline reset and link writes within one SQL transaction. Deleted/temp/token/foreign assets cannot be reclaimed; public DTOs still expose logical media URLs. Reviewer forced a late SQL CHECK failure at link insertion **after** ClaimMedia: the original deadline and original link both survived rollback. A subsequent valid selection cleared the final asset's deadline atomically. Cleanup does not acquire the store lock, so the added lock order introduces no reverse lock acquisition in these paths.
- **R2, P2 — hidden/draft kg switch:** fixed in `StorefrontSettingsService.cs:149` and product choice validation. The settings check now joins published product and variant gates. Reviewer v2 probe confirmed that a hidden weightless draft permits enabling kg, republishing it returns Invalid, and supplying kg permits Updated. Independent tests also exercise hidden variants and the settings/publication race.

The v1 failure history remains in [findings-v1.md](findings-v1.md), `probe-media-v1.log`, archived probe `probe/Program-v1.txt`, and the test artifacts. The initial independent test run was 114/125 with 11 harness failures; tester repairs preserve the business assertions and record corrected fixture/binding preconditions. Final results above use the rebuilt v2 application/v4 tests, not the failed run.

## Reviewed behavior and limits

Read review checked manual publication/default false, separate base/pack choices without synthetic conversions, controller/service permission and tenant checks, per-item bulk outcomes, aggregate rowversion, kg transitions under the shared store lock, safe media references, POS retail override precedence without pack-price multiplication, common read eligibility and no automatic menu changes. The one-column migration and model/snapshot default false were checked with actual fresh/upgrade/downgrade tests. The customer host remains independent of GaoApp.Web; admin CRUD stays in GaoApp.Web.

Reviewer probe ran only a guarded GUID SQL fixture on local SQL Express and recording-only storage; no real upload file or shop database was accessed. The temporary rollback CHECK existed only in that disposable fixture. Probe exit was 0, and no POS, inventory, money, bank or delivery operation was invoked. See [probe-fix-v2.log](probe-fix-v2.log) and [probe-v2-binary-sha256.json](probe-v2-binary-sha256.json). Probe observations are separate from the 342 test cases.

This conclusion does not certify browser viewport/visual behavior, the shared admin layout, complete customer catalog/cart/checkout UI, real order placement, bank/goods/money flows, production performance or UAT. Customer routes and protected media delivery belong to WEB03; final placement revalidation belongs to WEB04. Website remains closed. No application/test source was modified by the reviewer; no commit, push or deploy occurred.
