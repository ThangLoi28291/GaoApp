using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

[Collection("R1FinalDatabasePreflight")]
public sealed class InvoiceInputStockSqlTests
{
    [Fact]
    public async Task Sql_projection_reads_converted_xml_without_physical_posting_and_rejects_other_store()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(seed.StoreId);
        var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == seed.ProductVariantId);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId);
        var receipt = new StockDocument { StoreId = seed.StoreId, DocumentNo = "XML-RECEIPT-1", WarehouseId = warehouse.Id,
            ConfirmedLegalEntityId = warehouse.LegalEntityId, Status = StockDocumentStatus.Confirmed,
            Type = StockDocumentType.Receipt, ConfirmedAtUtc = DateTime.UtcNow };
        var xml = new InputInvoiceHead { StoreId = seed.StoreId, InvoiceNumber = "XML-0001", ResolvedBuyerLegalEntityId = warehouse.LegalEntityId };
        db.AddRange(receipt, xml); await db.SaveChangesAsync();
        var detail = new InputInvoiceDetail { InputInvoiceHeadId = xml.Id, ItemName = "Sữa lốc", Quantity = 15, UnitName = "lốc" };
        var line = new StockDocumentLine { StockDocumentId = receipt.Id, ProductVariantId = variant.Id, LineNo = 1,
            Quantity = 100, BaseQuantity = 100, Factor = 1, ProductNameSnapshot = "Sữa" };
        var map = new StockDocumentInputInvoiceMap { StoreId = seed.StoreId, StockDocumentId = receipt.Id, InputInvoiceHeadId = xml.Id };
        db.AddRange(detail, line, map); await db.SaveChangesAsync();
        var reconciliation = new StockDocumentInputInvoiceReconciliation { StoreId = seed.StoreId,
            StockDocumentId = receipt.Id, InputInvoiceHeadId = xml.Id, StockDocumentInputInvoiceMapId = map.Id };
        db.Add(reconciliation); await db.SaveChangesAsync();
        db.Add(new StockDocumentLineInputInvoiceMap { StoreId = seed.StoreId, StockDocumentId = receipt.Id,
            StockDocumentLineId = line.Id, InputInvoiceDetailId = detail.Id, UseInputInvoice = true });
        db.Add(new StockDocumentInputInvoiceDetailReconciliation { StoreId = seed.StoreId, StockDocumentId = receipt.Id,
            InputInvoiceHeadId = xml.Id, InputInvoiceDetailId = detail.Id, StockDocumentInputInvoiceReconciliationId = reconciliation.Id,
            ProductVariantId = variant.Id, ConfirmedFactor = 4, ConfirmedBaseUnitId = variant.Product.BaseUnitId,
            XmlQuantity = 15, DerivedBaseQuantity = 60, ReceiptBaseQuantity = 100 });
        await db.SaveChangesAsync();
        var repository = new InvoiceInputStockReadRepository(db);
        var result = await repository.GetMovementsAsync(seed.StoreId);
        Assert.Equal(60, Assert.Single(result).Change);
        Assert.Empty(await repository.GetMovementsAsync(seed.StoreId + 10000));
        Assert.Empty(await db.InventoryTransactions.ToListAsync());
    }
    [Fact]
    public async Task Sql_supplemental_projection_is_durable_unique_and_has_no_physical_stock_side_effect()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();

        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.ProductVariantId);
            variant.HasInputInvoice = false;
            var beforeTransactions = await db.InventoryTransactions.CountAsync();
            var beforeBalances = await db.InventoryBalances.CountAsync();
            db.InvoiceInputStockSupplementalMovements.Add(new InvoiceInputStockSupplementalMovement
            {
                StoreId = seed.StoreId,
                WarehouseId = seed.WarehouseId,
                ProductVariantId = seed.ProductVariantId,
                EffectiveAtUtc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                QuantityChange = 75m,
                MovementType = InvoiceInputStockSupplementalMovementType.LegacyOpening,
                LegacySourceKey = "SQL-C2-OPEN-001",
                SourcePeriod = "2025-06",
                Note = "synthetic"
            });
            await db.SaveChangesAsync();

            var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(seed.StoreId);
            Assert.Contains(rows, x => x.SourceCode == "SQL-C2-OPEN-001" && x.Change == 75m);
            Assert.Equal(beforeTransactions, await db.InventoryTransactions.CountAsync());
            Assert.Equal(beforeBalances, await db.InventoryBalances.CountAsync());
        }

        await using (var first = database.CreateTenantContext(seed.StoreId))
        {
            first.InvoiceInputStockSupplementalMovements.Add(new InvoiceInputStockSupplementalMovement
            {
                StoreId = seed.StoreId,
                WarehouseId = seed.WarehouseId,
                ProductVariantId = seed.ProductVariantId,
                EffectiveAtUtc = new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc),
                QuantityChange = 1m,
                MovementType = InvoiceInputStockSupplementalMovementType.LegacyInbound,
                LegacySourceKey = "SQL-C2-DUPLICATE"
            });
            await first.SaveChangesAsync();
        }

        await using (var duplicate = database.CreateTenantContext(seed.StoreId))
        {
            duplicate.InvoiceInputStockSupplementalMovements.Add(new InvoiceInputStockSupplementalMovement
            {
                StoreId = seed.StoreId,
                WarehouseId = seed.WarehouseId,
                ProductVariantId = seed.ProductVariantId,
                EffectiveAtUtc = new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc),
                QuantityChange = 2m,
                MovementType = InvoiceInputStockSupplementalMovementType.LegacyInbound,
                LegacySourceKey = "SQL-C2-DUPLICATE"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        await database.MigrateAsync("20260920093000_OptimizeInventoryLedgerTimeline");
        await using var afterDown = database.CreateHostContext();
        var tableCount = await afterDown.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'InvoiceInputStockSupplementalMovements'")
            .SingleAsync();
        Assert.Equal(0, tableCount);
    }

}
