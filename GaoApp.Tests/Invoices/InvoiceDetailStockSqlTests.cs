using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

[Collection("R1FinalDatabasePreflight")]
public sealed class InvoiceDetailStockSqlTests
{
    [Fact]
    public async Task Detail_stock_uses_documentary_stock_other_reservations_and_current_invoice_units_in_SQL()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(seed.StoreId);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId);
        var legal = await db.LegalEntities.SingleAsync(x => x.Id == warehouse.LegalEntityId);
        legal.DefaultWarehouseId = warehouse.Id;
        var current = new InvoiceHead { StoreId = seed.StoreId, LegalEntityId = legal.Id,
            IsAutoInvoiceGroup = true, ProviderStatus = InvoiceProviderStatus.Issuing };
        var issued = new InvoiceHead { StoreId = seed.StoreId, LegalEntityId = legal.Id,
            IsAutoInvoiceGroup = true, ProviderStatus = InvoiceProviderStatus.Issued, ProviderInvoiceNo = "STOCK-ISSUED" };
        var held = new InvoiceHead { StoreId = seed.StoreId, LegalEntityId = legal.Id,
            IsAutoInvoiceGroup = true, ProviderStatus = InvoiceProviderStatus.Issuing };
        db.AddRange(current, issued, held);
        await db.SaveChangesAsync();
        var pack = new InvoiceDetail { StoreId = seed.StoreId, InvoiceHeadId = current.Id,
            ProductVariantId = seed.ProductVariantId, ItemName = "Sữa lốc", UnitName = "lốc", Quantity = 3, LegacyUnitFactor = 4 };
        var carton = new InvoiceDetail { StoreId = seed.StoreId, InvoiceHeadId = current.Id,
            SourceType = InvoiceDetailSourceType.Manual, ProductVariantId = seed.ProductVariantId,
            ItemName = "Sữa thùng", UnitName = "thùng", Quantity = 1, LegacyUnitFactor = 6 };
        db.AddRange(pack, carton,
            new InvoiceDetail { StoreId = seed.StoreId, InvoiceHeadId = issued.Id,
                ProductVariantId = seed.ProductVariantId, ItemName = "Đã xuất", Quantity = 2 },
            new InvoiceDetail { StoreId = seed.StoreId, InvoiceHeadId = held.Id,
                ProductVariantId = seed.ProductVariantId, ItemName = "Đơn khác đang giữ", Quantity = 3 },
            new InvoiceInputStockSupplementalMovement { StoreId = seed.StoreId, WarehouseId = warehouse.Id,
                ProductVariantId = seed.ProductVariantId, QuantityChange = 20,
                MovementType = InvoiceInputStockSupplementalMovementType.LegacyOpening,
                EffectiveAtUtc = DateTime.UtcNow, LegacySourceKey = "DETAIL-STOCK-SQL" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new InvoiceInputStockRepository(db).GetAvailabilityAsync(current.Id);

        var aggregate = Assert.Single(result.Lines);
        Assert.Equal(18m, aggregate.RequiredBaseQuantity);
        Assert.Equal(15m, aggregate.AvailableBaseQuantity);
        Assert.Equal(3m, aggregate.ShortageBaseQuantity);
        var packStock = Assert.Single(result.DetailLines, x => x.InvoiceDetailId == pack.Id);
        Assert.Equal(3.75m, packStock.AvailableQuantity);
        Assert.Equal(4.5m, packStock.RequiredQuantity);
        Assert.Equal(0.75m, packStock.ShortageQuantity);
        Assert.Equal(2, packStock.SharedLineCount);
        Assert.Equal(warehouse.Name, packStock.WarehouseName);
        var cartonStock = Assert.Single(result.DetailLines, x => x.InvoiceDetailId == carton.Id);
        Assert.Equal(2.5m, cartonStock.AvailableQuantity);
        Assert.Equal(0.5m, cartonStock.ShortageQuantity);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.InventoryTransactions.ToListAsync());
        Assert.Empty(await db.InventoryBalances.ToListAsync());

        await using var otherStore = database.CreateTenantContext(seed.StoreId + 10000);
        var hidden = await new InvoiceInputStockRepository(otherStore).GetAvailabilityAsync(current.Id);
        Assert.Empty(hidden.Lines);
        Assert.Empty(hidden.DetailLines);
    }
}
