# Quản lý mẫu bill POS và máy in client

> Cập nhật 28/09/2026: mẫu mặc định chuyển sang dùng chung toàn cửa hàng và chỉ admin được cấu hình. Phần chọn mẫu riêng từng quầy bên dưới là mô tả phiên bản cũ; xem [hướng dẫn hiện tại](RECEIPT-STORE-DEFAULT.md). Máy in vẫn được cấu hình riêng tại quầy.

Status: READY FOR COORDINATOR REVIEW. User authorized the feature on 2026-09-10 and clarified that clients can run Linux. Implementation and local verification are complete. The initial handoff did not modify the live database; the user subsequently explicitly approved the bounded local migration repair recorded below. No commit, deployment, client software installation or physical print was performed.
Level C: additive tenant-owned template storage and integration with offline POS.
Branch/base: fix/r2-4-c1-invoice-identity-uniqueness. Base/expected parent: db6a6c5e98b69472624eeb31aa6be2161576c3d2. Preserve the existing dirty workspace.

## Scope

Manage customer POS receipts with 45 mm, 80 mm, A6, A5, A4 paper and several built-in layouts. Preview, duplicate, edit and delete store-owned designs; choose a template for this client/terminal. Keep identical rendering online/offline. Client printer preference is local to browser profile + store + terminal; never enumerate server printers as client printers. Standard browser printing remains available; installed-printer enumeration/routing uses an optional local QZ Tray connection. No QZ installation, signing key distribution or physical print is performed automatically. Electronic invoice provider forms and tax issuance are separate from these POS receipt templates.

Allowed files: new receipt template entity/configuration/migration/designer/snapshot; printing DTO/service/controller/views/assets; permission catalog/codes and menu seeder; existing POS receipt endpoints, POS header links, offline bootstrap/printing integration, ACB print page routing; relevant tests/browser probe and this document. Other existing changes remain locked.

Mandatory reads: development workflow; current POSReceipt and POSController print paths, OrderReceiptDto; POS offline and ACB print paths; tenant filters/authorization/menu seed; disposable SQL/browser fixtures; official browser print and QZ documentation.

Validation: schema snapshot consistency; store isolation, access checks, row-version conflicts and input validation; receipt rendering/escaping and paper sizes; client preference isolation and printer routing without real printing; Chrome visual and offline integration verification using disposable DB/server. Build to isolated artifacts. Physical paper, driver margins and printer delivery require client acceptance.

Rollback: revert only this feature's diff after preserving template records and offline pending data. Do not drop tables or clear browser storage automatically. Handoff may report local verification only, not independent review or CI PASS.

## Implementation handoff

- Fifteen built-in combinations: Hiện đại, Thanh lịch, Gọn gàng across 45 mm, 80 mm, A6, A5 and A4. The store can create, duplicate, edit and soft-delete additional designs with title, header/footer, color and optional customer/cashier/SKU/payment fields. This is a configurable receipt library, not a free-form HTML or drag-and-drop designer.
- Store-owned `PosReceiptTemplates` records have audit fields and rowversion. All reads and writes enforce StoreId; stale updates/deletes return a conflict. Viewing/selecting requires `pos.order.reprint`; modifying the shared library also requires `system.receipttemplate.manage`.
- Shared renderer covers management preview, online POS receipts, receipt reprints, ACB print windows and offline receipts. It uses the receipt's existing monetary totals and escapes template and business text. An offline manual transfer remains explicitly cashier-confirmed; printing does not assert bank verification.
- Each browser profile saves the selected template snapshot, printer name, copies and color mode separately for each store/terminal. Editing the store library does not unexpectedly replace an already selected client snapshot. Pressing “Dùng mẫu & máy in này” applies a new selection.
- QZ Tray 2.2.6 connector uses a secure localhost WebSocket to enumerate and target the client's installed printers. The exact selected printer is checked before sending; missing printers and print errors do not silently switch printers or automatically repeat a job. A successful send acknowledges submission to QZ, not delivery of paper.
- The default browser dialog remains available. QZ is optional client software and can show trust/print approval dialogs. Trusted signing for printing without those dialogs is not configured. The connector and LGPL license are included under `GaoApp.Web/wwwroot/Admin/js/printing`.

## Local validation evidence

Final source remains on the branch and HEAD above; the pre-existing dirty workspace is preserved. These are local results, not independent review or GitHub CI acceptance.

| Check | Result |
| --- | --- |
| Release build of `GaoApp.Tests.Browser/PosOffline.Browser.csproj`, isolated artifacts | Passed; final incremental build: 0 warnings, 0 errors |
| Selected .NET/SQL suite: receipt templates, model/snapshot consistency, offline journal, POS payment/UI contracts, permissions and menus | 279 passed, 0 failed |
| JS suite: receipt rendering/printer routing plus existing offline, QR and ACB regressions | 71 passed, 0 failed |
| Chrome integration with disposable SQL and actual POS assets/IndexedDB/service worker | Passed, including selected template/printer before and after offline replay |
| Chrome layout inspection | All 15 layout/size combinations fit their table width; screenshots and PDFs generated for five paper sizes |

The Chrome printer bridge is mocked: it enumerates simulated Windows/Linux printer names and checks the QZ job's exact printer, paper size and copy count. It does not run a Linux OS, a real QZ process or a physical printer. Real driver margins, font rendering, long/multipage bills and printer output still require client acceptance.

Repeatable commands from repository root (build outputs are isolated):

```powershell
dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj --no-restore -c Release --artifacts-path Logs/pos-offline-artifacts -v:q
dotnet vstest Logs/pos-offline-artifacts/bin/GaoApp.Tests/release/GaoApp.Tests.dll '/TestCaseFilter:FullyQualifiedName~ReceiptTemplateSqlServerTests|FullyQualifiedName~Current_model_and_migration_snapshot_have_no_differences|FullyQualifiedName~PosOfflineSqlServerTests|FullyQualifiedName~PosPrimeResponsiveUiContractTests|FullyQualifiedName~PosCheckoutPaymentUiContractTests|FullyQualifiedName~GaoApp.Tests.Payments|FullyQualifiedName~AdminMenu|FullyQualifiedName~Permission' '/logger:trx;LogFileName=receipt-templates.trx' /ResultsDirectory:TestResults/receipt-templates
node --test GaoApp.Tests/Ui/receipt-printing.test.cjs GaoApp.Tests/Ui/pos-acb-realtime.test.cjs GaoApp.Tests/Ui/pos-offline-worker.test.cjs GaoApp.Tests/Ui/pos-offline.test.cjs GaoApp.Tests/Ui/pos-qr-history.test.cjs GaoApp.Tests/Ui/pos-qr-installments.test.cjs GaoApp.Tests/Ui/pos-collection-idempotency.test.cjs GaoApp.Tests/Ui/acb-confirmation-lookup.test.cjs
dotnet Logs/pos-offline-artifacts/bin/PosOffline.Browser/release/PosOffline.Browser.dll --receipt-templates
```

Evidence: `TestResults/receipt-templates/receipt-templates.trx`; Chrome screenshots/PDFs in `TestResults/pos-offline/browser/receipts/`. The final Chrome rerun includes receipt status and payment-reference rendering. Its replay produces two orders, two receipts, total 100 and remaining stock 95 in disposable SQL; offline and online prints retain the same custom 45 mm template and selected client printer.

## Cập nhật ứng dụng

1. Đồng bộ các đơn offline còn chờ và chuẩn bị bản sao lưu database theo quy trình vận hành trước khi cập nhật. Không xóa dữ liệu trang web/IndexedDB để nâng cấp.
2. Đóng gói Web và Migrator cùng source theo [runbook triển khai](STAGING-DEPLOYMENT-RUNBOOK.md). Chạy Migrator của bản mới bằng cấu hình/credential migration đúng database, rồi mới mở Web của bản mới. Web Production không tự migrate.
3. Migration mới `20260910002000_AddPosReceiptTemplates` đứng sau `20260909210000_AddPosOfflineJournal`, chỉ thêm bảng mẫu in và chỉ mục/khóa ngoại tương ứng. Migrator còn cập nhật danh mục quyền và menu. [SQL tham khảo](../pos-receipt-templates-upgrade.sql) chỉ cập nhật schema và lịch sử migration; chạy riêng SQL đó không thay thế phần seed quyền/menu.
4. Mở lại POS khi còn kết nối server để tải bộ JavaScript và dữ liệu offline của bản mới. Không trộn Razor/assembly cũ với static assets mới.

## Thiết lập từng máy client Windows / Linux

1. Cài máy in và driver trong hệ điều hành của chính client, kiểm tra trang in thử của hệ điều hành. Với Linux, máy in cần xuất hiện trong danh sách máy in hệ thống/CUPS.
2. Cài và mở [QZ Tray](https://qz.io/download/) trên client. Dùng hướng dẫn [cài đặt chính thức](https://qz.io/docs/using-qz-tray) cho hệ điều hành tương ứng; phần in HTML cần runtime Java/JavaFX phù hợp theo [tài liệu pixel printing](https://qz.io/docs/pixel). Hoàn tất thiết lập chứng chỉ localhost của QZ theo trình cài đặt, không tắt kiểm tra TLS của Chrome.
3. Trong POS, mở **⋯ → Mẫu hóa đơn & máy in** (hoặc `/admin/receipt-templates`). Chọn kiểu trình bày và khổ giấy; muốn sửa mẫu dựng sẵn thì lưu thành mẫu mới hoặc nhân bản.
4. Tại **Máy in của quầy**, chọn **Máy in đã cài trên client · QZ Tray**, bấm **Kết nối máy in**, chấp nhận yêu cầu từ QZ, rồi chọn đúng máy in và số bản. Mẫu bill dùng chế độ đen trắng.
5. Bấm **Dùng mẫu & máy in này**, sau đó **In thử**. Kiểm tra trên giấy thật: chiều rộng, lề, tiếng Việt, tiền hàng, ngắt trang/cắt giấy. Khổ 45 mm ở đây là chiều rộng giấy; nếu thiết bị dùng cuộn 58 mm với vùng in khoảng 45 mm thì cần đối chiếu driver trước khi áp dụng.
6. Thiết lập riêng trên từng client và từng quầy. Chọn máy in trên client A không thay đổi client B. Đổi Chrome profile hoặc địa chỉ website sẽ có bộ thiết lập khác. Không xóa dữ liệu trình duyệt khi còn đơn offline chờ đồng bộ.

QZ phải đang chạy để gửi lệnh in trực tiếp. Khi mất kết nối server, POS dùng mẫu và thiết lập đã lưu trên client. Nếu QZ hoặc máy in gặp lỗi, hóa đơn đã ghi nhận vẫn giữ nguyên; kiểm tra hàng đợi máy in trước khi bấm in lại để tránh hai bản giấy. Bản này vẫn có thể hỏi xác nhận QZ: [tài liệu ký lệnh để bỏ hộp thoại](https://qz.io/docs/signing).

Nghiệm thu còn lại trên thiết bị thật: một client Windows và một client Linux chọn hai máy in khác nhau; in mỗi khổ được thiết bị hỗ trợ; thử đơn dài, offline rồi đồng bộ/in lại; đóng QZ hoặc gỡ máy in đã chọn để xác nhận thông báo lỗi. Không coi tên máy in Linux giả lập trong test là bằng chứng đã in trên Linux.

## Sửa lỗi khởi động Development ngày 2026-09-10

User reported Visual Studio “Unable to connect to web server 'https'. The web server is no longer running.” Log at 00:38:15 and 00:38:39 identifies SQL error 2714 creating `PosReceiptTemplates`. Read-only inspection of the user's local SQL Express database confirms the original generated migration `20260909171948_AddPosReceiptTemplates` was already applied while implementation was in progress. The implementation then renamed that migration to `20260910002000_AddPosReceiptTemplates` and widened `DefinitionJson` from nvarchar(4000) to nvarchar(max). This ID change caused Development startup to attempt creating the existing table; it is not evidence of a broken HTTPS certificate.

Recovery scope authorized by the user's startup-error report: a bounded one-time schema/history reconciliation after verification, plus regression tests and this evidence. [Repair SQL](../pos-receipt-templates-legacy-repair.sql) accepts only the known migration IDs/version and validates all receipt columns, primary key, store index and foreign key. It widens JSON storage and updates the existing history ID atomically, preserves template records, rejects ambiguous states and is repeatable after success. It does not change the normal migration or bypass startup validation. Stop Web/Migrator against the database before applying this recovery. Do not use the ordinary create-table upgrade script to repair the legacy state.

Recovery validation: Release build succeeded (0 errors, 10 pre-existing test warnings). Seven selected tests passed: five legacy-repair cases, the existing template HTTP/SQL integration test and model/snapshot consistency. Evidence: `TestResults/receipt-templates/receipt-legacy-repair.trx`. The repair tests preserve existing Vietnamese template text, identity and rowversion, verify repeated execution and subsequent EF migration startup, reject incompatible columns/missing or conflicting history, and verify transaction rollback when a history update fails after column widening. Tested repair SQL SHA-256: `451B0F41BED84B9AFB81FE4C744BDB3D4063FCA54DB528237C4A262427616831`.

Recovery application status: completed locally after the user explicitly replied “Cho phép”. The first execution attempt had been rejected before execution by automatic approval review pending explicit database-change approval; the approved retry used the exact tested script/hash above. The configured local SQL Express database `GaoAppDb` now records `20260910002000_AddPosReceiptTemplates` (EF 8.0.29), and `DefinitionJson` is nvarchar(max), with COL_LENGTH = -1. Before/after counts match: 0 templates, 3 orders, 4 order payments. No tables or business records were dropped or cleared.

Startup verification at 2026-09-10 00:48:55 local time used the existing Release Web assembly, Development configuration, and the same URLs as Visual Studio's `https` profile. HTTPS `/health/ready` and `/health/live` on port 7051 returned 200 Healthy using normal certificate validation; database and storage checks passed. HTTP `/health/live` on port 5100 also returned 200. Web remained running after those checks. Only the test Web process was then stopped so Visual Studio can reclaim its ports. Evidence: `Logs/receipt-startup-20260910-004855.stdout.log` and `.stderr.log`. No HTTPS certificate/configuration change was needed; the user can dismiss the old Visual Studio dialog and start the existing `https` profile again.

## Sửa khung xem trước và cấu hình mẫu Thanh lịch

User reported the Thanh lịch configuration/preview on 2026-09-10. Chrome reproduction confirmed the iframe retained a 390 px displayed width after its stage shrank to 346 px. The old zoom calculation ran only when selecting/editing a template. Height also needed remeasurement after Chrome changed the zoom factor. The classic renderer additionally overrode selected accent colors with hard-coded gray/black values.

The bounded fix in `receipt.studio.js` observes the preview stage's size, recalculates its fit using the available content width, and measures the receipt height after each scale/load. The preview's physical paper width remains unchanged, short edits shrink its height again, and selecting another template resets the preview scroll position. CSS reserves stable scrollbar space to avoid width changes when long receipts require scrolling. Scrolling long previews remains available in the outer stage; the iframe itself fits its content. `receipt.templates.js` now applies the configured accent to the classic brand, total and rules; local fallback preset colors match the server's existing presets.

Local verification: all 7 receipt JavaScript tests passed; the full extended Chrome/offline probe passed, including all five classic paper sizes while changing the browser viewport between 1440, 1000 and 390 px, long/short footer edits, actual computed accent colors, and offline/server print selection after replay. The original regression failed before the fix with the 390/346 px width mismatch. The first corrected probe exposed the height remeasurement issue; the final probe passes. A test-only iframe load race in the color assertion was also corrected. Screenshots: `TestResults/pos-offline/browser/receipts/classic-preview-{45,80,A6,A5,A4}.png` and `classic-configured-color.png`.

Status: READY FOR COORDINATOR REVIEW. This follow-up changes only receipt JS/CSS, the existing browser probe and this handoff; no live database change or physical print. Reload the template management page with Ctrl+F5 to receive the updated versioned assets.

## Thông tin tiệm dùng chung trên hóa đơn

User requested an editable place for the receipt's store name, address and phone. A new **Thông tin tiệm trên hóa đơn** section appears at the top of `/admin/receipt-templates`, with a separate **Lưu thông tin tiệm** button. The settings apply to all receipt templates and terminals in the current store. The preview and gallery use the store's real saved name; the former fictional GẠO · MARKET address/phone are no longer injected into the studio. Empty address/phone fields omit those lines. Unsaved identity edits update the preview but block applying/printing a sample until saved.

Scope: Store receipt identity fields and EF mapping/new migration/designer/snapshot; existing receipt template service/controller/view/JS/CSS; POS receipt DTO mapping and offline bootstrap/status/cache/printing; SQL, JS and browser verification; additive SQL upgrade and this handoff. Existing operational store name, tenant/subdomain and electronic invoice configuration are unchanged. Permission to edit is `system.receipttemplate.manage` plus existing receipt access; the API resolves the store on the server and ignores client-supplied store IDs. Store rowversion prevents stale/concurrent edits; normal UpdatedAt/UpdatedBy fields track changes.

Fixed migration ID **20260910012000_AddStoreReceiptIdentity** adds nullable `Stores.ReceiptName` (200), `ReceiptAddress` (300) and `ReceiptPhone` (50). The ID was fixed when created and existing migrations were not renamed. Existing store rows retain their values; an unset ReceiptName falls back to Store.Name. The model/snapshot consistency check passes. [Generated idempotent upgrade SQL](../pos-receipt-store-identity-upgrade.sql) is reviewable but was not applied to the configured application database during this change. Do not run Down after entering receipt identity data without preserving those fields.

Online receipts and reprints read the saved store receipt identity. Offline bootstrap includes its current values/version; existing POS status polling refreshes only a changed version and persists it through the same serialized IndexedDB writer, preserving pending operations. Disconnected clients use the last identity they received and refresh on reconnection. Reprints use the current available identity, not a historical snapshot of store contact details.

Local evidence: Release build passed with 0 errors and 10 pre-existing test warnings. **12 .NET/SQL tests passed**, including migration over an existing store, template security, identity access control, cross-store isolation, spoofed store ID, validation, CSRF, stale/concurrent writes, optional-field clearing, online receipt mapping and offline bootstrap/status values. Legacy repair and model/snapshot checks also passed. TRX: `TestResults/receipt-templates/receipt-store-identity.trx`. **25 JavaScript tests passed** across receipt printing, offline core and service worker, including escaped store contact text.

The extended Chrome probe passed: editing the three fields updates preview, saving persists across another page and template switch, the already-open POS receives the new identity, and the same name/address/phone appear in the captured offline and post-sync QZ print payloads. Existing classic layout/size, offline restart, manual QR, lost-response replay and disk-failure checks passed. Physical printing remains mocked. Screenshot: `TestResults/pos-offline/browser/receipts/store-identity-settings.png`; all sample merchant details in browser tests belong only to disposable test databases.

Status: READY FOR COORDINATOR REVIEW. To use this change in the user's Visual Studio Development setup, stop and start Web with F5 so the new C#/Razor build and automatic Development migration are loaded, then reload the template page. Production uses the matching Migrator before updated Web, as in the deployment runbook. This follow-up did not restart the user's Web, apply the new migration to their configured database, or save merchant details there.

## Cập nhật mẫu in đen trắng — 12/09/2026

Theo yêu cầu máy in bill trắng đen, cả 15 mẫu (Hiện đại/Thanh lịch/Gọn gàng × 45/80/A6/A5/A4) dùng mực đen `#000000` trên nền trắng. Bỏ chữ xám, đường kẻ xám, màu nhấn và nền tô của đầu bảng. Font dùng chung Arial/Helvetica/sans-serif với độ dày nền 600; tên tiệm, tiêu đề, mã hóa đơn, tên hàng, tổng cộng, đã thanh toán, còn thiếu, tiền thừa và số tiền theo phương thức thanh toán được nhấn 800. Tăng cỡ chữ thân bill 45 mm từ 9 lên 10 px, 80 mm từ 11 lên 12 px. Mẫu Thanh lịch dùng cùng font dễ đọc, giữ khác biệt bằng đường kẻ kép và bố cục.

Bỏ điều khiển Màu nhấn và In màu trong studio; thumbnail bill cũng chỉ có chữ/đường kẻ đen. Các màu của nút và khung quản trị không phải nội dung được in. Bộ dựng dùng chung cho preview, bản in trình duyệt, QZ và offline luôn chuẩn hóa accent về đen. API danh sách chiếu mẫu màu cũ sang đen khi đọc, giữ nguyên JSON và rowversion đang lưu; khi người dùng lưu mẫu thì định nghĩa mới lưu màu đen. Giữ trường AccentColor để tương thích JSON cũ, vẫn kiểm tra định dạng đầu vào. Không sửa dữ liệu mẫu cũ hàng loạt và không thêm migration.

Lựa chọn máy in, khổ giấy, số bản và nội dung mẫu giữ nguyên. Thiết lập màu cũ trên trình duyệt được bỏ qua; các lệnh QZ đều gửi grayscale. Mẫu màu lưu trong lựa chọn offline cũng qua cùng bộ chuẩn hóa.

Chữ dày làm tăng bề rộng con số, vì vậy khổ cuộn được phân bổ lại cột và số tiền không ngắt giữa chữ số. Các khoản quan trọng ở 45 mm trình bày nhãn và giá trị trên hai dòng trong cùng ô rộng; tổng cộng có đường kẻ kép. Tổng tiền luôn lấy từ dữ liệu hóa đơn, không tính lại trên giao diện.

Xác minh riêng, không Run All:

- 9/9 test JavaScript: `Logs/receipt-monochrome-js.log`.
- Test HTTP/SQL mẫu hóa đơn pass, bao gồm quyền, cửa hàng, validation, phiên bản, chuẩn hóa lưu mẫu mới và đọc mẫu màu cũ không ghi lại dữ liệu: `TestResults/receipt-templates/receipt-monochrome.trx`.
- Probe Chrome `--receipt-templates` pass: thư viện, thay đổi cấu hình, fit preview ở 1440/1000/390 px, 15 mẫu/khổ giấy, lệnh in QZ giả lập cho offline và in lại từ máy chủ. Log `Logs/receipt-monochrome-browser.log`.
- Probe bố cục sau khi chỉnh cột: `receipt-monochrome.browser.cjs` pass 15 mẫu với hóa đơn thường và số tiền lớn, kiểm tra chữ đen/nền trắng/độ dày, số tiền và số lượng nguyên dòng, cả media screen và print. Log `Logs/receipt-monochrome-layout.log`; PNG/PDF tại `TestResults/receipt-templates/monochrome`.
- Build riêng: `Logs/receipt-monochrome-build.log`. Không triển khai host hoặc gửi lệnh tới máy in thật.

## Thêm mẫu Tên hàng rộng · 80 mm — 12/09/2026

Thư viện có thêm đúng một mẫu `itemwide-80`, tổng cộng 16 mẫu. Tên mỗi sản phẩm in đậm trên toàn chiều ngang, tự xuống dòng khi dài; hàng bên dưới có bốn cột SL, ĐVT, Đơn giá, Thành tiền. Đường kẻ đứt phân cách sản phẩm. Tên và hàng số liệu nằm cùng nhóm để tránh tách sang hai trang khi in. SKU và giảm giá dòng vẫn hiển thị theo cấu hình/dữ liệu; số lượng lẻ, đơn vị bán và thành tiền lấy từ hóa đơn, không tính lại tổng.

Mẫu chỉ dùng khổ 80 mm. Chọn kiểu Tên hàng rộng trong trình chỉnh sửa tự chuyển giấy sang 80 mm và khóa các khổ không phù hợp; đổi lại kiểu khác mở lại đủ năm khổ. API và bộ dựng client cùng chuẩn hóa kiểu này về 80 mm. Có thể lưu thành mẫu riêng, chọn cho quầy và dùng khi offline qua bộ dựng hiện tại. Chữ đen trên nền trắng, độ dày 600/800 và QZ grayscale như các mẫu đã cập nhật. Không thêm migration.

Xác minh riêng, không Run All:

- 10/10 test JavaScript pass, gồm mẫu 80 mm mới và lệnh QZ từ lựa chọn offline: `Logs/receipt-itemwide-js.log`.
- Một test HTTP/SQL pass: danh sách 16 mẫu, lưu/đọc lại kiểu mới, chuẩn hóa khổ 80 mm, quyền, cô lập cửa hàng và phiên bản chỉnh sửa. Kết quả: `TestResults/receipt-templates/receipt-itemwide.trx`.
- Probe Chrome `--receipt-templates` pass: bộ lọc 80 mm có bốn mẫu sẵn, chuyển kiểu/khổ, tên chiếm đủ bốn cột, các giá trị dưới tên, lưu mẫu riêng và tải lại; các luồng in đã có vẫn pass. Log: `Logs/receipt-itemwide-browser.log`.
- Probe bố cục pass 16 mẫu ở media screen/print, số tiền lớn; mẫu mới thêm trường hợp tên dài, đơn vị dài, số lượng lẻ và phiếu trống: `Logs/receipt-itemwide-layout.log`.
- Build riêng thành công: `Logs/receipt-itemwide-build.log` (0 lỗi, 10 cảnh báo test có sẵn). Xem mẫu tại `TestResults/receipt-templates/monochrome/itemwide-80.png`, `itemwide-80-long-names.png` và `itemwide-80.pdf`.

Cần build/chạy lại Web để nạp danh sách mẫu từ C#/Razor mới, rồi mở `/admin/receipt-templates`, lọc 80 mm và chọn **Tên hàng rộng · 80 mm**. Thay đổi này không tự khởi động lại Web đang chạy, triển khai host hoặc gửi lệnh tới máy in thật.

## Kiểm tra số lượng và số tiền lớn trên mẫu 80 mm — 12/09/2026

Probe bổ sung tái hiện tràn cột ở thành tiền 99.989.990.001 và số lượng 99.999; trước đó bộ thử mẫu chỉ kiểm tra số lượng 100 và thành tiền 123.456.789. Bản ghi trước sửa: `Logs/receipt-itemwide-limits-before.log`.

Bộ dựng mẫu `itemwide` cân lại bốn cột theo số chữ số và độ rộng bảo thủ của chữ 12 px ở độ dày 600/800. Nếu một dòng có quá nhiều chữ số để giữ bốn cột, riêng dòng đó chuyển sang các ô có nhãn SL, ĐVT, Đơn giá, Thành tiền dưới tên hàng; giá trị vượt nửa khổ được dành một dòng rộng. Dòng hàng bình thường tiếp tục dùng bốn cột, kể cả trong giỏ có hàng số tiền lớn. Không thu nhỏ chữ, cắt số, rút gọn thành triệu/tỷ hoặc thay đổi số tiền. Phần tổng tiền dài chuyển nhãn và giá trị sang hai dòng để nhãn không bị ép hẹp.

Kiểm tra riêng, không Run All: 8 trường hợp Chrome ở media screen và print đều pass, gồm số lượng 1.000.000, số lượng lẻ 1.234.567,89, đơn giá đến 999.999.999.999, thành tiền đến 999.998.999.000.001 và giỏ nhiều hàng thường/lớn. Kiểm tra số liệu giữ nguyên, không tràn ô/khổ 80 mm, tên hàng nằm trên số liệu và giữ cùng nhóm khi in. Script `GaoApp.Tests.Browser/receipt-itemwide-limits.browser.cjs`, log `Logs/receipt-itemwide-limits.log`, ảnh và HTML trong `TestResults/receipt-templates/itemwide-limits/`.

10/10 test JavaScript liên quan in hóa đơn pass (`Logs/receipt-itemwide-limits-js.log`). Probe bố cục 16 mẫu pass (`Logs/receipt-itemwide-limits-regression.log`). Thay đổi chỉ ở bộ dựng JavaScript và kiểm tra trình duyệt; không đổi C#/DB, không in máy thật. Tải lại trang để nhận JS có phiên bản mới.
