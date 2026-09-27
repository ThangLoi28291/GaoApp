using GaoApp.Application.Common.Options;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Seed;
using GaoApp.Infrastructure.Security;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

public sealed class ProductionBootstrapTests
{
    private const string BaselineMigrationId =
        "20260726073029_InitialProductionBaseline";

    private const string InventoryPostingMigrationId =
        "20260801110856_AddInventoryPostingIdempotency";

    private const string PurchaseReceiptAuditMigrationId =
        "20260814090000_AddPurchaseReceiptAuditEvents";
    private const string PurchaseReceiptCostPolicyMigrationId =
        "20260817090000_AddPurchaseReceiptCostCapitalizationPolicy";
    private const string InputInvoiceIdentityMigrationId =
        "20260817150000_AddInputInvoiceIdentityUniqueness";
    private const string InputInvoiceSupplierResolutionMigrationId =
        "20260822090000_AddInputInvoiceSupplierResolution";
    private const string InputInvoiceBuyerOwnerGuardMigrationId =
        "20260824150000_AddInputInvoiceBuyerOwnerGuard";
    private const string InputInvoiceItemCatalogMappingMigrationId =
        "20260826150000_AddInputInvoiceItemCatalogMapping";
    private const string InputInvoiceReconciliationMigrationId =
        "20260827150000_AddInputInvoiceReconciliation";
    private const string InputInvoiceSingleActiveReceiptMigrationId =
        "20260828150000_EnforceSingleActiveInputInvoicePerReceipt";
    private const string ReceivingWorkbenchMigrationId =
        "20260830112901_AddReceivingWorkbench";
    private const string ProvisionalReceivingItemsMigrationId =
        "20260831135031_AddProvisionalReceivingItems";
    private const string AcbPaymentsMigrationId =
        "20260908151919_AddStoreAcbPayments";
    private const string AcbCallbackInboxMigrationId =
        "20260908155844_AddAcbCallbackInbox";

    [Fact]
    public void Disabled_configuration_is_valid_without_bootstrap_values()
    {
        new ProductionBootstrapOptions
        {
            Enabled = false
        }.HasValidConfiguration().Should().BeTrue();
    }

    [Fact]
    public void Enabled_configuration_requires_a_strong_password()
    {
        var options = CreateValidOptions();
        options.AdminPassword = "too-short";

        options.HasValidConfiguration().Should().BeFalse();
    }

    [Fact]
    public void Enabled_configuration_rejects_invalid_subdomain()
    {
        var options = CreateValidOptions();
        options.StoreSubdomain = "Invalid Domain";

        options.HasValidConfiguration().Should().BeFalse();
    }

    [Fact]
    public void Enabled_configuration_rejects_reserved_admin_subdomain()
    {
        var options = CreateValidOptions();
        options.StoreSubdomain = "admin";

        options.HasValidConfiguration().Should().BeFalse();
    }

    [Fact]
    public void Enabled_configuration_rejects_invalid_admin_email()
    {
        var options = CreateValidOptions();
        options.AdminEmail = "not-an-email";

        options.HasValidConfiguration().Should().BeFalse();
    }

    [Fact]
    public async Task Empty_database_is_bootstrapped_once_without_storing_raw_password()
    {
        var options = CreateValidOptions();
        await using var db = CreateContext();
        var sut = new ProductionBootstrapper(db, Options.Create(options));

        var firstPlan = await sut.InspectAsync();
        var first = await sut.ApplyAsync(firstPlan);
        var secondPlan = await sut.InspectAsync();
        var second = await sut.ApplyAsync(secondPlan);

        first.Should().Be(ProductionBootstrapResult.Created);
        second.Should().Be(ProductionBootstrapResult.AlreadyProvisioned);

        var store = await db.Stores.IgnoreQueryFilters().SingleAsync();
        var legalEntity = await db.LegalEntities.IgnoreQueryFilters().SingleAsync();
        var warehouse = await db.Warehouses.IgnoreQueryFilters().SingleAsync();
        var terminal = await db.POSTerminals.IgnoreQueryFilters().SingleAsync();
        var admin = await db.Users.IgnoreQueryFilters().SingleAsync();
        var mapping = await db.UserInStores.IgnoreQueryFilters().SingleAsync();

        store.SubDomainNormalized.Should().Be("first-store");
        legalEntity.DefaultWarehouseId.Should().Be(warehouse.Id);
        warehouse.LegalEntityId.Should().Be(legalEntity.Id);
        terminal.StoreId.Should().Be(store.Id);
        admin.IsHostAdmin.Should().BeTrue();
        admin.PasswordHash.Should().NotBe(options.AdminPassword);
        PasswordHasherHelper.Verify(
            options.AdminPassword!,
            admin.PasswordHash).Should().BeTrue();
        mapping.StoreId.Should().Be(store.Id);
        mapping.UserId.Should().Be(admin.Id);
        (await db.Roles.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == mapping.RoleId))
            .Code.Should().Be("ADMIN");

        var activeMenus = await db.AdminMenuItems.IgnoreQueryFilters()
            .Where(x => x.StoreId == store.Id && x.IsSystem && !x.IsDeleted && x.IsActive)
            .ToListAsync();
        activeMenus.Should().Contain(x =>
            x.Title == "Quản lý kho" &&
            x.Controller == "WarehouseManagement" &&
            x.PermissionCode == "inventory.warehouse.view");
        activeMenus.Should().Contain(x =>
            x.Title == "Tra cứu tồn kho" &&
            x.Url == "/admin/inventory-inquiry");
        activeMenus.Should().Contain(x =>
            x.Title == "Thẻ kho / Lịch sử giao dịch kho" &&
            x.Url == "/admin/inventory-ledger");
        activeMenus.Should().Contain(x =>
            x.Title == "Quản lý menu" &&
            x.PermissionCode == "security.role.permissions");

        var menuCount = activeMenus.Count;
        await AdminMenuSeeder.SeedAsync(db);
        (await db.AdminMenuItems.IgnoreQueryFilters()
            .CountAsync(x => x.StoreId == store.Id && x.IsSystem && !x.IsDeleted && x.IsActive))
            .Should().Be(menuCount);
    }

    [Fact]
    public async Task Partial_database_is_rejected_without_creating_admin()
    {
        var options = CreateValidOptions();
        await using var db = CreateContext();

        db.Stores.Add(new()
        {
            Name = "Existing",
            SubDomain = "existing",
            SubDomainNormalized = "existing",
            IsActive = true
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut = new ProductionBootstrapper(db, Options.Create(options));

        var plan = await sut.InspectAsync();
        db.ChangeTracker.Entries().Should().BeEmpty();
        var action = () => sut.ApplyAsync(plan);

        await action.Should()
            .ThrowAsync<ProductionBootstrapStateException>()
            .WithMessage("*ReasonCode=PartialOrDifferent*");
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.POSTerminals.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_database_changes()
    {
        await using var db = CreateContext();
        var sut = new ProductionBootstrapper(
            db,
            Options.Create(CreateValidOptions()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = () => sut.InspectAsync(cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        (await db.Stores.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public void AppDbContext_has_one_baseline_and_no_pending_model_changes()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                "Server=127.0.0.1,1;Database=DesignOnly;User Id=design;Password=not-used;Encrypt=False")
            .Options;
        var tenant = new TenantContext();
        tenant.SetHostAdmin();
        using var db = new AppDbContext(
            options,
            tenant,
            new BootstrapCurrentUser());

        var migrations = db.Database.GetMigrations().ToList();

        migrations.Should().BeEquivalentTo(
            [
                BaselineMigrationId,
                InventoryPostingMigrationId,
                PurchaseReceiptAuditMigrationId,
                PurchaseReceiptCostPolicyMigrationId,
                InputInvoiceIdentityMigrationId,
                InputInvoiceSupplierResolutionMigrationId,
                InputInvoiceBuyerOwnerGuardMigrationId,
                InputInvoiceItemCatalogMappingMigrationId,
                InputInvoiceReconciliationMigrationId,
                InputInvoiceSingleActiveReceiptMigrationId,
                ReceivingWorkbenchMigrationId,
                ProvisionalReceivingItemsMigrationId,
                AcbPaymentsMigrationId,
                AcbCallbackInboxMigrationId,
                "20260908192844_AddAcbQrNotificationReconciliation",
                "20260909015242_AddPosQrInstallmentLinks",
                "20260909024821_AddAcbConfirmationAudit",
                "20260909055844_EnforceSingleDefaultBankAccount",
                "20260909061611_EnableSnapshotProfitReads",
                "20260909062834_AddAcbCallbackStoreRouting",
                "20260909080000_AddPosCollectionIdempotency", "20260909100000_AddSupplierBankFields", "20260909210000_AddPosOfflineJournal", "20260910002000_AddPosReceiptTemplates", "20260910012000_AddStoreReceiptIdentity", "20260910040620_AddProductLabelPrinting", "20260911053655_AddReceiptIntakePacking",
                "20260912120000_AddCustomerDisplayWifi",
                "20260912150000_AddReceivingPackagingPhoto",
                "20260914073514_AddOrderRewardEligibilitySnapshots",
                "20260914154923_AddPOSShiftCashReceipt",
                "20260919095814_AddCustomerReceivables",
                "20260919111155_AddCustomerDeposits",
                "20260919111529_AddDepositReturnRestoration",
                "20260919173000_MakePurchaseOrderSupplierOptional",
                "20260919174500_AllowPurchaseOrderVariantMultipleUnits",
                "20260920093000_OptimizeInventoryLedgerTimeline",
                "20260921100000_AddInvoiceInputStockSupplementalMovements",
                "20260923140000_AddInvoiceStockLegacyDocumentReferences",
                "20260923160000_AddLegacyInvoiceImport",
                "20260923180000_AddLegacyReturnArchive",
                "20260924100000_AddAutoInvoiceIssuance",
"20260926113012_AddInvoiceIssuanceRoutingAndBuyerSelfService"
            ]);
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }

    private static ProductionBootstrapOptions CreateValidOptions()
        => new()
        {
            Enabled = true,
            StoreName = "First Store",
            StoreSubdomain = "first-store",
            LegalEntityCode = "LEGAL-01",
            LegalEntityName = "Primary Legal Entity",
            LegalEntityLegalName = "Primary Legal Entity Limited",
            WarehouseCode = "WAREHOUSE-01",
            WarehouseName = "Primary Warehouse",
            TerminalCode = "POS-01",
            TerminalName = "Primary POS",
            AdminUserName = "initial.admin",
            AdminFullName = "Initial Administrator",
            AdminEmail = "initial.admin@example.invalid",
            AdminPassword = "Bootstrap-Only-Test-42!"
        };

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase($"production-bootstrap-{Guid.NewGuid():N}")
            .Options;

        var tenant = new TenantContext();
        tenant.SetHostAdmin();

        return new InMemoryAppDbContext(
            options,
            tenant,
            new BootstrapCurrentUser());
    }

    private sealed class BootstrapCurrentUser : ICurrentUser
    {
        public int? UserId => null;
        public string? UserName => null;
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => false;
    }
}
