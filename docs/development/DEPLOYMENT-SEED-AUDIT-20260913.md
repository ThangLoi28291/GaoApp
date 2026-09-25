# Audit seed, bootstrap và dữ liệu khi triển khai — 2026-09-13

BMAD ROLE: Amelia — Senior Software Engineer. PROJECT: GaoApp. ROUTE: HIGH-RISK.
Authority trước sửa: GAOAPP-SA-20260913-03, parent -02; source `D:\GaoApp\GAOAPP-SA-20260819-01`; branch `fix/r2-4-c1-invoice-identity-uniqueness`; HEAD `db6a6c5e98b69472624eeb31aa6be2161576c3d2`.
Pre-edit: 499/499 checksum PASS; status/manifest 2169, full-tree 3538, staged 0, diff-check 0, status differences 0, byte failures 0. Planning đã ghi human-approved -03 và không được sửa trong remediation này.

## Kết luận và ranh giới

Trước sửa, Migrator không có mode bắt buộc: validate → preflight → MigrateAsync → transaction → MandatorySecuritySeeder ở cả trường hợp không bật seed. Demo và production bootstrap là nhánh cấu hình phía sau migration. Bootstrap chạy lại cũng reconcile security trước Apply, nên “đã khởi tạo” không có nghĩa là giữ nguyên customization. Web Development gọi MigrateAndSeedDatabaseAsync ở mọi startup; Production/Staging vốn không gọi extension đó. Một lần IIS restart Development vì vậy có thể ghi DB.

Sau sửa, CLI parse một mode duy nhất trước Host/DI/config/SQL. No/invalid/duplicate/combined mode trả exit 2. `--schema-only` validate cờ an toàn → preflight chỉ đọc → EF MigrateAsync → preflight lần hai đòi CurrentBaseline, applied count bằng source count → marker `SCHEMA_ONLY_VERIFIED` → return. Không mở provisioning transaction, không gọi bất kỳ seeder/bootstrapper nào. Lỗi trả nonzero, không in connection/password hay exception provider. Data modes đòi schema hiện hành, không migrate.

Normal Web startup mọi environment chỉ validate cấu hình và tạo HTTP pipeline. Nhánh `--recover-admin-menus-only` có sẵn vẫn là thao tác riêng, kết thúc process trước normal startup; không nằm trong TH1/TH2. Extension tự migrate/seed đã bỏ implementation. Không thay đổi seeder đồng bộ để tự chọn business authority. Web không khởi tạo user/role/menu/master data khi khởi động.

Phạm vi tìm kiếm: Domain, Application, Infrastructure, Web, Migrator, LabelPrintServer, Tests, Tests.Browser, scripts, eng/deployment, appsettings, PublishSafety.targets và toàn bộ 29 migration + designer + model snapshot. Truy vết các nhóm Seed/Seeder/HasData/Bootstrap/Initialize/EnsureCreated/Migrate/ExecuteSql/InsertData/UpdateData/DeleteData/INSERT/UPDATE/DELETE/MERGE/Upsert/DefaultAdmin/Demo/Permission/Role/Menu/Store/Tenant/LegalEntity và registrations IHostedService. Phân biệt declaration/caller với DTO/options, SQL inventory chỉ đọc, CRUD nghiệp vụ và fixture. Không suy ra initializer chỉ từ chữ “default”. `HasData(...)`, `EnsureCreated(...)`, `InsertData/UpdateData/DeleteData` trong runtime/migration: không có. `HasDatabaseName` không phải HasData.

## Inventory initializer và mutation

Đường dẫn bên dưới tương đối source root. “Idempotent” chỉ nói trạng thái dự kiến khi chạy lặp; không có nghĩa là không ghi hoặc bảo toàn tùy chỉnh. I/U/D = INSERT/UPDATE/DELETE hoặc reset.

| EntryPoint / file | Caller / mode, điều kiện | Class | ImplicitBefore | DataAffected | Insert | Update | Delete / Reset | Idempotent / customization | Risk / remediation | ImplicitAfter |
|---|---|---|---|---|---|---|---|---|---|---|
| MigrationExecutionPipeline.RunAsync; Infrastructure/Data/Migrations | Migrator/MigrationRunner, trước: mọi lần chạy; sau: SchemaOnly | SCHEMA_MIGRATION | Có | schema, history, DML Up đã review | EF schema/history + Up | Up + schema mechanics | Up/DDL được review | EF history kiểm soát, không hứa tất cả DDL transactional | Tách hoàn toàn seed, verify schema sau migration | Chỉ explicit schema-only |
| DatabaseStartupExtensions.MigrateAndSeedDatabaseAsync; Web/Configuration | Web Program, Development | SECURITY_SEED / SCHEMA_MIGRATION | Có | schema, permissions, legal/purchase/label menus, roles, role grants; demo optional | Có | Theo seeder | Theo seeder | Startup có side effect | Bỏ caller và toàn bộ implementation; test tự sở hữu fixture | Không |
| MandatorySecuritySeeder.SeedAsync; Infrastructure/Data/Seed/MigrationSeedRunners.cs | Pipeline trước mọi mode; sau chỉ SecuritySeed | SECURITY_SEED | Có | Permissions, AdminMenuItems, Roles, RolePermissions | Có | Menu canonical | Soft delete menu trùng, reset menu, bù lại default grants | Canonical idempotent, không giữ system-menu customization hoặc quyền default bị gỡ | Chỉ `--security-seed`, human phải duyệt dữ liệu | Không |
| SecuritySeedData.SeedPermissionsAsync | Mandatory, default-role helper | PERMISSION_SEED | Có qua callers | Permissions | Thêm code thiếu, so case-insensitive | Không sửa Name/GroupName đã có | Không | Additive; giữ metadata đã có | Thiếu permission phải seed riêng hoặc thao tác quản trị có review | Không qua deploy |
| AdminMenuSeeder.SeedAsync / ReconcileStore / ApplyCanonical; Infrastructure/Data/Seed | Mandatory, bootstrap mới; Web recovery flag riêng | MENU_SEED / REPAIR_RECONCILIATION | Có qua Mandatory | AdminMenuItems của Store active, không deleted; system rows kể cả deleted | Canonical thiếu | Title, parent/tree, route, URL, icon, permission, order; active/system/deleted flags | Re-activate canonical; soft-delete duplicates, clear deletion metadata winner | Không giữ customization trên IsSystem=true; custom non-system không reconcile | Giữ nguyên semantics; cô lập explicit, không dùng trong deploy | Không |
| SecuritySeedData.SeedLegalEntityAdminMenusAsync | Mandatory, bootstrap mới; trước Web Dev | MENU_SEED | Có | AdminMenuItems | LegalEntityManagement/Reconciliation thiếu ở Store active | Không | Không | Chỉ thêm thiếu theo controller, giữ existing inactive | Explicit security/bootstrap hoặc test setup | Không |
| SecuritySeedData.SeedPurchaseAdminMenusAsync | Mandatory, bootstrap mới; trước Web Dev | MENU_SEED | Có | AdminMenuItems | Parent “Mua hàng”/requests/orders/receipts thiếu | Không sửa existing | Không | Additive theo route và permission, có suy luận parent | Không tự thay business tree; explicit only | Không |
| ProductLabelMenuSeeder.SeedAsync; Infrastructure/Data/Seed | Trước Web Dev, hiện test fixture / test chuyên biệt | MENU_SEED | Có | AdminMenuItems, hai menu label ở Store active | Chỉ thêm khi chưa có controller/URL nondeleted | Không | Không | Idempotent, transaction + sp_getapplock; không join outer transaction | Bỏ khỏi Web; canonical label menus đã nằm trong explicit security/bootstrap, không thêm mode thừa | Không |
| SecuritySeedData.SeedDefaultRolesForStoreAsync / ForAllStoresAsync / SyncDefaultRolePermissionsAsync / EnsureRolePermissionsAsync | Mandatory, bootstrap, user-store helper | ROLE_SEED | Có | Roles ADMIN/MANAGER/CASHIER/WAREHOUSE, Permissions, RolePermissions | Role/code/grants thiếu | Không sửa role name/code | Không xóa grants; nhưng bù lại default grant đã cố tình thu hồi | Additive nhưng không bảo toàn việc thu hồi default permission | Explicit security/bootstrap; human review grants trước chạy | Không qua deploy |
| DemoDataSeeder.SeedAsync; SecuritySeedData.SeedUsersAsync / SeedUserInStoresAsync | Trước config EnableDemo sau mandatory; sau chỉ DemoSeed + flag true, Development | DEMO_SEED | Có nếu cờ bật | Users admin/manager/warehouse/cashier, UserInStores | Users chỉ khi Users rỗng; mappings thiếu theo role sẵn có | Không reset password/user/mapping đã có | Không | Có điều kiện; DB có user thì không tạo lại bộ demo | Không Production/Staging; schema và role setup là explicit prerequisites | Không |
| SecuritySeedData.SeedUserInStoreAsync | ProductionBootstrapper mới và test helpers | ROLE_SEED / OTHER_DATA_MUTATION | Gián tiếp bootstrap | UserInStores, default roles/permissions | Missing membership | Existing RoleId, IsActive | Có thể đổi role mapping | Không giữ mapping khi chủ động gọi helper | Chỉ bootstrap mới/test; không normal startup/deploy | Không |
| ProductionBootstrapper.InspectAsync / ApplyAsync; Infrastructure/Data/Seed | Bootstrap + Enabled=true; empty Stores+Users; serializable transaction và kiểm lại plan | PRODUCTION_BOOTSTRAP / MASTER_DATA_SEED / DEFAULT_ADMIN_SEED | Có khi config bật | Store, LegalEntity, Warehouse, POSTerminal, named admin User, mapping, security/menu | Bộ tenant/master/admin mới | Link default warehouse trên entity vừa tạo | Không reset DB có dữ liệu; partial/different reject | Matching returns no-op; pipeline không seed trước nó nữa | Cờ + mode, human supplied strong password; no hardcoded admin password | Không |
| SeedDataOptions.EnableDefaultAdminSeed | Options/config; không có standalone admin implementation | DEFAULT_ADMIN_SEED | Không có seeder độc lập thực tế | Không | Không | Không | Không | Cờ cũ gây hiểu nhầm | Migrator reject true; named admin chỉ qua bootstrap | Không |
| docs/pos-hold-permission-repair.sql | Human SQL invocation, StoreId cụ thể, @Apply=0 mặc định | PERMISSION_SEED / ROLE_SEED / REPAIR_RECONCILIATION | Không | Permissions pos.order.hold, ADMIN/CASHIER RolePermissions của Store | Chỉ thiếu | Không | Không | Additive, có thể bù default grant đã thu hồi | Manual exception, không gọi từ deploy | Không |
| docs/pos-receipt-templates-legacy-repair.sql | Human SQL hoặc ReceiptTemplateLegacyRepairTests | REPAIR_RECONCILIATION | Không | PosReceiptTemplates DefinitionJson schema, __EFMigrationsHistory | Không business rows | Widen nvarchar(max), chuyển đúng legacy MigrationId sau strict fingerprint checks | Không template reset; rollback transaction nếu fail | Known legacy/current states; không general repair | Giữ riêng, không auto chạy khi preflight từ chối | Không |
| 7 docs/*-upgrade.sql (liệt kê bên dưới) | Human SQL, không runtime caller | SCHEMA_MIGRATION / SCHEMA_REQUIRED_DATA_TRANSFORMATION | Không | schema/history và DML migration tương ứng | Schema/history; ACB item backfill | ACB pagination backfill | DDL theo script | Idempotent theo history; không thay thế preflight runtime | Tài liệu lịch sử; TH2 mới chỉ Migrator schema-only đã review | Không |
| InventoryBalanceRepository.GetOrCreateAsync; Infrastructure/Repositories/Inventory | Inventory movement/reservation/revaluation nghiệp vụ, tenant-scoped | OTHER_DATA_MUTATION | Không startup | InventoryBalances | Tạo zero balance nếu thiếu | Caller nghiệp vụ ghi số lượng/cost | Không initializer reset | Get existing hoặc tạo mới theo thao tác nghiệp vụ | Giữ, không gọi trong deployment | Không initializer |
| ProductVariantRepository.EnsureVariantInfrastructureAsync; Infrastructure/Repositories/Products | Create/update variant nghiệp vụ | OTHER_DATA_MUTATION | Không startup | ProductUnitConversions, ProductVariantUnitBarcodes | Base conversion/barcode thiếu | Business caller khác có update | Không startup reset | Missing-only; barcode conflict checks | Giữ nghiệp vụ, không seed master toàn DB | Không initializer |
| ReceiptTemplateService.BuiltIns/List/GetStoreInfo; Web/Services/Printing | Request đọc template | REFERENCE_DATA_SEED (in-memory only) | Không DB init | DTO built-ins; đọc PosReceiptTemplates/Stores | Không | Không | Không | Built-ins trong bộ nhớ, custom saved rows giữ nguyên | Save/Delete là user action riêng, không deployment seed | Không |
| Test fixture initializers, appendix | .NET tests / Tests.Browser reflection harness | TEST_ONLY_SEED | Chỉ test | Disposable local SQL/EF InMemory, synthetic accounts/catalog/order/receipt/payment/media | Có | Có, failure injection | Có, disposal drops DB theo prefix guard | Reset-per-fixture, không production API | FullApplicationFixture nay explicit seed đúng dataset Development trước cũ; published fixture tách schema và bootstrap | Chỉ test |

## Workers và side effect ngoài initializer

- `Web/Services/Acb/AcbCallbackWorker`: hosted worker đọc receipt đến hạn, gọi `AcbCallbackInbox.ProcessAsync`, ghi trạng thái callback, reconciliation/payment/audit theo nghiệp vụ đã nhận. Không migrate/seed canonical data. Được giữ nguyên. Không thể hứa DB không đổi khi ứng dụng đang phục vụ hoặc queue đang chạy.
- `Web/Services/Media/MediaCleanupWorker` → `MediaLibraryService.SweepAsync/ProcessAsync`: Enabled mặc định true; chờ một phút rồi định kỳ theo retention. Ghi ExpireAtUtc, tombstone IsDeleted/deletion metadata; xóa file đã hết hạn sau kiểm tra reference và alias cross-tenant. Đây là lifecycle media đã có, không bootstrap/master-data sync. Không đổi retention hoặc tắt tính năng Product trong remediation deployment. Khi so sánh byte/dữ liệu tĩnh, quiesce worker; deployment không thay thế C:\GaoAppData.
- `Web/Hubs/PosSessionRevocationWorker`: định kỳ kiểm lại quyền các connection SignalR rồi đóng connection; không seed/repair persistent DB.
- `LabelPrintServer/PrintWorker` → LabelPrintDispatcher: riêng executable, đọc printer enabled và xử lý queue, ghi job status và gọi printer. TH1/TH2 không start/stop service này; human phải quiesce trước TH2 nếu cùng DB.
- AppDbContext SaveChanges và AuditSaveChangesInterceptor chỉ chạy theo operation ghi đã được caller yêu cầu (timestamps/normalization/audit). Repository raw SQL locks/updates ở receipt intake, barcode, invoices, bank accounts, payments, media, label dispatcher là nghiệp vụ, không DI/startup initializer.
- StartupValidation/DataProtection tạo/kiểm thư mục, key persistence và file probe; readiness có storage probe. Không DB seed. Static browser IndexedDB/cache/local-storage initialization và schema upgrade của offline UI là dữ liệu client lúc dùng app, không SQL/server deploy; không sửa JS/UI trong task này.
- `SqlServerDatabaseBaselinePreflight`, object inventory, schema snapshot/catalog chỉ đọc metadata/history. SQL strings mô tả migration trong schema-manifest catalog không được execute để repair dữ liệu.

## EF migration data audit — giữ nguyên bytes lịch sử

| Migration | DML / schema-owned transformation trong Up | Giới hạn và Down |
|---|---|---|
| 20260817090000_AddPurchaseReceiptCostCapitalizationPolicy | UPDATE StockDocument confirmed Status=3 giữ policy VAT/freight lịch sử; UPDATE StockDocumentLine unconfirmed freight allocation về 0 | Thay dữ liệu theo policy migration; Down bỏ cột, không phục hồi allocation trước |
| 20260817150000_AddInputInvoiceIdentityUniqueness | UPDATE active InputInvoiceHead normalized seller tax/series/number/date; THROW nếu duplicate active identity/XML hash | Tạo unique indexes sau check; không tự chọn/xóa bản duplicate |
| 20260824150000_AddInputInvoiceBuyerOwnerGuard | UPDATE tất cả InputInvoiceHead buyer resolution từ exact normalized same-store active LegalEntity; UPDATE confirmed purchase StockDocument owner từ Warehouse | THROW khi owner/link không deterministic; computed normalized tax columns; không heuristic supplier/name |
| 20260826150000_AddInputInvoiceItemCatalogMapping | UPDATE active InputInvoiceDetail normalized ItemName/UnitName | Không suy luận reusable supplier mapping từ receipt history |
| 20260830112901_AddReceivingWorkbench | UPDATE active PO receipt lines allocation/status; UPDATE active PO receipts session state/revision theo document status | Read-only guards duplicate PO/receipt allocation; không tự merge/xóa duplicate |
| 20260908192844_AddAcbQrNotificationReconciliation | UPDATE AcbCallbackReceipts.TotalPages từ JSON; INSERT AcbQrNotificationItems từ valid historical transaction JSON | Filter valid date/amount/status/length; Down guard collision, drops new table/columns |
| 20260909055844_EnforceSingleDefaultBankAccount | Không DML; SELECT/THROW nếu nhiều default trước unique index | Không tự chọn default bank hay reset data |
| 20260909061611_EnableSnapshotProfitReads | ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON, suppressTransaction=true; tạo indexes | Database setting ngoài transaction; Down cố ý giữ ON, không tự tắt |

Không migration nào dùng HasData/InsertData/UpdateData/DeleteData. Sáu migration có explicit row DML, hai migration khác có raw SQL guard/database setting. Những migration còn lại chỉ schema operations; non-null default/computed column/constraint vẫn có thể tác động dữ liệu/khả năng ghi theo schema mechanics. Không thể đồng nhất “không seed” với “mọi migration không đổi dữ liệu”. TH2 chỉ được chạy các Up đã human review, không rewrite historical migration.

Manual upgrade scripts: `docs/acb-qr-list-upgrade.sql`, `docs/acb-confirmation-audit-upgrade.sql`, `docs/acb-callback-store-routing-upgrade.sql`, `docs/pos-qr-installments-upgrade.sql`, `docs/pos-receipt-templates-upgrade.sql`, `docs/pos-receipt-store-identity-upgrade.sql`, `docs/product-label-printing-upgrade.sql`. Chúng không được gọi bởi hai deploy scripts mới. Các INSERT __EFMigrationsHistory là schema bookkeeping, không application seed.

## Explicit operations và business decisions còn mở

1. `dotnet GaoApp.Migrator.dll --schema-only`: demo/default-admin/bootstrap flags false; chỉ schema/history/Up đã review; unknown history/schema từ chối, không tự repair.
2. `dotnet GaoApp.Migrator.dll --security-seed`: flags false, current schema; permission/role missing inserts và canonical system-menu reconciliation. Có UPDATE/soft-delete/reset menu, có re-grant default permission. Phải có phê duyệt human cho chính data delta. Không có cam kết giữ mọi custom system-menu value. Ownership của Title/order/route/active trên system menus và quyền default bị gỡ vẫn là quyết định Product/Human chưa được đoán thay.
3. `dotnet GaoApp.Migrator.dll --bootstrap`: ProductionBootstrap.Enabled=true, demo/default-admin=false, đủ fields/password; chỉ DB đã có current schema nhưng Stores+Users rỗng hoặc matching plan. Empty tạo master/admin/security; matching no-op; partial/different reject. Không reset mật khẩu hiện có.
4. `dotnet GaoApp.Migrator.dll --demo-seed`: Development và EnableDemoSeed=true, bootstrap/default-admin=false; schema/roles phải đã chuẩn bị riêng. Chỉ demo users/mappings, không gọi security mode hoặc migration. Không dùng server DB.
5. `dotnet GaoApp.Web.dll --recover-admin-menus-only`: giữ compatibility cho manual menu recovery; chỉ menu canonical, không permission/role/bootstrap/migration. Không gắn flag này vào IIS arguments. Hai deploy scripts kiểm web.config chỉ có argument DLL bình thường.

Không thêm mode riêng cho từng helper additive vì chưa có workflow/operator requirement rõ ràng. Cần menu/quyền mới nhưng muốn giữ toàn bộ customization: human review delta và thực hiện qua quản trị dữ liệu/phương án additive riêng đã duyệt; không chạy bulk security-seed để “thử sửa”.

## Tooling và verification contract

`eng/deployment/Deploy-WebOnly.ps1` chỉ nhận Web-only package, bắt trusted manifest hash, kiểm bytes/defaults/IIS arguments, paths disjoint, không reparse, pool riêng, backup Web có byte verify, switch physicalPath tới release immutable, stop/start chỉ GaoApp/GaoAppPool, HTTPS readiness và GaoMart config/files/state comparison. Failure TH1 phục hồi old physicalPath. Không SQL process hoặc DB write.

`Deploy-WithSchemaMigration.ps1` bắt human review bind exact DB + manifest + environment; Production đòi restore-verified backup evidence mới, Test chỉ waive explicit. Cờ WritersQuiesced xác nhận các writer khác đã dừng. Tool dừng GaoApp/pool trước schema để tránh worker/HTTP writer tranh chấp, schema-only đúng một process, exit/verification marker bắt buộc. Nếu fail, không deploy mới/không auto retry/Down, giữ GaoApp dừng. Nếu Web activation lỗi sau migration cũng không auto chạy old Web trên schema mới. Đây là bổ sung maintenance an toàn vào TH2.

`publish-staging-release.ps1 -WebOnly` và `test-release-package.ps1 -WebOnly` hỗ trợ TH1 không publish/invoke Migrator. Mặc định paired package giữ convention test cũ. `scripts/test-deployment-safety.ps1` chạy được Windows PowerShell 5.1; syntax + synthetic package/tamper/path/approval + mocked IIS/process failure/order checks; không phải IIS deployment nghiệm thu.

Verification cuối cùng phải lấy từ candidate -04 sau byte freeze, gồm full regression mới; không tái dùng PASS -03. Evidence lịch sử của các attempt lỗi vẫn được giữ. Production deployment, production migration/seed và human approval chưa thực hiện.

## Danh sách chính xác 29 migration (không thay đổi tệp)

- `20260726073029_InitialProductionBaseline`
- `20260801110856_AddInventoryPostingIdempotency`
- `20260814090000_AddPurchaseReceiptAuditEvents`
- `20260817090000_AddPurchaseReceiptCostCapitalizationPolicy`
- `20260817150000_AddInputInvoiceIdentityUniqueness`
- `20260822090000_AddInputInvoiceSupplierResolution`
- `20260824150000_AddInputInvoiceBuyerOwnerGuard`
- `20260826150000_AddInputInvoiceItemCatalogMapping`
- `20260827150000_AddInputInvoiceReconciliation`
- `20260828150000_EnforceSingleActiveInputInvoicePerReceipt`
- `20260830112901_AddReceivingWorkbench`
- `20260831135031_AddProvisionalReceivingItems`
- `20260908151919_AddStoreAcbPayments`
- `20260908155844_AddAcbCallbackInbox`
- `20260908192844_AddAcbQrNotificationReconciliation`
- `20260909015242_AddPosQrInstallmentLinks`
- `20260909024821_AddAcbConfirmationAudit`
- `20260909055844_EnforceSingleDefaultBankAccount`
- `20260909061611_EnableSnapshotProfitReads`
- `20260909062834_AddAcbCallbackStoreRouting`
- `20260909080000_AddPosCollectionIdempotency`
- `20260909100000_AddSupplierBankFields`
- `20260909210000_AddPosOfflineJournal`
- `20260910002000_AddPosReceiptTemplates`
- `20260910012000_AddStoreReceiptIdentity`
- `20260910040620_AddProductLabelPrinting`
- `20260911053655_AddReceiptIntakePacking`
- `20260912120000_AddCustomerDisplayWifi`
- `20260912150000_AddReceivingPackagingPhoto`

## Appendix — test initializer declarations

Các declaration dưới đây thuộc test/harness; không đăng ký trong production DI. Fixture tạo dữ liệu giả, test có thể INSERT/UPDATE/DELETE/DDL/failure injection; disposal chỉ xóa DB được tạo theo prefix guard. Không coi fixture PASS là seed production approval. `Ten` bị loại khỏi project compile và không được dùng làm runtime evidence.

| File:line | Helper | Classification / caller |
|---|---|---|
| `GaoApp.Tests/Configuration/AtomicProvisioningTests.cs:317` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseBaselinePreflightTests.cs:496` | `CreateDatabaseAsync_should_create_database_and_verify_DB_ID` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseBaselinePreflightTests.cs:518` | `CreateDatabaseAsync_when_command_reports_timeout_but_database_exists_should_succeed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseBaselinePreflightTests.cs:576` | `CreateDatabaseAsync_when_command_times_out_and_database_does_not_exist_should_rethrow_original_exception` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseBaselinePreflightTests.cs:625` | `CreateDatabaseAsync_when_normal_create_readiness_is_false_should_fail` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseBaselinePreflightTests.cs:655` | `CreateDatabaseAsync_when_timeout_database_exists_but_readiness_is_false_should_rethrow_original_exception` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseBaselinePreflightTests.cs:820` | `CreateDatabaseIndependentlyAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseBaselinePreflightTests.cs:1080` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseBaselinePreflightTests.cs:1091` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseBaselinePreflightTests.cs:1199` | `CreateDatabaseAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseBaselinePreflightTests.cs:1252` | `ExecuteCreateDatabaseCommandAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseHealthCheckTests.cs:342` | `CreateDatabaseConnectionProbe` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseObjectInventoryTests.cs:218` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseObjectInventoryTests.cs:229` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseSchemaManifestTests.cs:659` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseSchemaManifestTests.cs:670` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseSecurityMetadataTests.cs:528` | `CountingMandatorySeeder` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseSecurityMetadataTests.cs:534` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/DatabaseSecurityMetadataTests.cs:546` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/InventoryPostingLocalDb.cs:83` | `SeedInventoryCatalogAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/InventoryPostingLocalDb.cs:324` | `InventoryPostingSeed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/MigrationRunnerOrderingTests.cs:268` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/MigrationRunnerOrderingTests.cs:287` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/ReceiptTemplateLegacyRepairTests.cs:73` | `PrepareLegacyAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Configuration/StartupOrderingContractTests.cs:212` | `Migrator_ValidatesSeedOptionsBeforeDatabaseMigration` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceIdentitySqlServerConcurrencyTests.cs:505` | `SeedReceiptAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceItemCatalogMappingSqlServerConcurrencyTests.cs:135` | `SeedMultiUnitAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceItemCatalogMappingSqlServerConcurrencyTests.cs:256` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceItemCatalogMappingSqlServerConcurrencyTests.cs:373` | `Seed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceItemCatalogMappingSqlServerConcurrencyTests.cs:384` | `MultiUnitSeed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoicePickerLinkUnlinkSqlServerTests.cs:697` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoicePickerLinkUnlinkSqlServerTests.cs:842` | `SeedLinkReceiptAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoicePickerLinkUnlinkSqlServerTests.cs:920` | `SeedGenuinePostedEffectsAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoicePickerLinkUnlinkSqlServerTests.cs:1140` | `Seed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoicePickerLinkUnlinkSqlServerTests.cs:1150` | `LinkSeed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceSupplierResolutionServiceTests.cs:205` | `Seed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceSupplierResolutionSqlServerConcurrencyTests.cs:111` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceSupplierResolutionSqlServerConcurrencyTests.cs:211` | `ResolutionSeed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceXmlIdentityServiceTests.cs:820` | `SeedReceipt` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceXmlTenantGuardTests.cs:531` | `SeedStore` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceXmlTenantGuardTests.cs:549` | `SeedLegalEntity` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceXmlTenantGuardTests.cs:571` | `SeedReceipt` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceXmlTenantGuardTests.cs:621` | `SeedInvoice` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceXmlTenantGuardTests.cs:651` | `SeedInvoiceLink` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InputInvoiceXmlTenantGuardTests.cs:666` | `SeedLineMap` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InventoryNonPosPostingContractTests.cs:1626` | `SeedInitialInventoryAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InventoryPosPostingContractTests.cs:912` | `RelationalSalesReturnSeed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InventoryPosPostingContractTests.cs:1132` | `SeedBehaviorOrderAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InventoryReservationServiceTests.cs:440` | `SeedMultiLegalEntityAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InventoryReservationServiceTests.cs:526` | `SeedLegacyReservationAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Inventory/InventoryRevaluationPostingContractTests.cs:122` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Invoices/DraftInvoiceReturnSyncServiceTests.cs:67` | `SeedSplitInvoiceAndReturnAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Invoices/InvoiceInputStockRepositoryTests.cs:281` | `SeedBaseAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Invoices/InvoiceInputStockRepositoryTests.cs:485` | `SeededData` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Invoices/LegalEntityInvoiceGenerationTests.cs:154` | `SeedOrderAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Invoices/LegalEntityInvoiceGenerationTests.cs:332` | `SeededData` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/LegalEntities/LegalEntityManagementTests.cs:249` | `SeedTwoEntitiesAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/LegalEntities/LegalEntityManagementTests.cs:387` | `SeededData` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/LegalEntities/LegalEntityReconciliationTests.cs:109` | `SeedSplitOrderAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/LegalEntities/LegalEntityReconciliationTests.cs:295` | `SeededData` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Products/ProductVariantRepositorySearchTests.cs:151` | `SeedSearchGraph` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Products/ProductVariantUnitBarcodeRepositoryTests.cs:263` | `SeedBarcodeGraph` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Purchases/ProvisionalReceivingSqlServerTests.cs:570` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Purchases/ProvisionalReceivingSqlServerTests.cs:759` | `Seed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Purchases/PurchaseOrderCloseReopenSqlServerConcurrencyTests.cs:58` | `SeedOrderAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Purchases/PurchaseReceiptAuditSqlServerTransactionTests.cs:195` | `SeedReceiptAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Purchases/PurchaseReceiptAuditWorkflowTests.cs:206` | `SeedReceiptAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Purchases/PurchaseToReceiptFinalCoverageSqlServerTests.cs:445` | `SeedCatalogAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Purchases/ReceivingWorkbenchSqlServerTests.cs:575` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Purchases/ReceivingWorkbenchSqlServerTests.cs:687` | `Seed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Reports/LocalReportLoadTests.cs:235` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Security/FullApplicationFixture.cs:21` | `StoreSeed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Security/ProductLabelSqlServerTests.cs:232` | `SeedReceipt` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Security/ReceiptBarcodeProposalSqlServerTests.cs:252` | `Seed` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |
| `GaoApp.Tests/Security/ReceiptBarcodeProposalSqlServerTests.cs:253` | `SeedAsync` | TEST_ONLY_SEED / explicit test setup or fixture lifecycle; no production caller |

Generated source-side SQL `.artifacts/receipt-intake-upgrade.sql` cũng đã kiểm: ALTER cột/FK và INSERT __EFMigrationsHistory cho receipt-intake migration; không application-row seed/reset, không được hai deployment scripts gọi. Đây là artifact DDL lịch sử, không authority thay thế migration/preflight.
