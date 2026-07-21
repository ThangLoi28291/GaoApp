using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.LegalEntities;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Seed;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.LegalEntities;

public sealed class LegalEntityManagementTests
{
    [Fact]
    public async Task Update_should_assign_owned_warehouse_invoice_and_switch_purchase_default()
    {
        await using var context = CreateContext();
        var data = await SeedTwoEntitiesAsync(context, completeConfiguration: false);
        var service = CreateService(context);

        var result = await service.UpdateAsync(new UpdateLegalEntityRequest
        {
            Id = data.Second.Id,
            Code = data.Second.Code,
            Name = data.Second.Name,
            LegalName = data.Second.LegalName,
            TaxCode = data.Second.TaxCode,
            Address = data.Second.Address,
            SalePriority = 2,
            DefaultWarehouseId = data.SecondWarehouse.Id,
            InvoiceProviderSettingId = data.SecondInvoiceSetting.Id,
            IsDefaultForPurchase = true
        });

        result.IsSuccess.Should().BeTrue();
        data.Primary.IsDefaultForPurchase.Should().BeFalse();
        data.Second.IsDefaultForPurchase.Should().BeTrue();
        data.Second.DefaultWarehouseId.Should().Be(data.SecondWarehouse.Id);
        data.Second.InvoiceProviderSettingId.Should().Be(data.SecondInvoiceSetting.Id);
    }

    [Fact]
    public async Task Update_should_reject_invoice_setting_assigned_to_another_entity()
    {
        await using var context = CreateContext();
        var data = await SeedTwoEntitiesAsync(context, completeConfiguration: false);
        var service = CreateService(context);

        var result = await service.UpdateAsync(new UpdateLegalEntityRequest
        {
            Id = data.Second.Id,
            Code = data.Second.Code,
            Name = data.Second.Name,
            LegalName = data.Second.LegalName,
            TaxCode = data.Second.TaxCode,
            Address = data.Second.Address,
            SalePriority = 2,
            InvoiceProviderSettingId = data.PrimaryInvoiceSetting.Id,
            IsDefaultForPurchase = false
        });

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Conflict");
    }

    [Fact]
    public async Task Set_active_should_block_default_purchase_and_active_warehouses()
    {
        await using var context = CreateContext();
        var data = await SeedTwoEntitiesAsync(context, completeConfiguration: false);
        var service = CreateService(context);

        var defaultPurchaseResult = await service.SetActiveAsync(data.Primary.Id, false);
        defaultPurchaseResult.IsSuccess.Should().BeFalse();
        defaultPurchaseResult.Error.Code.Should().Be(
            "LegalEntity.DefaultPurchaseCannotDeactivate");

        var activeWarehouseResult = await service.SetActiveAsync(data.Second.Id, false);
        activeWarehouseResult.IsSuccess.Should().BeFalse();
        activeWarehouseResult.Error.Code.Should().Be("LegalEntity.ActiveWarehouseExists");

        data.SecondWarehouse.IsActive = false;
        await context.SaveChangesAsync();

        var success = await service.SetActiveAsync(data.Second.Id, false);
        success.IsSuccess.Should().BeTrue();
        data.Second.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_should_report_configuration_blockers_without_enabling_feature()
    {
        await using var context = CreateContext();
        await AddStoreAsync(context);
        context.LegalEntities.Add(NewLegalEntity(
            "PRIMARY",
            priority: 1,
            isDefaultForPurchase: true,
            taxCode: null,
            address: null));
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetActivationPreflightAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.IsConfigurationReady.Should().BeFalse();
        result.Value.CanActivate.Should().BeFalse();
        result.Value.Checks.Should().Contain(x =>
            x.Code == "LegalEntity.ActiveCount" && !x.IsPassed);
        result.Value.Checks.Should().Contain(x =>
            x.Code.EndsWith(".DefaultWarehouse") && !x.IsPassed);

        var store = await context.Stores.SingleAsync();
        store.IsMultiLegalEntityEnabled.Should().BeFalse();
        store.MultiLegalEntityActivatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Preflight_should_be_configuration_ready_and_allow_activation()
    {
        await using var context = CreateContext();
        await SeedTwoEntitiesAsync(context, completeConfiguration: true);
        var service = CreateService(context);

        var result = await service.GetActivationPreflightAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.IsConfigurationReady.Should().BeTrue();
        result.Value.CanActivate.Should().BeTrue();
        result.Value.ActivationGateMessage.Should().Contain("Có thể bật");
        result.Value.Checks.Should().OnlyContain(x => x.IsPassed);

        var store = await context.Stores.SingleAsync();
        store.IsMultiLegalEntityEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Set_feature_should_enable_with_activation_timestamp_then_disable_to_legacy()
    {
        await using var context = CreateContext();
        await SeedTwoEntitiesAsync(context, completeConfiguration: true);
        var service = CreateService(context);

        var enable = await service.SetMultiLegalEntityEnabledAsync(true);

        enable.IsSuccess.Should().BeTrue();
        var store = await context.Stores.SingleAsync();
        store.IsMultiLegalEntityEnabled.Should().BeTrue();
        store.MultiLegalEntityActivatedAtUtc.Should().NotBeNull();
        store.MultiLegalEntityActivatedAtUtc.Should().BeCloseTo(
            DateTime.UtcNow,
            TimeSpan.FromSeconds(5));

        var firstActivatedAt = store.MultiLegalEntityActivatedAtUtc;
        var enableAgain = await service.SetMultiLegalEntityEnabledAsync(true);

        enableAgain.IsSuccess.Should().BeTrue();
        store.MultiLegalEntityActivatedAtUtc.Should().Be(firstActivatedAt);

        var disable = await service.SetMultiLegalEntityEnabledAsync(false);

        disable.IsSuccess.Should().BeTrue();
        store.IsMultiLegalEntityEnabled.Should().BeFalse();
        store.MultiLegalEntityActivatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Set_feature_should_reject_enable_when_preflight_has_blockers()
    {
        await using var context = CreateContext();
        await SeedTwoEntitiesAsync(context, completeConfiguration: false);
        var service = CreateService(context);

        var result = await service.SetMultiLegalEntityEnabledAsync(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LegalEntity.ActivationPreflightFailed");
        var store = await context.Stores.SingleAsync();
        store.IsMultiLegalEntityEnabled.Should().BeFalse();
        store.MultiLegalEntityActivatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Permission_catalog_should_publish_all_legal_entity_permissions()
    {
        var codes = PermissionCatalog.All.Select(x => x.Code).ToHashSet();

        codes.Should().Contain(PermissionCodes.System.LegalEntity.View);
        codes.Should().Contain(PermissionCodes.System.LegalEntity.Create);
        codes.Should().Contain(PermissionCodes.System.LegalEntity.Update);
        codes.Should().Contain(PermissionCodes.System.LegalEntity.Activate);
        codes.Should().Contain(PermissionCodes.System.LegalEntity.Reconcile);
    }

    [Fact]
    public async Task Legal_entity_menu_seed_should_be_idempotent_for_each_active_store()
    {
        var options = CreateOptions();
        var tenant = new TenantContext();
        tenant.SetHostAdmin();
        await using var context = new InMemoryAppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        context.Stores.AddRange(
            NewStore(1, "store-one"),
            NewStore(2, "store-two"));
        await context.SaveChangesAsync();

        await SecuritySeedData.SeedLegalEntityAdminMenusAsync(context);
        await SecuritySeedData.SeedLegalEntityAdminMenusAsync(context);

        var menus = await context.AdminMenuItems
            .IgnoreQueryFilters()
            .Where(x => x.Controller == "LegalEntityManagement")
            .ToListAsync();

        menus.Should().HaveCount(2);
        menus.Select(x => x.StoreId).Should().BeEquivalentTo(new[] { 1, 2 });
        menus.Should().OnlyContain(x =>
            x.PermissionCode == PermissionCodes.System.LegalEntity.View);

        var reconciliationMenus = await context.AdminMenuItems
            .IgnoreQueryFilters()
            .Where(x => x.Controller == "LegalEntityReconciliation")
            .ToListAsync();
        reconciliationMenus.Should().HaveCount(2);
        reconciliationMenus.Select(x => x.StoreId).Should().BeEquivalentTo(new[] { 1, 2 });
        reconciliationMenus.Should().OnlyContain(x =>
            x.PermissionCode == PermissionCodes.System.LegalEntity.Reconcile);
    }

    private static LegalEntityService CreateService(InMemoryAppDbContext context)
        => new(
            new LegalEntityRepository(context),
            new WarehouseRepository(context),
            new InvoiceProviderSettingRepository(context, new TestCredentialProtector()),
            new TestCurrentStore(1));

    private static async Task<SeededData> SeedTwoEntitiesAsync(
        InMemoryAppDbContext context,
        bool completeConfiguration)
    {
        await AddStoreAsync(context);

        var primary = NewLegalEntity(
            "HKD-1",
            1,
            isDefaultForPurchase: true,
            taxCode: "0100000001",
            address: "Địa chỉ HKD 1");
        var second = NewLegalEntity(
            "HKD-2",
            2,
            isDefaultForPurchase: false,
            taxCode: "0100000002",
            address: "Địa chỉ HKD 2");
        context.LegalEntities.AddRange(primary, second);
        await context.SaveChangesAsync();

        var invoice1 = NewInvoiceSetting("0100000001", "C26AAA");
        var invoice2 = NewInvoiceSetting("0100000002", "C26AAB");
        var warehouse1 = NewWarehouse(primary.Id, "KHO-HKD-1", "Kho bán HKD 1");
        var warehouse2 = NewWarehouse(second.Id, "KHO-HKD-2", "Kho bán HKD 2");
        context.InvoiceProviderSettings.AddRange(invoice1, invoice2);
        context.Warehouses.AddRange(warehouse1, warehouse2);
        await context.SaveChangesAsync();

        primary.InvoiceProviderSettingId = invoice1.Id;
        primary.DefaultWarehouseId = warehouse1.Id;
        if (completeConfiguration)
        {
            second.InvoiceProviderSettingId = invoice2.Id;
            second.DefaultWarehouseId = warehouse2.Id;
        }
        await context.SaveChangesAsync();

        return new SeededData(
            primary,
            second,
            warehouse1,
            warehouse2,
            invoice1,
            invoice2);
    }

    private static async Task AddStoreAsync(InMemoryAppDbContext context)
    {
        if (!await context.Stores.AnyAsync())
        {
            context.Stores.Add(NewStore(1, "store-one"));
            await context.SaveChangesAsync();
        }
    }

    private static Store NewStore(int id, string subdomain)
        => new()
        {
            Id = id,
            Name = subdomain,
            SubDomain = subdomain,
            SubDomainNormalized = subdomain.ToUpperInvariant(),
            IsActive = true,
            RowVersion = new byte[8]
        };

    private static LegalEntity NewLegalEntity(
        string code,
        int priority,
        bool isDefaultForPurchase,
        string? taxCode,
        string? address)
        => new()
        {
            StoreId = 1,
            Code = code,
            Name = code,
            LegalName = $"Hộ kinh doanh {code}",
            TaxCode = taxCode,
            Address = address,
            SalePriority = priority,
            IsDefaultForPurchase = isDefaultForPurchase,
            IsActive = true,
            RowVersion = new byte[8]
        };

    private static Warehouse NewWarehouse(
        int legalEntityId,
        string code,
        string name)
        => new()
        {
            StoreId = 1,
            LegalEntityId = legalEntityId,
            Code = code,
            Name = name,
            IsActive = true,
            AllowNegativeInventory = false,
            RowVersion = new byte[8]
        };

    private static InvoiceProviderSetting NewInvoiceSetting(
        string taxCode,
        string series)
        => new()
        {
            StoreId = 1,
            ProviderCode = "VIETTEL",
            BaseUrl = "https://example.test",
            Username = "test-user",
            Password = "protected-test-password",
            SupplierTaxCode = taxCode,
            InvoiceType = "1",
            TemplateCode = "1/001",
            InvoiceSeries = series,
            CurrencyCode = "VND",
            IsActive = true,
            RowVersion = new byte[8]
        };

    private static InMemoryAppDbContext CreateContext()
    {
        var tenant = new TenantContext();
        tenant.SetStore(1, "store-one");
        var context = new InMemoryAppDbContext(
            CreateOptions(),
            tenant,
            new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private sealed record SeededData(
        LegalEntity Primary,
        LegalEntity Second,
        Warehouse PrimaryWarehouse,
        Warehouse SecondWarehouse,
        InvoiceProviderSetting PrimaryInvoiceSetting,
        InvoiceProviderSetting SecondInvoiceSetting);

    private sealed class TestCredentialProtector : IInvoiceProviderCredentialProtector
    {
        public bool IsProtected(string value) => true;
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedOrLegacyPlaintext) => protectedOrLegacyPlaintext;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "phase-22-2-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class TestCurrentStore : ICurrentStore
    {
        public TestCurrentStore(int storeId) => StoreId = storeId;
        public int StoreId { get; }
    }
}
