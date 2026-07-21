using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory.Warehouse;
using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.LegalEntities;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.LegalEntities;

public sealed class LegalEntityFoundationTests
{
    [Fact]
    public async Task Global_filter_should_isolate_legal_entities_by_store()
    {
        var options = CreateOptions();

        await using (var hostContext = CreateContext(
                         options,
                         tenant => tenant.SetHostAdmin()))
        {
            hostContext.LegalEntities.AddRange(
                NewLegalEntity(1, "HKD-1", 1, isDefaultForPurchase: true),
                NewLegalEntity(2, "HKD-2", 1, isDefaultForPurchase: true));

            await hostContext.SaveChangesAsync();
        }

        await using var storeContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var visibleCodes = await storeContext.LegalEntities
            .Select(x => x.Code)
            .ToListAsync();

        visibleCodes.Should().Equal("HKD-1");
    }

    [Fact]
    public async Task Tenant_guard_should_reject_legal_entity_for_another_store()
    {
        var options = CreateOptions();
        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        context.LegalEntities.Add(
            NewLegalEntity(2, "WRONG-STORE", 1, isDefaultForPurchase: true));

        var action = () => context.SaveChangesAsync();

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*không khớp TenantContext=1*");
    }

    [Fact]
    public async Task Warehouse_service_should_use_default_purchase_legal_entity_for_legacy_request()
    {
        var options = CreateOptions();
        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var primary = NewLegalEntity(
            1,
            "PRIMARY",
            1,
            isDefaultForPurchase: true);

        context.LegalEntities.Add(primary);
        await context.SaveChangesAsync();

        var warehouseRepository = new WarehouseRepository(context);
        var legalEntityRepository = new LegalEntityRepository(context);
        var service = new WarehouseService(
            warehouseRepository,
            legalEntityRepository);

        var created = await service.CreateAsync(new CreateWarehouseRequest
        {
            Name = "Kho tương thích UI cũ",
            IsDefault = true,
            AllowNegativeInventory = false
        });

        created.LegalEntityId.Should().Be(primary.Id);

        var persisted = await context.Warehouses.SingleAsync();
        persisted.StoreId.Should().Be(1);
        persisted.LegalEntityId.Should().Be(primary.Id);
    }

    [Fact]
    public async Task Legal_entity_service_should_create_second_entity_and_reject_duplicate_priority()
    {
        var options = CreateOptions();
        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        context.LegalEntities.Add(
            NewLegalEntity(1, "PRIMARY", 1, isDefaultForPurchase: true));
        await context.SaveChangesAsync();

        var legalEntityRepository = new LegalEntityRepository(context);
        var warehouseRepository = new WarehouseRepository(context);
        var service = new LegalEntityService(
            legalEntityRepository,
            warehouseRepository,
            invoiceSettings: null!,
            currentStore: new TestCurrentStore(1));

        var createResult = await service.CreateAsync(new CreateLegalEntityRequest
        {
            Code = "HKD-2",
            Name = "HKD 2",
            LegalName = "Hộ kinh doanh 2",
            SalePriority = 2,
            IsDefaultForPurchase = false
        });

        createResult.IsSuccess.Should().BeTrue();

        var duplicatePriority = await service.CreateAsync(new CreateLegalEntityRequest
        {
            Code = "HKD-3",
            Name = "HKD 3",
            LegalName = "Hộ kinh doanh 3",
            SalePriority = 2,
            IsDefaultForPurchase = false
        });

        duplicatePriority.IsSuccess.Should().BeFalse();
        duplicatePriority.Error?.Code.Should().Be("Conflict");
    }

    [Fact]
    public async Task Legal_entity_service_should_only_accept_an_owned_active_default_warehouse()
    {
        var options = CreateOptions();
        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var first = NewLegalEntity(1, "HKD-1", 1, isDefaultForPurchase: true);
        var second = NewLegalEntity(1, "HKD-2", 2, isDefaultForPurchase: false);
        context.LegalEntities.AddRange(first, second);
        await context.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            StoreId = 1,
            LegalEntityId = second.Id,
            Code = "KHO-HKD-2",
            Name = "Kho bán HKD 2",
            IsActive = true,
            RowVersion = new byte[8]
        };

        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        var legalEntityRepository = new LegalEntityRepository(context);
        var warehouseRepository = new WarehouseRepository(context);
        var service = new LegalEntityService(
            legalEntityRepository,
            warehouseRepository,
            invoiceSettings: null!,
            currentStore: new TestCurrentStore(1));

        var ownershipMismatch = await service.SetDefaultWarehouseAsync(
            first.Id,
            warehouse.Id);

        ownershipMismatch.IsSuccess.Should().BeFalse();
        ownershipMismatch.Error?.Code.Should().Be(
            "LegalEntity.WarehouseOwnershipMismatch");

        var success = await service.SetDefaultWarehouseAsync(
            second.Id,
            warehouse.Id);

        success.IsSuccess.Should().BeTrue();
        second.DefaultWarehouseId.Should().Be(warehouse.Id);
    }

    [Fact]
    public void Model_should_use_composite_store_scoped_warehouse_ownership_foreign_key()
    {
        var options = CreateOptions();
        using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var warehouseType = context.Model.FindEntityType(typeof(Warehouse));
        warehouseType.Should().NotBeNull();

        var ownership = warehouseType!.GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == typeof(LegalEntity));

        ownership.Properties.Select(x => x.Name)
            .Should().Equal(
                nameof(BaseStoreEntity.StoreId),
                nameof(Warehouse.LegalEntityId));

        ownership.PrincipalKey.Properties.Select(x => x.Name)
            .Should().Equal(
                nameof(BaseStoreEntity.StoreId),
                nameof(BaseEntity.Id));
    }

    [Fact]
    public void Multi_legal_entity_feature_should_be_disabled_by_default()
    {
        var store = new Store();

        store.IsMultiLegalEntityEnabled.Should().BeFalse();
        store.MultiLegalEntityActivatedAtUtc.Should().BeNull();
    }

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static InMemoryAppDbContext CreateContext(
        DbContextOptions<InMemoryAppDbContext> options,
        Action<TenantContext> configureTenant)
    {
        var tenant = new TenantContext();
        configureTenant(tenant);

        var context = new InMemoryAppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        context.VerifyRowVersionConfiguration();
        return context;
    }

    private static LegalEntity NewLegalEntity(
        int storeId,
        string code,
        int salePriority,
        bool isDefaultForPurchase)
        => new()
        {
            StoreId = storeId,
            Code = code,
            Name = code,
            LegalName = code,
            SalePriority = salePriority,
            IsDefaultForPurchase = isDefaultForPurchase,
            IsActive = true,
            RowVersion = new byte[8]
        };

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "phase-22-1-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class TestCurrentStore : ICurrentStore
    {
        public TestCurrentStore(int storeId)
        {
            StoreId = storeId;
        }

        public int StoreId { get; }
    }
}
