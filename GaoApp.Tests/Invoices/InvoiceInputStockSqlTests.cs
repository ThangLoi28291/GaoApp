using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Invoices;
using System.Diagnostics;

namespace GaoApp.Tests.Invoices;

[Collection("R1FinalDatabasePreflight")]
public sealed class InvoiceInputStockSqlTests
{
    [Fact]
    public async Task Large_legacy_import_is_consumed_once_and_both_tabs_reuse_the_snapshot()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(seed.StoreId);
        db.Database.SetCommandTimeout(120);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId);
        var legal = await db.LegalEntities.SingleAsync(x => x.Id == warehouse.LegalEntityId);
        legal.DefaultWarehouseId = warehouse.Id;
        var head = new InvoiceHead { StoreId = seed.StoreId, LegacySourceId = 1, LegacyReadOnly = true,
            LegalEntityId = legal.Id, ProviderStatus = InvoiceProviderStatus.Issued, ProviderInvoiceNo = "BENCH-1" };
        db.Add(head); await db.SaveChangesAsync();
        var detail = new InvoiceDetail { StoreId = seed.StoreId, InvoiceHeadId = head.Id, LegacySourceId = 1,
            ProductVariantId = seed.ProductVariantId, ItemName = "Sữa tươi", Quantity = 1, LegacyUnitFactor = 1 };
        var outbound = new InvoiceInputStockSupplementalMovement { StoreId = seed.StoreId, WarehouseId = seed.WarehouseId,
            ProductVariantId = seed.ProductVariantId, EffectiveAtUtc = DateTime.UtcNow, QuantityChange = -1,
            MovementType = InvoiceInputStockSupplementalMovementType.LegacyOutbound, LegacySourceKey = "GSTORE-IIS-V1|X|1", LegacyOrderId = 1,
            Note = "Ghi chú lịch sử không cần tải toàn bộ để tính tồn hoặc tìm kiếm.".PadRight(140, 'x') };
        db.AddRange(detail, outbound); await db.SaveChangesAsync();
        // Clone trusted EF model columns into the disposable test database.
        var detailColumns = Columns<InvoiceDetail>(db);
        var detailSelect = detailColumns.Select(c => c == "LegacySourceId" ? "n.i+1" : $"s.[{c}]");
        var detailInsertSql = $$"""
            WITH n AS (SELECT TOP (219999) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO InvoiceDetails ({{string.Join(',', detailColumns.Select(c => $"[{c}]"))}})
            SELECT {{string.Join(',', detailSelect)}} FROM n CROSS JOIN InvoiceDetails s WHERE s.Id={0};
            """;
        await db.Database.ExecuteSqlRawAsync(detailInsertSql, detail.Id);
        var movementColumns = Columns<InvoiceInputStockSupplementalMovement>(db);
        var movementSelect = movementColumns.Select(c => c == "LegacySourceKey" ? "CONCAT(N'GSTORE-IIS-V1|X|', n.i+1)" : $"s.[{c}]");
        var movementInsertSql = $$"""
            WITH n AS (SELECT TOP (219999) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO InvoiceInputStockSupplementalMovements ({{string.Join(',', movementColumns.Select(c => $"[{c}]"))}})
            SELECT {{string.Join(',', movementSelect)}} FROM n CROSS JOIN InvoiceInputStockSupplementalMovements s WHERE s.Id={0};
            """;
        await db.Database.ExecuteSqlRawAsync(movementInsertSql, outbound.Id);
        db.Add(new InvoiceInputStockSupplementalMovement { StoreId = seed.StoreId, WarehouseId = seed.WarehouseId,
            ProductVariantId = seed.ProductVariantId, EffectiveAtUtc = DateTime.UtcNow.AddDays(-1), QuantityChange = 500000,
            MovementType = InvoiceInputStockSupplementalMovementType.LegacyOpening, LegacySourceKey = "BENCH-OPEN" });
        db.Add(new InvoiceDetail { StoreId = seed.StoreId, InvoiceHeadId = head.Id, ProductVariantId = seed.ProductVariantId,
            ItemName = "Sữa tươi", Quantity = 4 });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new InvoiceInputStockReadService(new InvoiceInputStockReadRepository(db, cache), new Store(seed.StoreId));
        var timer = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var balance = await service.GetPageAsync(new(), timeout.Token);
        Assert.Equal(279996, Assert.Single(balance.Balances).Available);
        Assert.Equal(220001, balance.DecreaseCount);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"Cold stock page took {timer.Elapsed}.");
        timer.Restart();
        var ledger = await service.GetPageAsync(new() { View = "ledger", Page = 2 });
        Assert.Equal(220002, ledger.TotalItems); Assert.Equal(20, ledger.Movements.Count);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"Warm ledger took {timer.Elapsed}.");
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.InventoryTransactions.ToListAsync());
    }

    private sealed class Store(int storeId) : ICurrentStore { public int StoreId => storeId; }
    private static string[] Columns<TEntity>(GaoApp.Infrastructure.Data.AppDbContext db) => db.Model.FindEntityType(typeof(TEntity))!
        .GetProperties().Where(p => p.Name != "Id" && !p.IsConcurrencyToken && p.GetComputedColumnSql() == null)
        .Select(p => p.GetColumnName()).ToArray();

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

    [Fact]
    public async Task Paged_projection_keeps_search_totals_notes_and_legacy_links_without_loading_all_notes()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        await using var db = database.CreateTenantContext(seed.StoreId);
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.ProductVariantId);
        variant.ProductVariantName = "Sữa tươi 220ml";
        variant.Sku = "ST-220";
        var legacy = new InvoiceHead { StoreId = seed.StoreId, LegacySourceId = 999,
            LegacyReadOnly = true, ProviderStatus = InvoiceProviderStatus.Issued, ProviderInvoiceNo = "TEST-999" };
        db.Add(legacy);
        db.Add(new InvoiceInputStockSupplementalMovement { StoreId = seed.StoreId,
            WarehouseId = seed.WarehouseId, ProductVariantId = seed.ProductVariantId,
            QuantityChange = 10000, MovementType = InvoiceInputStockSupplementalMovementType.LegacyOpening,
            LegacySourceKey = "PAGE-OPEN", EffectiveAtUtc = DateTime.UtcNow.AddDays(-1) });
        db.AddRange(Enumerable.Range(1, 1000).Select(i => new InvoiceInputStockSupplementalMovement
        {
            StoreId = seed.StoreId, WarehouseId = seed.WarehouseId, ProductVariantId = seed.ProductVariantId,
            QuantityChange = -1, MovementType = InvoiceInputStockSupplementalMovementType.LegacyOutbound,
            LegacySourceKey = "GSTORE-IIS-V1|X|" + i, LegacyOrderId = 999,
            LegacyInvoiceNumber = "8120", LegacyInvoiceSymbol = "C26TVK", SourcePeriod = "2026-10",
            Note = $"Ghi chú dòng {i}: " + new string('x', 900), EffectiveAtUtc = DateTime.UtcNow.AddMinutes(i)
        }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new InvoiceInputStockReadRepository(db, cache);
        var source = await repository.GetPageMovementsAsync(seed.StoreId, null, null);
        Assert.All(source.Where(x => x.LegacyOrderId != null), row =>
        {
            Assert.DoesNotContain("Ghi chú dòng", row.Note);
            Assert.Null(row.InvoiceHeadId);
        });
        var service = new InvoiceInputStockReadService(repository, new Store(seed.StoreId));
        var balance = await service.GetPageAsync(new() { Keyword = "sua tuoi" });
        Assert.Equal(9000m, Assert.Single(balance.Balances).Available);
        Assert.Equal(1000, balance.DecreaseCount);
        var ledger = await service.GetPageAsync(new() { View = "ledger", Keyword = "HĐ 8120", Page = 2 });
        Assert.Equal(1000, ledger.TotalItems);
        Assert.Equal(20, ledger.Movements.Count);
        Assert.All(ledger.Movements, row =>
        {
            Assert.Contains("Ghi chú dòng", row.Note);
            Assert.Contains("kỳ 2026-10", row.Note);
            Assert.Equal(legacy.Id, row.InvoiceHeadId);
        });
        Assert.All(source.Where(x => x.LegacyOrderId != null), row => Assert.DoesNotContain("Ghi chú dòng", row.Note));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.InventoryTransactions.ToListAsync());
    }
}
