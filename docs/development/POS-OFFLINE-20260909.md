# POS offline — BMAD implementation contract

## Host vẫn trả 403 khi giữ đơn sau publish — 2026-09-12

Ảnh host `chonthanh.gaomart.com.vn:8443` cho thấy `POST /admin/pos/cart/current/hold` bị từ chối quyền. Trong `GaoApp.Web/Program.cs`, Web chỉ gọi `MigrateAndSeedDatabaseAsync` khi `IsDevelopment()`. Test dùng database mới đã seed, còn việc copy bản Web lên Production không tự bổ sung quyền vào database hiện có. Đây là nguyên nhân phù hợp với ảnh; chưa đọc database host để phân biệt thiếu định nghĩa quyền hay thiếu gán quyền cho vai trò tài khoản đang dùng.

Script sửa riêng lỗi này: [`../pos-hold-permission-repair.sql`](../pos-hold-permission-repair.sql).

1. Mở script trong SSMS trên host, chọn đúng database của ứng dụng. Mặc định `@StoreSubDomain = N'chonthanh'`, `@Apply = 0`: chạy toàn bộ file để xem database/cửa hàng, quyền giữ đơn của từng vai trò và vai trò của tài khoản. Chế độ này không ghi dữ liệu; sai cửa hàng thì script dừng.
2. Nếu đúng database/cửa hàng và tài khoản dùng ADMIN hoặc CASHIER, đổi `@Apply = 1` rồi chạy lại toàn bộ file. Script tạo định nghĩa `pos.order.hold` nếu thiếu và bổ sung các gán quyền còn thiếu cho hai vai trò này tại riêng cửa hàng đã chọn, trong một transaction. Chạy lại không tạo trùng. Nếu tài khoản dùng vai trò tùy chỉnh, quản trị viên cấp quyền **Giữ và lấy lại đơn POS** qua màn phân quyền của vai trò đó sau khi định nghĩa quyền đã được bổ sung.
3. Reload POS rồi thử giữ và lấy lại đơn. Quyền API được đọc từ database mỗi request; kiểm thử xác nhận cùng phiên đăng nhập hoạt động sau sửa.

Script không chạy Migrator, không đổi schema, giỏ hàng, ca, tiền hoặc tồn kho. Đây là cách áp dụng riêng quyền giữ đơn; cập nhật đầy đủ danh mục quyền vẫn dùng `MandatorySecuritySeeder` trong quy trình Migrator hiện có.

Xác minh: build Release đạt (0 lỗi, 10 warning có sẵn). Hai kiểm thử SQL/API `PosHoldPermissionRepairSqlServerTests` đạt, không skip: mô phỏng thiếu định nghĩa hoặc thiếu gán quyền, tái hiện 403 trước sửa, preview không ghi, sai subdomain bị từ chối, apply hai lần vẫn chỉ thêm đúng quyền/gán quyền cần thiết, không đổi gán quyền của vai trò/cửa hàng khác, giữ và resume thành công trong cùng phiên sau sửa, không phát sinh thu tiền hay đổi tồn kho. Kết quả: `TestResults/pos-hold-host-repair/pos-hold-host-repair.trx`. Chưa chạy script trên database host.

```powershell
dotnet vstest Logs/pos-regression-20260912-artifacts/bin/GaoApp.Tests/release/GaoApp.Tests.dll '/TestCaseFilter:FullyQualifiedName~PosHoldPermissionRepairSqlServerTests' '/logger:trx;LogFileName=pos-hold-host-repair.trx' /ResultsDirectory:TestResults/pos-hold-host-repair
```

## Sửa nút Thanh toán mở chậm sau quét — 2026-09-12

Ảnh người dùng cho thấy đã báo thêm sản phẩm nhưng các nút còn khóa, đồng thời nhãn “Đã thanh toán đủ” vẫn hiện khi còn tiền phải thu. Hai lỗi được tái hiện bằng kiểm thử thực thi JavaScript trước sửa:

- Bridge `PosCommon.setActionPending` / `setBusyScopes` gọi helper `PosState` rồi return sớm, bỏ qua `refreshUiLocks`. Sự kiện lưu journal offline render nút khi scope `cartMutate` còn bận; khi action kết thúc scope đã rỗng nhưng nút vẫn disabled đến lần refresh khác, thường là heartbeat 6 giây. Bổ sung refresh ngay sau khi helper thay đổi state; vẫn giữ khóa trong lúc ghi giỏ, checkout khác và khi mất kết nối không có offline khả dụng.
- `applyDraftPatch` cập nhật các số tổng tiền nhưng bỏ sót `renderSummaryState` và `renderSummaryActionState`, nên nhãn/trạng thái cũ còn giữ sau thêm hàng. Bổ sung hai render cùng lúc với phần tổng tiền.

Thay đổi runtime đợt này chỉ gồm hai dòng bridge trong `pos.common.js` và hai dòng render trong `pos.render.js`. Không đổi API, database, journal hoặc thời gian heartbeat. Các script đều có `asp-append-version`; cập nhật assets của bản chạy và reload POS để nhận bản mới, không cần xóa dữ liệu trình duyệt.

Kiểm thử mới `pos-action-locks.test.cjs`: 8/9 thất bại trước sửa, 9/9 đạt sau sửa; bao gồm khóa lúc scan đang chờ, mở khóa ngay sau success/error/abort, pending-only controls, giữ khóa khác và trạng thái chưa thanh toán khi patch giỏ. Chạy cùng các nhóm barcode/offline/QR/payment: 80/80 đạt. Chrome với Web/SQL tạm xác nhận button đã enabled, `uiLocked=false`, không còn scope bận và click ngay trong cùng lượt hoàn thành thêm sản phẩm phát sự kiện mở modal, không đợi timer/heartbeat. Probe chờ animation modal kết thúc rồi kiểm tra tiếp giữ/resume, restart offline, replay, QR và quota; toàn bộ đạt, không pageerror, SQL đúng 2 đơn/2 khoản thu/tổng 100/tồn 95. `node --check` và `git diff --check` các file runtime thay đổi đạt. Chưa triển khai/restart hệ thống bán hàng.

## Sửa lỗi vận hành POS — 2026-09-12

Phản hồi mới: giữ đơn bị từ chối quyền, mở lại POS chậm, máy quét chọn lại gợi ý của mã trước. Người dùng xác nhận tìm theo tên hoạt động và hỏi liệu khách tiếp theo phải tải lại danh mục hay không. Danh mục chỉ được chuẩn bị khi khởi tạo trang, không tải lại theo mỗi lần thanh toán.

Thay đổi trong đợt sửa này:

- `PermissionCatalog`: bổ sung `pos.order.hold` và `pos.order.discount` đã được controller và cấu hình role CASHIER tham chiếu nhưng thiếu trong danh mục seed. Seeder hiện có sẽ bổ sung quyền cho ADMIN/CASHIER; không bỏ kiểm tra quyền tại API.
- `pos.offline.js`: lỗi 403 của một thao tác không còn bị coi là mất đăng nhập; 401, thay đổi ngữ cảnh quầy/ca và lỗi quyền tại endpoint xác thực ngữ cảnh vẫn chặn phiên. Lỗi replay được giữ trong journal để đối soát.
- Khởi tạo chỉ chờ kiểm tra ngữ cảnh, journal còn chờ và khóa tab. Nếu cần danh mục mới, tải sản phẩm/khách/khuyến mãi ở nền; các giao dịch online được xử lý trước khi lấy snapshot giỏ mới nhất và bật offline. Danh mục cùng phiên vừa tải trong 15 phút được tái sử dụng khi reload. Có journal chờ thì giữ danh mục gốc. Tab thứ hai bị chặn cả khi tab đầu đang chuẩn bị danh mục.
- `pos.render.js`: thông báo đang chuẩn bị offline nhưng có thể tiếp tục bán online.
- `pos.barcode.js`: hủy debounce và vô hiệu hóa response cũ khi đổi nội dung hoặc đóng/chọn gợi ý. Enter chỉ chọn gợi ý thuộc nội dung hiện tại; mã số được xử lý bằng endpoint barcode chính xác. Tìm theo tên và tiền tố số lượng được giữ.
- `pos.offline.core.js`: nhận trường `quantity` mà ô quét thực tế gửi, đồng thời giữ tương thích với `qty`.

Xác minh: build Release ở `Logs/pos-regression-20260912-artifacts` đạt; 71/71 JavaScript test đạt (6 test mới về gợi ý/máy quét và 1 test quantity); 23/23 .NET/SQL/UI tests đạt, không skip (`TestResults/pos-regression-20260912/pos-regression.trx`). Chrome với Web và SQL tạm đạt: màn POS và sửa giỏ hoạt động khi request danh mục đang bị giữ; snapshot offline chứa sửa giỏ trong thời gian tải; reload không gọi lại catalog/customers/promotions; lỗi giữ đơn 403 không khóa đăng nhập; giữ và resume có quyền thành công; khôi phục journal sau reload offline và đồng bộ đúng 2 đơn/2 khoản thu/tổng 100/tồn 95. Không có pageerror trong probe.

Browser probe chạy từ thư mục output `GaoApp.Tests/release` sau khi copy riêng các file `PosOffline.Browser*` từ output của probe, để `TestApplicationBuild` tìm đúng Web assembly ở layout artifacts. Không thay build ứng dụng đang chạy.

Áp dụng: cập nhật đồng bộ Web/assets và chạy seeder bảo mật hiện có (`SecuritySeedData.SeedPermissionsAsync` + `SeedDefaultRolesForAllStoresAsync`, được gọi bởi `MandatorySecuritySeeder`) trên bản mới. Kiểm tra kế hoạch migration của môi trường trước khi dùng Migrator; đợt sửa này không thêm migration. Reload POS để nhận URL JS có hash mới, giữ nguyên IndexedDB/journal. Chưa chạy seeder/migration vào DB bán hàng, chưa restart/deploy server, chưa kiểm chứng máy quét vật lý và chưa tạo commit/PR.

Lệnh kiểm tra:

```powershell
node --test GaoApp.Tests/Ui/pos-barcode-races.test.cjs GaoApp.Tests/Ui/pos-offline.test.cjs GaoApp.Tests/Ui/pos-offline-worker.test.cjs GaoApp.Tests/Ui/pos-acb-realtime.test.cjs GaoApp.Tests/Ui/pos-qr-history.test.cjs GaoApp.Tests/Ui/pos-qr-installments.test.cjs GaoApp.Tests/Ui/pos-collection-idempotency.test.cjs GaoApp.Tests/Ui/acb-confirmation-lookup.test.cjs
dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj -c Release --artifacts-path Logs/pos-regression-20260912-artifacts --configfile Logs/pos-offline-NuGet.Config -p:NuGetAudit=false
dotnet vstest Logs/pos-regression-20260912-artifacts/bin/GaoApp.Tests/release/GaoApp.Tests.dll '/TestCaseFilter:FullyQualifiedName~PosOfflineSqlServerTests|FullyQualifiedName~EndpointSecurityCoverageTests|FullyQualifiedName~SessionRevocationSqlServerTests|FullyQualifiedName~PosPrimeResponsiveUiContractTests' '/logger:trx;LogFileName=pos-regression.trx' /ResultsDirectory:TestResults/pos-regression-20260912
Get-ChildItem -LiteralPath Logs/pos-regression-20260912-artifacts/bin/PosOffline.Browser/release -Filter 'PosOffline.Browser*' -File | Copy-Item -Destination Logs/pos-regression-20260912-artifacts/bin/GaoApp.Tests/release
dotnet Logs/pos-regression-20260912-artifacts/bin/GaoApp.Tests/release/PosOffline.Browser.dll
```

Status: READY FOR COORDINATOR REVIEW — implemented and locally verified. User authorized implementation on 2026-09-09 after discovery. Not committed, independently reviewed, CI verified or deployed.
Level C: durable client storage, retry/idempotency, payments and POS transactions.
Working branch/base branch: fix/r2-4-c1-invoice-identity-uniqueness.
Base commit/expected parent: db6a6c5e98b69472624eeb31aa6be2161576c3d2.
The existing uncommitted working tree is the implementation baseline; do not reset it.
Baseline status and copies of existing integration files: Logs/pos-offline-baseline.

## Accepted behavior

- Each terminal owns its carts. Continue ordinary POS sales when the LAN server cannot respond.
- Persist cart changes and pending requests before reporting success; survive page/browser restart.
- Keep the current POS UI and connectivity banner. Automatic recovery must not overwrite local work.
- Generate transfer QR locally from public bank account details, with explicit manual receipt confirmation.
- Replay pending writes with stable identities. Server-side authorization, pricing, inventory, shift and audit rules remain authoritative.
- Preserve a conflicting sale locally for reconciliation; never silently discard it, change its paid amount or bypass a server validation.
- An offline-capable terminal must previously have prepared its application/data and authorized context. First-time setup and opening a new shift require the server; an already open shift can be recovered offline.

## Implementation and scope

Manual BMAD: discovery, architecture/contract, implementation, local verification, handoff. No separate agent delegation, commit, push, CI verdict or deployment is implied.
Allowed changes: this document; POS offline DTO/entity/configuration/migration/designer/snapshot; offline services/controller/filter; AppUnitOfWork and its interfaces for joining transactions, abort propagation and deferred post-commit work; the POSService invoice post-commit call; ACB manual confirmation provenance/policy; Program registrations; POS Index and POS JavaScript modules; local offline worker and QR encoder asset/license; tests and the standalone browser probe for these paths; decision log append. All other existing work is locked.
Mandatory reads: development workflow, POSController, POSService, POSScreen/OrderDraft/OrderLine DTOs, AppDbContext, AppUnitOfWork, payment/QR services, POS common/app/state/render/order/barcode/customer/payment modules and existing security/JavaScript fixtures.

## Validation

- Build to an isolated output directory without replacing the running application.
- JavaScript tests: local persistence/reload, network interruption, replay identities and ID translation, failures/quotas, context isolation, QR payload/confirmation, monetary calculations.
- SQL tests on disposable LocalDB: concurrent/repeated writes, rollback, tenant/terminal/user boundaries, original payment identity, monetary conflict checks and migration model consistency.
- Browser smoke when an isolated fixture/runtime is available. No live database migration, no real bank request, no service restart.

## Rollout and rollback

Deploy matching server and client assets only after applying the additive migration using the existing Migrator procedure. HTTPS trusted by clients is required for the service worker and offline restart. Local test results do not certify actual printer/browser/HTTPS installation.
Never clear local pending sales or remove the server retry ledger during rollback. Drain/reconcile pending work before disabling the feature. No automatic database downgrade.

## Handoff

Record actual modified files, commands/results, unresolved limits and deployment steps. Local verification does not constitute INDEPENDENT REVIEW PASS, CI VERIFIED or TASK PASS.

## Phạm vi sử dụng

- Tiếp tục giỏ đang bán; tạo giỏ mới; quét/tìm sản phẩm trong danh mục đã tải; đổi số lượng, đơn vị và giảm giá theo quyền; ghi chú; giữ/mở lại đơn của chính quầy.
- Chọn khách đã tải hoặc tạo khách tạm; dữ liệu khách mới có mã riêng và được đổi sang mã server khi đồng bộ. Số điện thoại trùng dữ liệu mới ở server sẽ cần đối soát.
- Tính giá lẻ/sỉ, giá theo lốc/thùng, giảm giá sản phẩm, combo và hàng tặng theo danh mục/khuyến mãi đã tải ở lần chuẩn bị. Server vẫn kiểm tra lại tổng tiền và danh sách hàng khi nhận khoản thu/chốt đơn.
- Tiền mặt và chuyển khoản xác nhận thủ công; tạo VietQR từ BIN/tài khoản đã cấu hình. QR tạo tại quầy không cần gọi dịch vụ tạo ảnh bên ngoài. Máy khách/ngân hàng của người trả vẫn phải thực hiện được việc chuyển tiền.
- QR tự động đã lưu có thể chuyển sang nút xác nhận thủ công khi mất kết nối. Server dùng khóa chung với xử lý ACB để gắn vào cùng một khoản thu. Nguồn `OfflineManual = 8` ghi rõ người xác nhận, không coi đó là bằng chứng ngân hàng. Không ghi thêm tiền nếu ACB đã ghi khoản thu trước đó.
- In phiếu tạm bằng cửa sổ in của trình duyệt. Sau khi đồng bộ, phiếu chính thức dùng luồng in server hiện có. Chưa kiểm chứng máy in vật lý tại cửa hàng.
- Báo mất kết nối, số thao tác đang chờ, thử đồng bộ và xuất JSON đối soát. Dữ liệu được ghi IndexedDB trước khi UI báo thành công. Một quầy chỉ có một tab được ghi.

Các thao tác cần dữ liệu chung như dùng voucher/điểm thưởng, chuyển đơn giữa quầy, mở/đóng ca mới, sửa cấu hình, trả hàng/hoàn tiền và phát hành hóa đơn điện tử vẫn cần server. Không tự xác nhận thanh toán thẻ với ngân hàng khi offline. Tồn kho toàn cửa hàng không thể cập nhật tức thời giữa các quầy khi mất kết nối.

## Chuẩn bị quầy và vận hành

1. Triển khai cùng phiên bản Web, các JS mới và `pos-offline-worker.js`; áp dụng migration `20260909210000_AddPosOfflineJournal` theo quy trình Migrator hiện có. Không áp riêng JS trước schema/server.
2. Dùng Chrome/Edge với địa chỉ HTTPS được máy khách tin cậy. `http://localhost` chỉ phục vụ kiểm thử trên cùng máy; IP LAN qua HTTP không đủ cho Service Worker/Web Locks.
3. Đăng nhập đúng cửa hàng/quầy/nhân viên, mở ca khi server đang hoạt động rồi mở POS. Chờ tải đủ danh mục và ứng dụng. Việc chuẩn bị không tải bí mật ACB; chỉ tải thông tin tài khoản công khai để nhận tiền.
4. Phiên offline dùng ca đã mở, có hiệu lực 24 giờ từ lần chuẩn bị. Mở lại POS khi đang có kết nối để chuẩn bị lại dữ liệu. Chưa từng chuẩn bị máy hoặc cần đăng nhập/mở ca mới thì phải có server.
5. Khi mất kết nối, tiếp tục bán trên quầy đã chuẩn bị. Với QR, kiểm tra tiền thực nhận rồi bấm **Đã nhận thủ công**. Đóng/mở lại trang vẫn giữ giỏ và các lần thu đang chờ.
6. Khi server trở lại, hệ thống gửi tuần tự bằng mã thao tác cũ. Nếu ca, quyền, giá, hàng tặng hoặc dữ liệu gốc đổi, hệ thống giữ đơn và hiện lý do cần đối soát. Không tự sửa số tiền đã thu và không xóa đơn lỗi. Dùng **Lưu bản đối soát** để giữ bản kiểm tra; không tự nhập lại khoản đã được server ghi nhận.

Không xóa dữ liệu trình duyệt hoặc dùng chế độ duyệt riêng trong lúc còn đơn chưa đồng bộ. Phiên khác tại cùng quầy bị chặn nếu ca/nhân viên trước còn giao dịch chờ; khôi phục đúng phiên gốc trước. Mất/hỏng ổ đĩa máy quầy trước khi đồng bộ nằm ngoài khả năng phục hồi của lưu trữ trên chính máy đó.

## Kiến trúc và bảo toàn dữ liệu

Client lưu ý định trước khi gửi, giữ nguyên UUID và request đã ánh xạ cho mỗi lần thử lại. Các mã tạm của đơn/dòng/khách được ánh xạ tuần tự khi server cấp mã thật. Trạng thái UI và hàng đợi được ghi trong cùng giao dịch IndexedDB.

`PosOperationFilter` chạy sau xác thực/ủy quyền MVC, lấy khóa theo cửa hàng/thao tác/quầy, kiểm tra ca và giỏ gốc, ghi nghiệp vụ cùng `PosOperationReceipts` trong một giao dịch SQL. Gửi lại cùng mã/nội dung trả kết quả đã lưu; đổi nội dung hoặc người/quầy dùng lại mã bị từ chối. Lệch tổng tiền hoặc danh sách hàng làm rollback khoản thu/chốt đơn. Yêu cầu rollback trong service con ngăn transaction ngoài commit. Sinh hóa đơn được chuyển sang sau commit thật, giữ hành vi best effort hiện có.

Thời điểm thu/chốt offline được lưu riêng với thời điểm server nhận yêu cầu. Nhật ký lưu nguồn xác nhận thủ công. Chỉ xóa hàng đợi sau khi server xác nhận và kết quả đã được ghi lại trên máy; lỗi bộ nhớ ngăn nhận thao tác mới. Service Worker chỉ cache màn hình POS đã chuẩn bị và tài nguyên công khai; không cache phản hồi API bằng HTTP cache.

## Chạy lại kiểm thử

```powershell
dotnet build GaoApp.sln -c Release --artifacts-path Logs/pos-offline-artifacts
dotnet vstest Logs/pos-offline-artifacts/bin/GaoApp.Tests/release/GaoApp.Tests.dll '/TestCaseFilter:FullyQualifiedName~PosOfflineSqlServerTests|FullyQualifiedName~Current_model_and_migration_snapshot_have_no_differences|FullyQualifiedName~PosPrimeResponsiveUiContractTests|FullyQualifiedName~GaoApp.Tests.Payments' '/logger:trx;LogFileName=pos-offline-final.trx' /ResultsDirectory:TestResults/pos-offline
node --test GaoApp.Tests/Ui/pos-collection-idempotency.test.cjs GaoApp.Tests/Ui/pos-qr-history.test.cjs GaoApp.Tests/Ui/pos-qr-installments.test.cjs GaoApp.Tests/Ui/pos-acb-realtime.test.cjs GaoApp.Tests/Ui/acb-confirmation-lookup.test.cjs GaoApp.Tests/Ui/pos-offline.test.cjs
npm ci --prefix GaoApp.Tests.Browser
dotnet build GaoApp.Tests.Browser/PosOffline.Browser.csproj -c Release --artifacts-path Logs/pos-offline-artifacts
dotnet Logs/pos-offline-artifacts/bin/PosOffline.Browser/release/PosOffline.Browser.dll
```

Browser probe yêu cầu Google Chrome và SQL LocalDB. Nó tự tạo hai cửa hàng kiểm thử, tài khoản, Web ở cổng ngẫu nhiên và DB dùng một lần bằng `FullApplicationFixture`; không sử dụng chuỗi kết nối của ứng dụng đang chạy, không gọi ngân hàng thật. Kiểm tra mất phản hồi sau commit, bán tiếp offline, mở lại trang, QR đã lưu, chống hai tab cùng ghi, đồng bộ và lỗi dung lượng; cuối cùng đối chiếu SQL đúng hai đơn hoàn tất, hai khoản thu tổng 100 và tồn kho 95 từ 100.

Evidence: `TestResults/pos-offline/*.trx`, `TestResults/pos-offline/browser/*.png`; kết quả build/tests được ghi ở handoff cuối. Source QR encoder: Nayuki QR Code generator, MIT license nằm trong `qrcodegen.js`, SHA-256 `2511BC17F40A3C41D4A0578995DB956B38997334D3D20113A5D4DC5C49C69480`.

## Kết quả xác minh tại workspace

- Build .NET Release và browser probe: thành công. Build đầy đủ có 10 warning đã có ở tests; không có lỗi build.
- JavaScript: 51/51 đạt, gồm 12 test mới cho nghiệp vụ offline, khuyến mãi, khách hàng, mã thao tác, QR và các test thanh toán/QR hiện có.
- Web/SQL/schema/UI/payment regression: 223/223 đạt, không skip; `TestResults/pos-offline/pos-offline-final.trx`.
- Edge thật, Web thật và SQL LocalDB dùng một lần: đạt. Đã kiểm tra service worker/IndexedDB, chặn tab thứ hai, mất phản hồi sau commit, bán offline và mở lại trang, mở QR/nhấn xác nhận thủ công qua UI, đồng bộ tuần tự và từ chối thao tác khi ghi bộ nhớ thất bại. Kết quả SQL: 2 đơn, 2 khoản thu, tổng 100, tồn từ 100 xuống 95. Không có lỗi JavaScript được ghi nhận trong probe.
- Chưa xác minh máy in vật lý, chứng chỉ HTTPS/LAN hoặc cấu hình trình duyệt của các máy quầy thật. Không chạy migration lên DB đang sử dụng, không deploy/restart server và không tạo commit/PR.

Các nhóm file bàn giao: `PosOperationReceipt` + configuration + migration/designer/model snapshot; `POSOfflineController`/`PosOperationFilter`; `IAppTransaction`/`IAppUnitOfWork`/`AppUnitOfWork` và một call site post-commit ở `POSService`; nguồn xác nhận ACB và policy/audit; `Program`, `POSController`, POS Index/app/state/render/payment; `pos.offline.core.js`, `pos.offline.js`, `pos-offline-worker.js`, `qrcodegen.js`; test SQL/JS/UI và project `GaoApp.Tests.Browser`; contract và decision log. Các thay đổi đã có trước trong working tree được giữ nguyên.

## Sửa lỗi tải tài nguyên sau phản hồi người dùng — 2026-09-09

Ảnh Console báo `Failed to convert value to 'Response'`, bốn script POS không tải được và `PosRender` undefined. Nguyên nhân: nhánh tài nguyên Service Worker dùng `cache.match(request) || fetch(request)` mà chưa await kết quả match. Promise luôn truthy nên khi cache miss, network fallback không chạy và respondWith nhận undefined. Kiểm thử ban đầu chỉ chạy với script đã cache đầy đủ nên bỏ sót đường này.

Đã sửa để await Cache.match trước khi chọn response cache hoặc fetch. Giữ nguyên tên cache, IndexedDB và dữ liệu hàng đợi. Thay đổi runtime chỉ ở `GaoApp.Web/wwwroot/pos-offline-worker.js`.

Xác minh: bộ regression mới `GaoApp.Tests/Ui/pos-offline-worker.test.cjs` tái hiện 4 lỗi trước sửa; sau sửa 6/6 đạt. Chạy cùng core offline: 18/18 đạt. Browser probe thêm trường hợp URL script phiên bản mới và xóa đúng bốn script khỏi cache tài nguyên của quầy kiểm thử rồi reload. Edge tải lại POS thành công, không có lỗi JavaScript; luồng offline, QR thủ công, khôi phục trang, đồng bộ và kiểm tra SQL vẫn đạt (hai đơn/hai khoản thu/tổng 100/tồn 95).

```powershell
node --test GaoApp.Tests/Ui/pos-offline-worker.test.cjs GaoApp.Tests/Ui/pos-offline.test.cjs
dotnet Logs/pos-offline-artifacts/bin/PosOffline.Browser/release/PosOffline.Browser.dll
```

Với trình duyệt đang giữ worker cũ: khi server đã có file sửa, mở DevTools → Application → Service Workers → Update, sau đó tải lại POS. Không dùng Clear site data để xử lý lỗi này vì có thể xóa các đơn offline chưa gửi.

Theo thông tin người dùng sử dụng Google Chrome, browser probe đã chuyển mặc định sang channel `chrome` và chạy lại đạt trên Google Chrome cài trên máy. Các bước tải script chưa có trong cache, khôi phục POS offline, QR xác nhận thủ công, đồng bộ sau mất phản hồi và lỗi ghi bộ nhớ đều đạt; SQL đối chiếu đúng hai đơn, hai khoản thu tổng 100, tồn kho 95. Đây là kiểm thử bằng profile và DB dùng một lần; chưa cập nhật trình duyệt hay server bán hàng đang sử dụng.

## Sửa cảnh báo QR còn hiện sau khi kết nối lại — 2026-09-09

Người dùng báo đã bán một đơn offline rồi kết nối lại nhưng còn thông báo “Chưa tải được các QR đang chờ”. Source evidence: `pos.acb.js` vẫn gọi `terminal-pending` và status/complete khi offline hoặc hàng đợi chưa đồng bộ; `pos.offline.js` chủ động từ chối các endpoint ACB này trong localMode. Nhánh discovery thất bại tạo banner nhưng nhánh thành công không gỡ banner, kể cả khi server trả danh sách rỗng. Ảnh này riêng lẻ không chứng minh đơn của người dùng đồng bộ thất bại hay thành công.

Thay đổi giới hạn ở `pos.acb.js`: tạm ngừng kiểm tra QR khi trình duyệt/POS mất kết nối hoặc còn thao tác chờ gửi; tiếp tục discovery khi POS phục hồi và hàng đợi hết; kiểm tra lại trạng thái kết nối sau response để không bắt đầu complete khi vừa chuyển offline. Cảnh báo discovery được gỡ khi tải danh sách thành công hoặc POS chuyển offline và dùng banner kết nối hiện có. Cảnh báo thanh toán/đối soát vẫn được giữ; lỗi server mới vẫn hiển thị khi đang online. Không thay đổi journal, IndexedDB, migration hoặc logic ghi nhận khoản thu phía server.

Regression trong `pos-acb-realtime.test.cjs`: thêm 7 trường hợp, 6 trường hợp thất bại trước sửa; sau sửa 22/22 đạt. Chạy cùng core/worker offline, lịch sử QR, trả góp QR, idempotency khoản thu và hiển thị nguồn xác nhận: 64/64 đạt.

Chrome browser probe bổ sung lỗi HTTP 503 cho riêng danh sách QR rồi phục hồi về danh sách rỗng; kiểm tra banner ẩn. Sau mở lại POS offline, kiểm tra không gọi discovery/status/complete; giữ request replay đầu tiên để xác minh server đã kết nối nhưng hàng đợi chưa hết thì vẫn chưa kiểm tra QR. Sau đồng bộ, discovery chạy lại và không còn cảnh báo. Toàn bộ probe đạt, SQL đúng 2 đơn hoàn tất, 2 khoản thu tổng 100, tồn kho 95 từ 100. Đã xem ảnh `TestResults/pos-offline/browser/after-sync.png`: banner kết nối ổn định, không có cảnh báo QR cũ.

Các lệnh xác minh:

```powershell
node --check GaoApp.Web/wwwroot/Admin/js/pos/pos.acb.js
node --test GaoApp.Tests/Ui/pos-acb-realtime.test.cjs GaoApp.Tests/Ui/pos-offline-worker.test.cjs GaoApp.Tests/Ui/pos-offline.test.cjs GaoApp.Tests/Ui/pos-qr-history.test.cjs GaoApp.Tests/Ui/pos-qr-installments.test.cjs GaoApp.Tests/Ui/pos-collection-idempotency.test.cjs GaoApp.Tests/Ui/acb-confirmation-lookup.test.cjs
dotnet Logs/pos-offline-artifacts/bin/PosOffline.Browser/release/PosOffline.Browser.dll
```

Sau khi server phục vụ bản sửa, tải lại POS trên Chrome để nhận URL `pos.acb.js` có hash mới qua `asp-append-version`; giữ nguyên dữ liệu site. Chưa đọc hay sửa đơn bán thật của người dùng, chưa deploy/restart server. Trạng thái bàn giao vẫn READY FOR COORDINATOR REVIEW.

## Sửa thanh kết nối ổn định hiện/tắt liên tục — 2026-09-09

`renderNetworkBanner` trước đây luôn dựng lại banner, hiện nó và khởi động lại timer 3 giây mỗi lần gọi. Heartbeat offline chạy mỗi 6 giây và các lần cập nhật giỏ đều gọi render, nên thanh xanh tái xuất hiện dù trạng thái kết nối không đổi.

Sửa giới hạn ở `pos.render.js`: lưu variant trên container; khi vẫn online thì giữ nguyên trạng thái hiển thị và timer. Lần mở trang đang online không hiện thông báo xanh; sau offline/reconnecting/đồng bộ thì hiện một lần trong 3 giây rồi ẩn. Cảnh báo mất kết nối, dữ liệu chờ gửi và công cụ đối soát vẫn hiện bình thường.

Chrome probe đã tái hiện lỗi trước sửa: banner đã ẩn nhưng lại hiện sau heartbeat thành công dù connected=true, pending=0 và không có lỗi. Sau sửa, ba lần heartbeat liên tiếp không hiện lại banner (kiểm tra cả DOM MutationObserver); ngắt mạng tiếp vẫn hiện cảnh báo mất kết nối. Toàn bộ luồng bán offline, phục hồi QR, đồng bộ và lỗi bộ nhớ đạt; SQL đúng 2 đơn, 2 khoản thu tổng 100, tồn 95. `node --check` cho render và browser probe đạt. Không thay đổi backend, dữ liệu thực, cấu hình chứng chỉ hay triển khai server.
