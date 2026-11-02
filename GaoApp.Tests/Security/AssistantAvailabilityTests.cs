using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Assistant;
using Xunit;

namespace GaoApp.Tests.Security;

public sealed partial class AssistantIntegrationTests
{
    [Fact]
    public async Task Availability_is_store_scoped_and_omits_internal_or_unrecorded_quantities()
    {
        using var f = new Fixture();
        await f.Seed();

        f.Db.Warehouses.AddRange(
            new Warehouse { Id = 100, StoreId = 1, LegalEntityId = 1, Code = "SALE", Name = "Quầy bán", IsActive = true },
            new Warehouse { Id = 101, StoreId = 1, LegalEntityId = 1, Code = "CLOSED", Name = "Kho ngừng", IsActive = false },
            new Warehouse { Id = 102, StoreId = 1, LegalEntityId = 1, Code = "NO-BALANCE", Name = "Kho chưa ghi tồn", IsActive = true },
            new Warehouse { Id = 103, StoreId = 1, LegalEntityId = 1, Code = "NEGATIVE", Name = "Kho cần rà soát", IsActive = true },
            new Warehouse { Id = 200, StoreId = 2, LegalEntityId = 2, Code = "OTHER", Name = "Cửa hàng khác", IsActive = true });
        f.Db.POSTerminals.AddRange(
            new POSTerminal { Id = 10, StoreId = 1, Code = "POS1", Name = "Quầy 1", IsActive = true },
            new POSTerminal { Id = 11, StoreId = 1, Code = "POS3", Name = "Quầy 3", IsActive = true },
            new POSTerminal { Id = 20, StoreId = 2, Code = "POS2", Name = "Quầy khác", IsActive = true });
        f.Db.POSShifts.AddRange(
            new POSShift { Id = 10, StoreId = 1, TerminalId = 10, WarehouseId = 100, OpenedByUserId = 1 },
            new POSShift { Id = 11, StoreId = 1, TerminalId = 10, WarehouseId = 101, OpenedByUserId = 1 },
            new POSShift { Id = 12, StoreId = 1, TerminalId = 11, WarehouseId = 102, OpenedByUserId = 1 },
            new POSShift { Id = 13, StoreId = 1, TerminalId = 11, WarehouseId = 103, OpenedByUserId = 1 },
            new POSShift { Id = 20, StoreId = 2, TerminalId = 20, WarehouseId = 200, OpenedByUserId = 1 });
        f.Db.InventoryBalances.AddRange(
            new InventoryBalance { Id = 10, StoreId = 1, WarehouseId = 100, ProductVariantId = 10, OnHandQty = 10, ReservedQty = 2 },
            new InventoryBalance { Id = 11, StoreId = 1, WarehouseId = 103, ProductVariantId = 10, OnHandQty = 1, ReservedQty = 2 },
            new InventoryBalance { Id = 20, StoreId = 2, WarehouseId = 200, ProductVariantId = 20, OnHandQty = 999, ReservedQty = 0 });
        foreach (var entry in f.Db.ChangeTracker.Entries<GaoApp.Domain.Common.BaseEntity>())
            entry.Entity.RowVersion = [1];
        f.SetStoreScope(null);
        await f.Db.SaveChangesAsync();
        f.SetStoreScope(1);

        var identity = await f.Access.CurrentAsync(null, default);
        var variant = AssistantCatalogService.Build(Variant(10, 1), true);
        var service = new AssistantAvailabilityService(f.Db, f.Clock);
        var result = await service.ReadAsync(identity, variant, default);

        Assert.Equal(3, result.Warehouses.Length);
        var warehouse = Assert.Single(result.Warehouses, item => item.WarehouseId == 100);
        Assert.Equal(100, warehouse.WarehouseId);
        Assert.Equal("available", warehouse.Status);
        Assert.Equal(8m, warehouse.AvailableQuantity);
        var baseUnit = Assert.Single(warehouse.Units, unit => unit.Factor == 1m);
        Assert.Equal(8m, baseUnit.AvailableQuantity);
        var missing = Assert.Single(result.Warehouses, item => item.WarehouseId == 102);
        Assert.Equal("not_recorded", missing.Status);
        Assert.Null(missing.AvailableQuantity);
        var negative = Assert.Single(result.Warehouses, item => item.WarehouseId == 103);
        Assert.Equal("review_required", negative.Status);
        Assert.Null(negative.AvailableQuantity);
        Assert.DoesNotContain(result.Warehouses, item => item.WarehouseName is "Kho ngừng" or "Cửa hàng khác");
        Assert.Equal(f.Clock.Now, result.VerifiedAt);
    }
}
