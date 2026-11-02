# WEB04 independent final review — PASS

Candidate: **APP V3 / Test V6**. No unresolved P0/P1/P2 finding remains within WEB04. This conclusion combines frozen-source review, actual SQL/HTTP/browser results, independent artifact/ID/probe checks and final cleanup/identity verification. Root remains the phase coordinator; production stays closed and WEB05 is a separate gate.

## Frozen identity

Final fresh independent verification: **51 production files + 18 test files + 6 central DLLs + 9 runtime dependency copies = 84 checks, zero drift**. `final-cleanup-and-identity-v6-audit.json` contains every identity; root independently recorded the same result in `coordinator/post-tests-v3-test-v6-identity.json`.

- Production manifest: `E866D812B3DD5ECB8649EEA923F2AE786BC50F412E5E26C29FD41483A692210A`.
- Test V6 manifest: `3BE45E1B0AAFFBEE59A0FEE136426DAAB5CF151248B27E33AF9BEF347B3DE540`.
- Central binary manifest: `A046867BE1C40F70FD41B457C408AFA97E3132B8CEC61787B71F4B1557B61790`.
- Runtime-copy manifest: `BF3D3B721AA8EFE0FE0723A30B5D7BF41FBCC6C744AA59C2F90744BA73B14B8C`.

The isolated graph built successfully. Its one preexisting xUnit warning was retained. Reviewer built only an owned offline probe referencing tested DLL copies; no APP/test edit, shared build, SQL query, shop file access, process stop, menu mutation, commit or deployment was performed by this reviewer.

## Actual test result and retry accounting

**640 distinct test IDs passed, zero skipped**: 207 WEB04 + 359 WEB01–WEB03 regression + 74 bounded legacy cases. `final-640-distinct-id-audit.json` independently reconciles all four final TRXs, not display names. Main566 and bounded74 have no overlap. The bounded list has 74 IDs despite 71 truncated display names.

There were **641 final attempts**: 640 passed attempts and one historical environment failure, then the exact same metadata-test ID passed on retry. An unnecessary R1-only SQL selector was rejected by its unchanged named-LocalDB guard before connection construction. Removing that override uses the default local-test selector, which may inherit the retained local SQL Express setting; no actual LocalDB connection is claimed. The selected method constructs a closed provider context and reads migration operations/manifest, with no Open/CreateDB/Migrate/EnsureCreated path. No source/configuration/guard patch was made for that retry. Five bounded source files retain their recorded hashes and have no FullApplicationFixture/Main/bootstrap entry point; low-level guarded GUID inventory fixtures are still used by some bounded cases.

Targeted41/1 and earlier failed runs are history/subsets, not additional accepted cases. Earlier fixture corrections are explicitly audited: a 43-character legacy Delivery code became 38 while retaining its full GUID; original-root planned replacement coverage became zero under the existing guard; the missing real PickingWork parent was inserted. All assertions and POS3 + Delivery4 + Web3 = counter10 stayed intact, and the final target/full run exercised them. No APP guard or financial/inventory expectation was weakened.

## Findings closed by fix and actual evidence

| Finding | Final disposition |
| --- | --- |
| W04R1 P1 source changes during balance wait | Source row/range HOLDLOCK coverage through commit plus rebuild/fingerprint/expiry after common balance locks. Actual price/factor/activity/promotion activation/insertion races record administrator commit blocked until the accepted placement serialized first. Late expiry/new-empty-balance rollback and same-key retry paths pass. |
| W04R2 P1 committed web holds omitted from outbound protection | Active Web12 is included alongside Delivery11; verified own exclusion remains Delivery-only. Actual POS movement tests cover forbid-negative rejection and the intentionally preserved trusted negative-allowed branches, without clearing another hold. |
| W04R3 P1 forged physical Web12 | Generic movement rejects hold-only origin12; database CHECK also rejects it. Actual service/raw-SQL positive/negative-direction attempts leave no forbidden physical posting. Later website physical dispatch remains typed Delivery11. |
| W04R4 P2 accepted quote under another command key | Useful conflict before duplicate insert; no prior guest-link disclosure. Same-key exact protected replay, different payload conflict, concurrent placement and actual process restart pass. |
| W04R5 build blocker | Bounded local-variable rename only; actual graph build and final executions succeed. |
| W04R6 P2 UTC kind lost on SQL read | Actual old response/PNG proved seven-hour error. Module-scoped SpecifyKind(Utc) preserves known UTC ticks. Final placement/access both contain the same explicit-Z timestamp and Bangkok DOM matches. No global/legacy serializer or ToUniversalTime shift. |
| W04R7 P2 fresh CHECK comparison | Four captured equivalent whole predicates are recognized exactly, with old normalizer logic/pairs unchanged. Actual fresh schema fingerprints match and every mismatch category is zero. Independent positive, semantic-mutation, disabled/untrusted and quoted-literal regressions pass. No DDL or grouping/arithmetic/literal/binary/flag bypass. |

POS totals extraction was independently compared with the captured pre-WEB04 method body; it is the same calculation, now shared through the POS wrapper. Actual parity cases cover selected discounts, required/mixed combinations, optional hidden unselected members, same-unit gifts, fractional aggregate rounding and large VND. Gross already includes the bounded signed adjustment. Source/admin/tenant checks, strict catalog address membership, shipping/fees, typed real-owner allocations and atomic order/holds/snapshots/receipt/event/outbox are exercised by the listed suites. Checkout creates no physical stock/financial posting or fabricated POS context.

## Canonical browser evidence

Use **`tests/browser/798ff960c9514e5c884f662d13c2bb51`** from the successful full run, not earlier targeted/failed images. Independent audit: ten nested steps passed; 34 DOM records comprise 33 viewport/font/contrast records plus one keyboard record; 36 PNGs; widths320/360/390/430/1440. Recorded body font minimum16, Inter loaded in all33 recorded viewport states, minimum measured contrast6.5713, zero overflow/small-control/contrast failures and no unhandled JS errors.

Reviewer directly viewed six canonical images covering all five widths, keyboard focus, authoritative quote, replacement quote, lost-response retry, guest tracking and exact money above2^53. Focus bottom354.734 is above navigation top761.219 with3px outline. Placement/access `2026-10-30T19:48:34.2738361Z` display `02:48:34 31/10/2026` in Asia/Bangkok. Two network aborts are intentional ward-fetch/lost-response cases and recover. Three total placement attempts mean one QuoteChanged followed by the committed/lost-response request and its identical replay; they are not three identical commands or three orders.

Encoded text, fragment/body guest access, no-store/no-referrer and protected outcome persistence were reviewed with actual HTTP/browser/source evidence; retained capability fields are masked. The browser request record stores method/path only and cannot independently attest query-string telemetry. These browser counts are not added to the xUnit total. `full-web04-canonical-browser-v6-audit.json` hashes every canonical PNG and records exactly which images were viewed.

## Cleanup and boundaries

Independent current filesystem checks validate all **478 unique GUID targets** are direct children of the three authorized fixture roots, contained within the workspace and absent. All three roots are plain/non-reparse and empty. Producer evidence reports799 removed files including215 private fixture keys and12 synthetic media files; reviewer additionally counted169 preserved log/metadata files and zero archived XML keys. No unknown-path action/global kill is reported. Process evidence records56 owned process metadata entries, zero remaining owned PIDs and zero matching public-host/Node/headless-Chrome processes. Unknown processes were untouched. The producer documents a redundant per-target PowerShell array warning after its prior zero-reparse scan; both its postcheck and this review independently verify all478 target absences/containment and empty roots.

The retained before/after master inventory files are byte-identical, with the same five baseline rows and SHA256 `9AF396A6CF0297686A229C5B43B73A7443A4EEA0C2CE1864A0CFBBC110CAF0D6`. Reviewer compares retained SQL/process evidence; reviewer did not operate SQL or stop processes. All84 final identities were freshly re-read directly after tests/cleanup.

The bundled address JSON remains the audited287104-byte snapshot,34 provinces/3321 wards, SHA256 `688F1A761199A2315C3C3B0F68F642493A961D775E5ADA2233411291D518A23D`; independent comparison covered every captured code/name/parent triple. This is captured factual evidence, not a geocoding/legal-boundary/future-freshness guarantee. No runtime downloader/seed is introduced.

The customer host remains separate/default-closed Production. Guest COD access is deliberately long-lived, dependent on canonical hash/order/active store, with no invented TTL. Admin URL `/admin/storefront/orders`, permission `storefront.orders.view`, is supplied for user-managed menus; no automatic menu/sidebar/role grant was added. Trusted negative-stock behavior is preserved, including existing allowed outbound bypasses; this is not an unconditional physical-stock guarantee. Evidence covers the listed suites/states, not every possible interleaving. QR, physical fulfilment, cancellation/refund, final media packaging and opening production remain their later planned gates.

**Independent WEB04 review: PASS.**
