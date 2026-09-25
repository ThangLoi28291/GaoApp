# POS customer display redesign — 2026-09-10

## Contract and scope

User requested a complete, modern redesign of `/admin/pos/customer-display` for customers, informed by current customer-display designs. Level B: a bounded presentation and display-state revision. Base branch `fix/r2-4-c1-invoice-identity-uniqueness`; base/expected parent `db6a6c5e98b69472624eeb31aa6be2161576c3d2`. Preserve the existing dirty workspace, POS workflow, payment commands and tenant boundaries. No commit, deployment, live database changes or schema work.

Read the standalone CustomerDisplay Razor view/CSS/JS, its POS controller action, BasePOSPageController header binding, OrderLineDto, ReceiptTemplateService store identity read, POS payment display events, PosHub terminal broadcast contract, display-promotion integration and the isolated browser fixture.

## Research and design

Primary references accessed 2026-09-10:

- [Shopify: customer display screens](https://help.shopify.com/en/manual/sell-in-person/shopify-pos/customize-pos/customer-display): separate idle/cart/thank-you states, store branding and product images.
- [Shopify: terminal experience enhancements](https://changelog.shopify.com/posts/pos-terminal-experience-enhancements): distinct cart and payment presentation.
- [Lightspeed: customer-facing display](https://retail-support.lightspeedhq.com/hc/en-us/articles/360042245893-Setting-up-Customer-Facing-Display): branded welcome screen transitioning into an itemized transaction, with a discreet connection indicator.

An original forest-green, warm-ivory and light-lime design uses a prominent store identity, generous spacing, fixed transaction totals and separate payment panels. No reference screenshots, external brand assets or product imagery were copied. The grocery-bag SVG is an original local illustration. System fonts keep typography available on LAN devices without another font download.

## Result

- Standalone layout replaces the old markup and stylesheet. Persistent store header, terminal name, clock/date, connection status and fullscreen button. The latter uses the browser fullscreen API when available. Revision E removes checkout steps and decorative copy and strengthens the theme contrast.
- Idle: a welcome message and original grocery illustration; the existing display-promotion endpoint still supplies text, images, muted video, duration, flash-sale/countdown data and an option to expand media across the workspace. A missing media file is skipped and the illustration is restored when none remain; no repeated broken-image rotation loop. Revision B keeps the same player and rotation running alongside checkout.
- Cart: the latest changed product retains its image/name/unit/price/line total panel alongside both promotions and the itemized list (Revision D supersedes Revision B's replacement of the product panel). The itemized list uses catalog thumbnails with a neutral missing-image fallback. All lines remain available instead of truncating at seven; long carts automatically scroll and pause while hovered/focused. Reduced-motion preference disables automatic scrolling and entry animation. Unchanged snapshots do not repeatedly rebuild the list or restart its animation.
- Totals: display the existing DTO's subtotal, discounts, grand total, paid amount, balance and change. No new payment or price calculation policy. Show settlement information when applicable. Long product names wrap safely; product names and image attributes remain escaped.
- Payment: separate cash and QR panels. QR keeps the payment image, actual amount, bank/account and transfer content together; changing back to cash clears the QR presentation. Partial-payment acknowledgment is visually distinct from finalized success. Receipt reset clears customer text and returns to idle.
- Failed screen reads retain the visible last snapshot and show a waiting-for-update indicator instead of resetting displayed amounts to zero. This is display continuity, not a new offline POS synchronization mechanism.
- Branding: the controller reads the current store's existing receipt identity through ReceiptTemplateService. CustomerDisplay uses that name/address instead of hard-coded GaoMart or placeholder membership QR promises. The service already applies CurrentStoreId; no new public endpoint or permission grant.

## Files

- `GaoApp.Web/Areas/Admin/Controllers/POSController.cs`: CustomerDisplay action only (other dirty edits predate this task).
- `GaoApp.Web/Areas/Admin/Views/POS/CustomerDisplay.cshtml`
- `GaoApp.Web/wwwroot/Admin/css/pos/pos-customer-display.css`
- `GaoApp.Web/wwwroot/Admin/js/pos/pos.customer-display.js`
- `GaoApp.Web/wwwroot/Admin/img/customer-display/grocery-bag.svg` (new)
- `GaoApp.Tests.Browser/Program.cs`: additional isolated probe entry point.
- `GaoApp.Tests.Browser/customer-display.browser.cjs` (new)
- This handoff and DECISION-LOG.md.

## Validation

Build:

```powershell
dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj --no-restore -c Release --artifacts-path Logs/label-printing-artifacts -v:q
dotnet Logs/label-printing-artifacts/bin/PosOffline.Browser/release/PosOffline.Browser.dll --customer-display
dotnet vstest Logs/label-printing-artifacts/bin/GaoApp.Tests/release/GaoApp.Tests.dll '/TestCaseFilter:PosCockpitModernUiContractTests|PosPrimeResponsiveUiContractTests|PosCustomerRewardsHeldUiContractTests|PosRuntimeContextSourceContractTests' '/logger:trx;LogFileName=customer-display-pos-regressions.trx' /ResultsDirectory:TestResults/customer-display
```

The isolated Release build succeeded (final incremental build 0 warnings / 0 errors; the full dependent build reports 10 existing unrelated test warnings). All 27 matching POS source/UI regression checks passed; evidence `TestResults/customer-display/customer-display-pos-regressions.trx`. JavaScript syntax and scoped whitespace checks were run.

Chrome uses a fixture-owned Web instance and disposable SQL LocalDB. Login, store identity Razor rendering, terminal-group joins and payment display events use the real application and SignalR hub. Cart/promotional responses are controlled inside Playwright, and QR/product images are synthetic display fixtures. No real financial payment, printer operation, external bank account or configured store data is used. This verifies presentation and event handling, not bank processing or production multi-client acceptance.

The probe covers the configured store identity; idle, six-line cart, quantity update and latest-product selection; cash/QR/cash transitions; partial and final success; reset; retained totals on a failed screen read; 22-line scrolling and safely displayed HTML-like product text. Viewport checks cover 1920×1080, 1440×900, 1366×768, 1024×768, 800×600, 768×1024 and 390×844, with payment content checks preventing QR/amount/content clipping. The compact 800×600 QR layout was corrected after screenshot inspection. Promotion text/full-width image/failing-image fallback and idle portrait checks are included. No JavaScript page errors in the completed primary run.

Screenshots are in `TestResults/customer-display/browser/`. Key examples: `idle-1440.png`, `cart-1440.png`, `cart-1024x768.png`, `cart-390x844.png`, `payment-cash.png`, `payment-qr-1440x900.png`, `payment-qr-800x600.png`, `payment-partial.png`, `payment-success.png`. Store/product/amount/account examples shown are fixture data. Desktop/cart/mobile/QR/success screenshots were visually inspected. Video playback on the user's actual secondary monitor and hardware QR scanning remain deployment acceptance checks; the existing video player path is retained.

## Handoff and rollback

**READY FOR COORDINATOR REVIEW.** No independent review, GitHub CI pass, commit or deployment is claimed. Run the updated application and reload the customer display with Ctrl+F5. Existing store identity and promotion settings supply its real content.

Rollback only this task's CustomerDisplay view/CSS/JS, its controller identity read, original SVG and browser-probe additions, preserving all pre-existing edits and data. No migration or data rollback is required.

## Revision B — Promotions alongside checkout

User requested the supermarket pattern where advertisements shrink to one side while the customer follows the sale. Level B presentation revision on the same branch/base. Allowed files: CustomerDisplay.cshtml, its CSS/JS, the existing customer-display.browser.cjs probe and this handoff/DECISION-LOG.md. Read the existing media endpoint consumer, rotation/countdown/video lifecycle, cart and payment rendering, responsive styles and browser fixture. Server controllers, permissions, sale/payment contracts and database schema remain outside this revision.

- Active promotions occupy a 32% rail beside the itemized cart and fixed totals. The rail replaces the large latest-product card; the updated product is still highlighted in the cart. Without usable promotions, retain the product-card layout.
- Use one mounted media player for idle and checkout. Scanning items, changing quantities or returning to idle changes layout without replacing the current image/video. Duration-based rotation, video completion and promotion countdown continue while selling. Fullscreen promotion settings expand only in idle.
- Existing real-time promotion updates work during checkout too; unchanged updates and temporary feed failures retain the player. Ignore obsolete overlapping feed responses. Failed media is skipped, reverting to the product layout if the playlist becomes empty.
- At widths of 1200px or more, cash/QR/acknowledgment panels leave the ad rail visible. At narrower sizes payment fills the workspace to preserve readable amounts and QR size. Phones below 700px use a compact promotional strip above the cart.
- Validation: isolated Release build passed with 0 warnings/errors; the extended real Chrome/Razor/SignalR probe passed, and all 27 existing POS regression checks passed again. Verified non-overlapping promotion/cart layouts at seven sizes, payment/QR fit at eight, live updates and removal, feed failure retention, rotation/countdown, unchanged player identity across cart updates/reset, actual synthetic muted-video playback across idle/cart and unavailable-media fallback. Only fixture-owned LocalDB and synthetic display data were used. Promotion notifications are sent by real authorized admin create/toggle actions with antiforgery protection, because client-sent promotion events are correctly rejected by PosHub. The fixture's cart-state wait was corrected before the completed run; no production authorization changes were made.

Visual inspection covered the 1440px cart rail with text/image, the 800×600 compact cart, 390px phone strip, 1200×700 QR with adjacent advertising, cash and success panels. Evidence: `TestResults/customer-display/browser/cart-promotion-1440x900.png`, `cart-promotion-800x600.png`, `cart-promotion-390x844.png`, `cart-promotion-image.png`, `cart-promotion-video.png`, `payment-qr-1200x700.png`, `payment-cash.png`, `payment-success.png`, `cart-missing-promotion-fallback.png`. Actual store media, physical second-monitor playback and hardware QR scanning remain user-environment acceptance checks. JavaScript syntax and scoped whitespace checks passed.

**READY FOR COORDINATOR REVIEW.** No commit, deployment, independent review or GitHub CI result claimed.

No new promotion configuration or migration is required. Rollback only Revision B's view additions, conditional CSS, player lifecycle and browser-probe changes, retaining the original redesign and unrelated workspace edits.

## Revision C — Make the latest product unmistakable

User requested stronger emphasis for the just-selected product in the customer-facing cart. Bounded styling revision on the same branch/base. Allowed files: pos-customer-display.css and this handoff/DECISION-LOG.md. Read the existing changed-line detection/rendering and all cart breakpoints; preserve its current ordering, update semantics, totals and promotion player.

The existing `.is-changed` row now has a pale-lime card background, visible outline and dark leading edge. Its thumbnail is larger, name/amount are bolder and larger, and the high-contrast “VỪA CẬP NHẬT” label remains visible on phones. A short single entry effect respects reduced-motion preferences. Highlighting persists until the next changed product under the existing display logic; no blinking or new timers.

Validation used the unchanged Chrome display probe and screenshot review; no new tests were added for this CSS-only adjustment. Build passed with 0 warnings/errors. The complete existing browser probe passed, including seven cart sizes, eight QR sizes and promotion/video transitions. Visually inspected `cart-1024x768.png`, `cart-promotion-1440x900.png`, `cart-promotion-800x600.png` and `cart-promotion-390x844.png` under `TestResults/customer-display/browser/`; the highlighted row is clear and totals remain visible. Scoped whitespace checks passed. No runtime JavaScript, backend, permissions, payment or data changes. Rollback only Revision C's changed-row CSS rules. **READY FOR COORDINATOR REVIEW**; no commit or deployment.

## Revision D — Preserve the product card alongside advertisements

The user clarified that shrinking ads must not remove the separate latest-product card shown in their screenshot. This supersedes Revision B's product-panel replacement. Bounded layout correction on the same branch/base. Allowed files: pos-customer-display.css, the existing customer-display.browser.cjs probe and this handoff/DECISION-LOG.md. Read the Razor structure, full product/advertising/cart layout and responsive breakpoints, and the existing browser assertions. JavaScript display state, media player, backend and monetary contracts remain unchanged.

- Wide displays (1400px+) present product details, cart/totals and promotions in three separate columns.
- Narrower displays stack the product card above the advertising panel beside the cart. Short screens use a compact image/details arrangement, retaining name, quantity, unit price and line total. Phones stack a compact product card, promotion strip and cart.
- The current-product highlight and one continuous promotion player are retained. Payment still prioritizes QR/amount readability, with ads beside payment on wide displays. Fullscreen ads remain an idle-only layout.
- Extend the existing browser scenario to require every product detail to fit inside its visible card, alongside non-overlapping ads and cart at seven sizes. Verify quantity changes update the visible product total. Existing payment/video/rotation/fallback checks remain.

Release build passed with 0 warnings/errors. The extended Chrome/Razor/SignalR probe passed: every product detail fits inside its visible card at all seven cart sizes, alongside separate ads and fixed totals; QR checks pass at eight sizes. The existing continuous-video, rotation, countdown, update/removal and fallback scenarios also pass. Data and admin actions were confined to the disposable fixture SQL/Web instance.

Visually reviewed `cart-promotion-1440x900.png`, `cart-promotion-1024x768.png`, `cart-promotion-800x600.png`, `cart-promotion-390x844.png`, `cart-promotion-product-updated.png` and `payment-qr-1440x900.png` in `TestResults/customer-display/browser/`. Product changes update the image, name, quantity and total while the ad player remains mounted. Node syntax and scoped whitespace checks passed.

**READY FOR COORDINATOR REVIEW.** No database, permission, payment, media lifecycle or deployment changes. Rollback only this revision's CSS and probe assertions, preserving the earlier product-row emphasis and other workspace changes.

## Revision E — Essential information and stronger contrast

User requested a less pale background, clearer text and removal of unnecessary steps/copy, specifically the cart/payment/completed journey and the numbered welcome instructions. Bounded presentation revision on the same branch/base. Allowed files: CustomerDisplay.cshtml, its CSS/JS and this handoff/DECISION-LOG.md. Read the rendered sections, styles/breakpoints and related JS references. Retain the three product/cart/promotion areas from Revision D and all payment data and media behavior.

- Remove numbered steps, welcome instructions, generic retail slogans, repeated headings, decorative promotion captions, product reassurance, success decorations/footer and duplicate thank-you lines. Idle keeps a short greeting with store identity in the header; ads show their configured content. Keep meaningful totals, product details, customer information, bank/QR instructions and transaction status.
- Use a more distinct gray-green canvas with white content panels and dark forest text. Darken secondary labels (quantity/price, customer details, discount/settlement, connection status and transfer instructions); strengthen the selected-row background/outline. Increase small operational text and retain bright labels on dark product/payment cards.
- Remove the unused journey helper/calls and CSS selectors for deleted markup. Simplify the QR title to its payment method. Keep the current-product card, continuous promotion player and responsive arrangement unchanged.

Release build passed with 0 warnings/errors. Syntax and scoped whitespace checks passed. Static contrast calculations for the actual theme pairs give body 10.09:1, line metadata 6.87:1, product labels 9.38:1, footer 5.33:1, selected-row metadata 4.86:1 and QR labels 6.43:1. These checks cover system colors, not user-configured advertisement colors. Validation reused the unchanged Chrome/Razor/SignalR probe; no new tests were added for this presentation-only revision. The full probe passed, including simultaneous product/cart/ad visibility at seven sizes, QR at eight sizes and all existing payment, media-continuity and fallback cases.

Visually inspected `idle-1440.png`, `cart-promotion-1440x900.png`, `cart-promotion-390x844.png` and `payment-qr-1440x900.png` in `TestResults/customer-display/browser/`. The removed steps/copy are absent, transaction details remain visible and the clearer theme renders correctly on desktop and phone. All browser data belonged to the disposable fixture. **READY FOR COORDINATOR REVIEW**; no commit or deployment.

No payment commands, sale calculation, promotion settings, permissions, schema or real-store data changes. Rollback only this revision's markup, theme and unused journey-helper removal, preserving Revision D's layout and other dirty workspace changes.
