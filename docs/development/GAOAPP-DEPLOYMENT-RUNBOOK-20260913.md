# Runbook triển khai GaoApp — 2026-09-13

**Chỉ có hai quy trình triển khai bình thường: TH1 chỉ Code/Web và TH2 Code + schema. Seed/bootstrap/repair là thao tác dữ liệu riêng do human phê duyệt.** Không dùng “chạy Migrator” như một lệnh mặc định.

Scope: GaoApp / GaoAppPool, một instance IIS, external storage `C:\GaoAppData`. GaoMart không thuộc phạm vi. Script chuyển IIS physicalPath sang thư mục release immutable đã verify; không chép đè release đang chạy, không thay C:\GaoAppData, không xóa release/backup cũ. Script không commit/push/deploy tự động từ development. File này là hướng dẫn cho operator sau human review; task viết tài liệu chưa triển khai server.

## Chuẩn bị chung

- Development: mở PowerShell tại source đã human-approved. Release Build và tests phải gắn đúng bytes; sửa tiếp thì validation cũ trở thành lịch sử. CLI Release tương đương chọn Release trong Visual Studio và build solution. Không lấy DLL từ bin của ứng dụng đang chạy.
- Server: mở **Windows PowerShell 5.1 64-bit** với quyền quản trị IIS; cài .NET 8 Hosting Bundle và WebAdministration phù hợp. Chỉ dùng script đã review. Nếu máy cấm script, dùng chính sách ký script của tổ chức hoặc cho phép riêng process sau review; không đổi execution policy toàn máy để chạy nhanh.
- Package bất biến: ví dụ `D:\GaoAppDeploy\releases\<release-id>\web` và khi TH2 thêm `migrator`. Đây là path phục vụ lâu dài, không phải Temp bị tự xóa. Tài khoản GaoAppPool chỉ cần read/execute package; khóa write cho identity ứng dụng. Thư mục backup riêng, ví dụ `D:\GaoAppDeploy\WebBackups`, ACL hạn chế operator/backup. Tạo các thư mục này trước khi gọi script. Các path old/new Web, backup, GaoMart, C:\GaoAppData phải tách rời, không symlink/junction.
- GaoApp phải dùng riêng pool `GaoAppPool`; không pool dùng chung với GaoMart/site/app khác. Script từ chối child application chưa được review. GaoMart phải tồn tại để so sánh. GaoApp phải đang Started khi bắt đầu; outage có sẵn cần chẩn đoán trước, không dùng deploy để che lỗi.
- Cấu hình host/secrets nằm trong **environmentVariables của riêng GaoAppPool**, không trong package hoặc environment dùng chung toàn máy: `DOTNET_ENVIRONMENT=Production`, `ASPNETCORE_ENVIRONMENT=Production`, `SeedData__EnableDemoSeed=false`, `SeedData__EnableDefaultAdminSeed=false`, `ProductionBootstrap__Enabled=false`; SQL connection đúng DB, tenant/AppUrl/AllowedHosts/proxy theo host đã review. Storage__UploadRoot, DataProtection__KeysPath và CertificatePath nằm dưới C:\GaoAppData, khóa ACL phù hợp; CertificatePassword được cấp qua kênh secret của host. Giữ nguyên key ring/certificate để cookie còn dùng được sau restart.
- Xem mẫu **tên khóa**, không đưa secret thật vào source: `eng/deployment/host-environment.example.json`. Nếu hệ thống hiện đặt config trong web.config cũ hoặc global environment, phải chuyển sang pool-specific config trong maintenance/change được duyệt trước; deployment script không tự ghi cấu hình đó hay thay GaoMart.
- HTTPS smoke URL phải khớp host binding cụ thể của GaoApp, ví dụ `https://gaoapp.example.vn/health/ready`, chứng thư hợp lệ, không query/userinfo. Script không bypass TLS và không đi theo redirect. Health checks đọc DB và probe storage; không gọi login hay business endpoint để làm PASS.
- Source anchor là source authority; `release-manifest.json` là package manifest. Lấy hash manifest từ máy development qua kênh độc lập đã tin cậy. Không lấy hash mới từ một gói nghi bị thay đổi rồi coi đó là expected.

## Chọn TH1 hay TH2

| Thay đổi | Chọn | Điều kiện |
|---|---|---|
| JS/CSS/Razor/controller/service, không đổi schema/model yêu cầu DB | TH1 | Review migration inventory/diff không thay đổi; server schema đã tương thích |
| Có migration pending đã review | TH2 | Review Up/Down/DML/rủi ro, backup/recovery point, maintenance và writer quiescence |
| Model đòi schema nhưng chưa có migration, history/schema không khớp | Dừng | Không tự chạy EnsureCreated, seed, SQL repair hoặc sửa history để ép deploy |
| Cần menu/quyền/role/admin/master data mới | Data operation riêng | Không tự ghép seed vào TH1 hay TH2; human duyệt delta cụ thể |

Remediation này giữ 29 migration, latest `20260912150000_AddReceivingPackagingPhoto`; không tạo migration thứ 30. Trong lần phát triển schema tương lai, chỉ tạo migration sau authority tương ứng.

## TH1 — CHỈ SỬA CODE / UI / SERVICE — KHÔNG ĐỔI DB

**NO MIGRATOR — NO SEED — NO DB CHANGE bởi công cụ triển khai.** Quy trình: development → Release Build → tests → publish Web → verify → server staging → backup old Web → stop GaoApp → switch Web → verify → start → smoke → human practical.

| Bước | WHERE TO RUN | WHAT TO DO | WHY | EXPECTED RESULT / PASS CONDITION | FAIL ACTION | ROLLBACK |
|---|---|---|---|---|---|---|
| 1. Khóa source và chọn TH1 | Development / source root | Review exact git diff/status, migration inventory, EF model compatibility; xác nhận không schema change; ghi source anchor và hashes | Tránh phát hành code đang sửa hoặc cần schema mới | Đúng branch/HEAD/source authority; migration delta NONE; một writer | Dừng, review schema/source; không force reset | Chưa tác động server |
| 2. Release Build | Development | `dotnet build GaoApp.sln -c Release --no-restore -t:Rebuild --nologo -p:UseSharedCompilation=false` (restore trước nếu dependencies chưa sẵn) | Build từ bytes cuối | Exit 0, không compiler error; cảnh báo ghi evidence | Sửa trong phạm vi đã duyệt, build lại; chưa publish | Chưa tác động server |
| 3. Tests | Development, DB tạm do test tạo | `dotnet test GaoApp.Tests/GaoApp.Tests.csproj -c Release --no-build --no-restore --logger trx`; targeted thay đổi; `powershell.exe -NoProfile -File scripts/test-deployment-safety.ps1`; browser nếu bị ảnh hưởng | Không mang lỗi startup/data isolation vào server | Fresh tests PASS, zero skipped ngoài quyết định rõ ràng; evidence gắn final bytes | Dừng nếu fail/unavailable; không dùng PASS anchor cũ | Fixture chỉ xử lý DB test theo prefix guard |
| 4. Publish Web | Development / source root | `./scripts/publish-staging-release.ps1 -WebOnly`; ghi đường dẫn release được trả ra | Chỉ publish GaoApp.Web và dependency của nó | Package chỉ có web + release-manifest; **không có migrator** | Dừng nếu publish/verify lỗi, không copy bin cũ | Giữ release lỗi ngoài server, không deploy |
| 5. Verify và truyền hash | Development | `./scripts/test-release-package.ps1 -ReleasePath '<release>' -WebOnly`; `(Get-FileHash '<release>/release-manifest.json' -Algorithm SHA256).Hash` | Bind file list/length/hash, safe defaults và normal IIS DLL arguments | PASS; lưu trusted manifest hash cùng source/build/test evidence | Không “sửa” manifest để khớp file lỗi; tạo lại gói từ source đúng | Chưa server change |
| 6. Transfer / staging | Server staging | Copy nguyên release vào thư mục release-id mới, không tồn tại/không đang phục vụ; chuyển deploy scripts đã review; verify lại với `-WebOnly` và trusted hash | Phát hiện thiếu/sai bytes khi transfer | Full manifest match, secret-free, đúng Web.dll/web.config, không reparse | Dừng trước IIS; không overlay thư mục current | Current Web tiếp tục chạy |
| 7. Host preflight | Server, admin PowerShell | Kiểm prerequisites chung: pool-specific config, external data/keys ACL, host/cert, disk backup, GaoMart state; gọi command TH1 bên dưới | Fail trước khi stop nếu package/host topology không đúng | Script nhận đúng path/hash/URL; GaoApp Started, pool riêng, các root disjoint | Dừng, sửa host theo change được duyệt; không đổi GaoMart | Không server mutation trước bước backup/switch |
| 8. Backup old Web | Script TH1 trên server | Script tạo `GaoApp-Web-<UTC>-<GUID>`, copy toàn old Web, so toàn file/length/SHA và recheck nguồn | Có bản phục hồi byte chính xác, phát hiện old Web đang bị đổi | Backup count/bytes khớp, đủ chỗ, không nằm trong web/data/GaoMart | Dừng trước stop nếu backup fail hoặc bytes đang đổi | Current Web còn nguyên; giữ evidence backup chưa đủ |
| 9. Stop đúng phạm vi | Script TH1 | Stop-Website GaoApp; Stop-WebAppPool GaoAppPool; chờ Stopped ≤30 giây | Không thay DLL khi worker cũ còn chạy | Chỉ GaoApp/pool dừng; GaoMart không bị stop/recycle | Dừng và chẩn đoán; không iisreset/stop W3SVC | Script cố phục hồi old Web theo nhánh failure; operator verify state |
| 10. Deploy và byte verify | Script TH1 | Đổi `IIS:\Sites\GaoApp.physicalPath` sang `<release>/web`; kiểm files/bytes mới | Không overlay, không replace persistent data | New path đúng, bytes không đổi trong thao tác; C:\GaoAppData giữ nguyên | Quay về old physicalPath; giữ package/evidence lỗi để điều tra | Không SQL rollback vì không đụng DB |
| 11. Start | Script TH1 | Start GaoAppPool rồi GaoApp | Chạy Web mới với pool configuration giữ nguyên | Site/pool chạy; Web startup không migrate/seed ở mọi environment | Script rollback Web nếu activate/smoke fail; kiểm log đã redacted | Old physicalPath + restart chỉ GaoApp |
| 12. Runtime smoke và GaoMart verify | Script TH1 / trình duyệt operator | HTTPS `/health/ready` phải 200 JSON status Healthy; script retry bounded cho warmup; so GaoMart IIS config/state và tree bytes | Xác nhận host/runtime còn hoạt động, isolation không bị phá | Ready healthy, GaoMart unchanged; không login giả để làm health pass | Fail thì rollback Web; nếu GaoMart khác, dừng và điều tra writer khác/runtime file, không tự sửa GaoMart | Old Web; C:\GaoAppData/DB không được restore bởi TH1 |
| 13. Human practical | Trình duyệt đúng tenant sau script PASS | Human kiểm login, menu tùy chỉnh/order/active, quyền tài khoản thật, màn hình thay đổi; thao tác nghiệp vụ chỉ khi được phép | Health không thay thế nghiệm thu Product | Human xác nhận practical; ghi release ID, hash, thời gian, quyết định giữ/rollback | Dừng nghiệm thu, thu log/screenshot an toàn; không chạy seed để thử sửa | Làm TH1 rollback bên dưới |

Command trên server (thay path/hash/URL bằng giá trị đã review, không chạy nguyên placeholder):

```powershell
./eng/deployment/Deploy-WebOnly.ps1 `
  -ReleasePath 'D:\GaoAppDeploy\releases\<release-id>' `
  -ExpectedManifestSha256 '<trusted-64-hex-sha256>' `
  -BackupRoot 'D:\GaoAppDeploy\WebBackups' `
  -SmokeUrl 'https://gaoapp.example.vn/health/ready'
```

TH1 **không chạy GaoApp.Migrator**, không migration, không seed/bootstrap/repair, không DB backup/restore trong script. Khi app phục vụ trở lại, request và worker nghiệp vụ vẫn có thể ghi dữ liệu bình thường; “NO DB CHANGE” ở đây là deployment tooling/startup initializer, không phải khóa read-only toàn ứng dụng.

## TH2 — CÓ THAY ĐỔI SCHEMA

**SCHEMA-ONLY — NO AUTOMATIC SEED.** Review migration → Release build/tests → publish Web+Migrator → verify/staging → backup/recovery point → maintenance/quiesce → schema-only đúng một lần → verify schema → backup old Web → deploy Web → start/smoke → human practical.

Bổ sung an toàn: script dừng GaoApp/GaoAppPool trước khi gọi schema-only để HTTP/worker không ghi trong migration. Human quiesce các writer khác (LabelPrintServer, external jobs, integrations), traffic và background operations trước backup/recovery point; `-WritersQuiesced` là xác nhận thao tác đó, không tự stop dịch vụ khác. Không đụng GaoMart để tạo maintenance.

| Bước | WHERE TO RUN | WHAT TO DO | WHY | EXPECTED RESULT / PASS CONDITION | FAIL ACTION | ROLLBACK |
|---|---|---|---|---|---|---|
| 1. Tạo migration theo authority | Development | Chỉ khi change schema đã được duyệt: tạo migration có tên rõ; **task hiện tại không tạo**; review model diff | Schema change phải là artifact được kiểm soát | Migration đúng scope, tên/ordering hợp lệ | Dừng nếu thiếu approval migration; không tạo tự phát | Chưa server change |
| 2. Review Up/Down | Development, human reviewer | Đọc toàn Up/Down và generated SQL; liệt kê INSERT/UPDATE/DELETE/MERGE, SQL raw, default/computed, FK/index/constraint, suppressTransaction | “Schema-only” vẫn có DML migration-owned | Reviewer chấp thuận exact migration IDs, data effects, blocking time và rollback; record JSON bind manifest sau publish | Dừng nếu transformation không giải thích được, không auto sửa data | Chưa server change |
| 3. Chứng minh trên DB tạm | Development | Test upgrade từ prefix đại diện và dataset tương ứng, uniqueness/backfill guards, dữ liệu custom; chỉ DB giả/tạm | Đo blocking/destructive effects trước server | Fresh SQL integration PASS; rejection giữ dữ liệu khi unsafe | Sửa source/test/migration chỉ trong authority; không chạy server để thử | Test harness cleanup theo guard |
| 4. Release Build/tests | Development | Release build, targeted migration/preflight/seed isolation, full regression mới, PS validation, browser nếu thay đổi ảnh hưởng | Final source phải được chứng minh | Actual fresh PASS, build exit0; giữ log failures lịch sử | Dừng nếu fail/unavailable; không dùng baseline PASS | Chưa server change |
| 5. Publish paired | Development | `./scripts/publish-staging-release.ps1` (không -WebOnly) | Web+Migrator cùng bytes Domain/Application/Infrastructure | Verify paired assembly SHA, safe defaults, exact file manifest | Dừng nếu mismatch hoặc release có secret | Không deploy gói không đồng bộ |
| 6. Verify / transfer | Development → server staging | `./scripts/test-release-package.ps1 -ReleasePath '<release>'`; chuyển nguyên gói vào path mới; kiểm trusted manifest SHA server | Chống transfer thiếu/byte drift | Cả web/migrator nguyên vẹn, source binding đúng | Không regenerate manifest trên server để “sửa” mismatch | Old Web nguyên vẹn |
| 7. Chốt SQL target và review record | Server / human change record | Ghi exact server/database/environment, manifest hash, reviewedMigrationIds, approvedBy, dataTransformationsReviewed=true, rollbackReviewed=true | Không migrate nhầm DB hoặc dùng review của release khác | Record khớp GaoAppPool SQL identity và process SQL identity; Encrypt=true, TrustServerCertificate=false | Dừng trước stop/migration; không in connection string | Không SQL mutation |
| 8. Quiesce và DB backup | Server / DBA approved tools | Dừng traffic/writers ngoài GaoApp theo kế hoạch; lấy full DB backup phù hợp và log backup/recovery point nếu cần; verify restore vào DB khác; đảm bảo backup sau dữ liệu cần bảo toàn | Có đường phục hồi thật, không chỉ file .bak tồn tại | Backup evidence đúng DB/release, approvedBy, backupReference, completedUtc ≤24h, restoreVerified=true; không còn write sau recovery point ngoài kế hoạch | **Production dừng nếu thiếu backup/restore evidence**; không tự waive | Chưa migration; phục hồi traffic nếu hủy maintenance |
| 9. Test-only waiver | Chỉ môi trường Test đã human chỉ định | Nếu cố ý không cần phục hồi dữ liệu giả: `-TargetEnvironment Test -HumanTestBackupWaiver`; review record vẫn phải environment Test và đúng DB/hash | Tránh gọi test waiver ngầm cho production | Explicit human decision và target binding, WritersQuiesced vẫn bắt buộc | Sai target/production waiver bị reject | Test DB có thể phải tạo lại; không áp dụng production |
| 10. Secure SQL và host checks | Server / admin PowerShell | Cấp connection qua process `GAOAPP_DEPLOY_CONNECTION`, không argument/manifest/file; giữ pool host config sẵn; gọi command TH2 | Child process riêng chỉ nhận safe allowlist + schema flags false | Target match pool/review, package PASS, topology/path/backup roots valid | Dừng nếu thiếu config; không log secret để debug | Chưa schema change |
| 11. Stop và migrate một lần | Script TH2 | Stop chỉ GaoApp/GaoAppPool, chờ stopped; `dotnet GaoApp.Migrator.dll --schema-only` ở migrator directory | Không lẫn seed, không retry tự động, không chạy song song | Config/preflight allowed; EF migration succeeds; postflight CurrentBaseline/counts đúng | Nonzero/missing marker: **không deploy Web**, giữ GaoApp stopped, human xem trạng thái; không chạy lại tự động | DBA quyết định restore backup; không auto Down |
| 12. Verify schema | Migrator + script TH2; DBA read-only nếu cần | Postflight kiểm history prefix, full structural manifest, applied/source count; marker SCHEMA_ONLY_VERIFIED; verify DB history/schema human khi change yêu cầu | Exit 0 chỉ có khi verification thành công | Không pending, đúng schema; reviewed DML effects phù hợp | Dừng khi verify lỗi dù EF đã chạy xong; migration có thể đã commit | Restore DB + previous Web hoặc kế hoạch forward-fix được duyệt |
| 13. Backup Web và deploy | Script TH2 sau schema PASS | Recheck GaoMart/package; backup old Web và hash verify; switch physicalPath; byte verify | Không chạy code mới khi schema fail; giữ old package để rollback đồng bộ | Backup verified, new Web exact release | Fail sau schema: giữ GaoApp stopped, không auto start old Web trên schema mới | Human phối hợp DB restore và old Web |
| 14. Start/smoke | Script TH2 | Start GaoAppPool/GaoApp; bounded HTTPS ready smoke; compare GaoMart | Xác minh runtime với schema mới | Healthy, GaoMart unchanged, external keys/uploads nguyên vị trí | Fail: script stop GaoApp, không tự Down/reseed/old-Web restart | Quy trình TH2 rollback phía dưới |
| 15. Human practical | Trình duyệt + DBA read-only review | Kiểm customized menu/order/active, role/grants, store/master data và nghiệp vụ có schema change; so migration-owned data expectations | Seed isolation và practical đều cần nghiệm thu | Human chấp thuận release; ghi validation/evidence; chỉ resume writers sau quyết định | Nếu sai, giữ maintenance; không chạy security-seed để che nguyên nhân | Restore đúng recovery point + old Web; ghi dữ liệu có nguy cơ mất sau backup |

Ví dụ nội dung **migration-review.json** (không có secret; human ký/điền qua change-control):

```json
{
  "server": "sqlhost\\instance",
  "database": "GaoApp",
  "environment": "Production",
  "releaseManifestSha256": "<trusted-64-hex-sha256>",
  "approvedBy": "<human/change-ticket>",
  "reviewedMigrationIds": ["<actual-reviewed-pending-migration-id>"],
  "dataTransformationsReviewed": true,
  "rollbackReviewed": true
}
```

**database-backup.json** dùng cùng server/database/environment/hash/approvedBy và thêm `backupReference` (backup set ID hoặc kho backup được bảo vệ), `completedUtc` (ISO8601 UTC), `restoreVerified: true`. Đây là attestation của human/DBA; script kiểm binding/tuổi record nhưng không chứng minh chất lượng backup từ một boolean. DBA chịu trách nhiệm restore drill/recovery point. Không đặt connection/password trong các record.

Cấp SQL process secret bằng secret manager của host, hoặc nhập kín (không gõ literal password vào command history):

```powershell
$deploySecret = Read-Host 'SQL connection cho đúng GaoApp DB' -AsSecureString
$secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($deploySecret)
try {
  $env:GAOAPP_DEPLOY_CONNECTION = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)
  ./eng/deployment/Deploy-WithSchemaMigration.ps1 `
    -ReleasePath 'D:\GaoAppDeploy\releases\<release-id>' `
    -ExpectedManifestSha256 '<trusted-64-hex-sha256>' `
    -BackupRoot 'D:\GaoAppDeploy\WebBackups' `
    -SmokeUrl 'https://gaoapp.example.vn/health/ready' `
    -TargetEnvironment Production `
    -ReviewedMigrationEvidence 'D:\GaoAppDeploy\change-records\migration-review.json' `
    -DatabaseBackupEvidence 'D:\GaoAppDeploy\change-records\database-backup.json' `
    -WritersQuiesced
}
finally {
  Remove-Item Env:\GAOAPP_DEPLOY_CONNECTION -ErrorAction SilentlyContinue
  [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer)
  $deploySecret = $null
}
```

Không dùng transcript chứa secret và không echo biến. Environment là secret trong process memory, không phải mã hóa khỏi administrator; dùng quyền tối thiểu/secret manager của tổ chức. SQL identity cho migration có quyền schema cần thiết, chỉ đúng DB; Web dùng runtime identity riêng khi host hỗ trợ, cùng server/database nhưng không nhất thiết cùng user. Script so server/database, không yêu cầu cùng credentials.

## Hiểu đúng rủi ro migration và giới hạn giữ dữ liệu

- Thêm nullable column: thường giữ row cũ, cột mới NULL; vẫn cần xem default/computed/trigger/locking và code compatibility.
- Thêm bảng: thường không đụng dữ liệu bảng cũ; review FK/index/permission/resource và bất kỳ SQL kèm theo.
- Unique index/constraint: có thể fail vì duplicate/invalid data, khóa bảng lâu, ảnh hưởng ghi. Không tự delete/merge để “đủ điều kiện”. Human quyết định sửa dữ liệu riêng.
- Non-null column/backfill: xác định giá trị cho mọi row, kiểm fallback/default có đúng business không; cân nhắc rollout nullable → backfill reviewed → constraint. Không suy ra owner từ tên/supplier khi schema guard đòi bằng chứng chính xác.
- DROP/ALTER type/length: nguy cơ mất/truncate/chuyển đổi data; old Web có thể không còn chạy. Backup restore drill và downtime plan bắt buộc. Không coi Down là backup.
- Migration-owned transformation: DML nằm trong Up, gắn history/version và được reviewer duyệt; ví dụ normalized invoice identity, receipt policy, buyer owner, receiving state và ACB notification backfill trong audit. Được TH2 chạy theo review.
- Seed/bootstrap: dữ liệu canonical/default/demo/admin/master được helper tạo/sync; **không phải migration schema**. Hai workflow deployment không gọi chúng. Schema không pending không có nghĩa là được phép seed.
- EF có migration `EnableSnapshotProfitReads` chạy ALTER DATABASE ngoài transaction; failure không đảm bảo toàn bộ lần chạy tự rollback. Preflight không truy cập SQL production trong task phát triển này và cũng không chứng minh business content đúng ngoài schema invariants.
- Runtime request, ACB queue, media retention, print worker vẫn có thể ghi/xóa dữ liệu theo Product sau khi app chạy. Không hứa toàn DB/ổ data bất biến mãi sau deployment. Quiesce các writer để làm so sánh snapshot tĩnh.

## Khi nào chủ động seed / bootstrap

Chỉ thực hiện như **data change riêng**, sau human phê duyệt danh sách DB/tenant/entities/expected INSERT/UPDATE/soft-delete/re-grant và rollback. Không thêm các lệnh này vào script normal deploy, IIS startup, scheduled restart hoặc bước “smoke”.

| Operation | Command và config | Data thay đổi | Bảo toàn customization / approval |
|---|---|---|---|
| Security seed | `dotnet GaoApp.Migrator.dll --security-seed`; cả ba flags false; schema current | Permissions thiếu; default Roles/grants thiếu; canonical menu ở active stores; legal/purchase menus thiếu | Có thể đổi Title/parent/route/order/active, restore deleted system menu và soft-delete duplicates; re-grant default quyền đã gỡ. **Human review bắt buộc**, không dùng nếu muốn giữ mọi system-menu customization |
| Production bootstrap | `dotnet GaoApp.Migrator.dll --bootstrap`; ProductionBootstrap__Enabled=true, demo/default-admin=false, đủ named fields/password | Store/LegalEntity/Warehouse/terminal/admin/mapping/security cho trạng thái Empty | Matching no-op, partial/different reject; không reset user/password cũ. Bootstrap một tenant đầu tiên, không phải “thêm store tùy ý” |
| Demo | `dotnet GaoApp.Migrator.dll --demo-seed`; Development, EnableDemoSeed=true và DemoUserPassword đủ; bootstrap/default-admin=false | Bốn user demo khi Users rỗng, missing mappings dùng roles có sẵn | Chỉ DB giả; schema và security/roles chuẩn bị explicit riêng; không Production/Staging |
| Menu-only recovery tương thích | `dotnet GaoApp.Web.dll --recover-admin-menus-only` | Canonical AdminMenuItems | Đồng bộ/reset như trên, không migration/roles; process exit, không phục vụ Web. Không đặt vào web.config |

Mỗi data operation: **Where** maintenance shell đúng package/DB; **What** inventory/read-only preview và backup trước, human duyệt exact delta, cấp secret process, chạy đúng một command, kiểm delta sau và xóa secret process; **Why** tránh reset data custom; **Expected/Pass** đúng entity/tenant/semantics đã duyệt, counts và values khớp; **Fail** dừng, không chạy mode khác để thử sửa; **Rollback** DBA/human áp dụng data recovery đã duyệt hoặc restore backup cùng Web tương thích. Không có “dry-run seed” giả: pipeline hiện không cung cấp preview mutation nên preview phải là truy vấn/read-only review hoặc clone DB được cho phép.

Nếu cần permission/menu mới mà chỉ chấp nhận insert-missing, không chạy full `--security-seed` trước khi biết canonical reset sẽ tác động gì. Quyền sở hữu system-menu fields và default grants còn cần Product quyết định. Remediation này cô lập hazard, không tự chọn server data hay source definitions làm authority cho mọi giá trị.

## TH1 rollback

Ở server, giữ nguyên DB và C:\GaoAppData. Script tự rollback physicalPath khi activation/smoke lỗi; human verify old Web phục vụ và GaoMart unchanged. Nếu practical fail sau script: ghi old/new paths và hashes; stop chỉ GaoApp, stop GaoAppPool, chờ stopped; đổi physicalPath về release cũ đã verify (hoặc bản Web backup copy vào một path phục vụ mới, verify trước); start pool/site; HTTPS ready + practical lại. Không dùng iisreset, không restore DB, không seed, không thay key ring/uploads. Nếu old Web không lên, dừng và điều tra host/code; không dùng migration như biện pháp sửa TH1.

## TH2 rollback

Giữ maintenance và tất cả GaoApp writers dừng. DBA xác nhận migration nào đã commit và recovery point; không chạy tự động Down. Với production rollback đã duyệt: restore DB backup/log chain đúng recovery point vào đúng DB qua quy trình DBA; kiểm integrity/history/schema, các setting ngoài transaction và compatibility của old Web; restore previous Web/release path có hashes đúng; giữ keys/uploads, đánh giá đồng bộ external media nếu business writes đã xảy ra sau backup; start chỉ GaoApp/pool, ready + human practical, kiểm GaoMart; resume writers sau human quyết định. Ghi rõ giao dịch sau backup có thể mất khi restore, không hứa khôi phục chúng nếu chưa có log/reconciliation plan. Nếu chọn forward-fix thay restore, đó là change mới cần review, không automatic retry của deployment script.

## Checklist nghiệm thu và giới hạn automation

Ghi source anchor + release ID + manifest hash + migration IDs + backup/reference + actual build/tests + timing + operator + practical outcome. Script test PASS là unit/synthetic/mocked boundary; không thay thế actual IIS acceptance trên server được cấp phép. GaoMart comparison gồm root package bytes/IIS site state, bindings, app list, pool state; external data hoặc writes từ chính GaoMart có thể tạo khác biệt cần điều tra, script không tự sửa hay rollback GaoMart. Package ACL, database backup authenticity và việc quiesce external writers là trách nhiệm vận hành được chứng thực, không suy ra chỉ từ một tham số.

**TH1: NO MIGRATOR / NO SEED / NO DB CHANGE bởi deployment. TH2: SCHEMA-ONLY / NO AUTOMATIC SEED. Sau candidate và report: STOP FOR HUMAN REVIEW.**
