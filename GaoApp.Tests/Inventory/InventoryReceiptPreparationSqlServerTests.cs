using System.Data.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
public sealed class InventoryReceiptPreparationSqlServerTests
{
    [Fact]
    public async Task Real_balance_batches_create_missing_rows_once_with_inverse_concurrent_input()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        int secondId;
        await using (var setup = database.CreateTenantContext(seed.StoreId))
        {
            var original = await setup.ProductVariants.SingleAsync();
            var variant = new ProductVariant { StoreId = seed.StoreId, ProductId = original.ProductId,
                Sku = "BALANCE-BATCH-SECOND", CostPrice = 10m, Price = 20m, IsActive = true };
            setup.ProductVariants.Add(variant); await setup.SaveChangesAsync(); secondId = variant.Id;
        }
        var keys = new[] { seed.ProductVariantId, secondId }.Select(id =>
            new GaoApp.Application.DTOs.Inventory.InventoryPostingLockKey(seed.StoreId, seed.WarehouseId, id)).ToArray();
        var arrivals = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Run(bool inverse)
        {
            await using var db = database.CreateTenantContext(seed.StoreId);
            await using var tx = await db.Database.BeginTransactionAsync();
            if (Interlocked.Increment(ref arrivals) == 2) start.SetResult();
            await start.Task;
            await Movement(db, new InventoryCostLayerAllocationRepository(db)).PreLockBalancesAsync(inverse ? keys.Reverse() : keys);
            await Task.Delay(50);
            await tx.CommitAsync();
        }
        await Task.WhenAll(Run(false), Run(true)).WaitAsync(TimeSpan.FromSeconds(20));
        await using var verify = database.CreateTenantContext(seed.StoreId);
        Assert.Equal(2, await verify.InventoryBalances.CountAsync());
        Assert.All(await verify.InventoryBalances.ToArrayAsync(), balance => Assert.Equal(0m, balance.OnHandQty));
    }

    [Fact]
    public async Task Prepared_receipts_batch_provisionals_reuse_locks_and_resolve_each_part_once()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync(allowNegativeInventory: true);
        var variants = await SeedNegatives(database, seed, 24);
        var reads = new Reads(); var saves = new Saves();
        await using var db = Context(database, seed.StoreId, reads, saves);
        var allocations = new InventoryCostLayerAllocationRepository(db);
        var movement = Movement(db, allocations);
        var factory = new InventoryMovementFactory();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var keys = variants.Select(id => new GaoApp.Application.DTOs.Inventory.InventoryPostingLockKey(
            seed.StoreId, seed.WarehouseId, id)).ToArray();
        await movement.PreLockBalancesAsync(keys);
        reads.Count = 0;
        await movement.PreparePurchaseReceiptAsync(keys.Concat(keys));
        foreach (var id in variants)
        {
            var row = Assert.Single(await allocations.GetOpenProvisionalAllocationsForUpdateAsync(seed.WarehouseId, id));
            Assert.Equal(4m, row.Quantity);
        }
        Assert.Equal(1, reads.Count);
        reads.Count = 0;
        var balances = new InventoryBalanceRepository(db);
        foreach (var id in variants) await balances.LockAndGetOrCreateAsync(seed.StoreId, seed.WarehouseId, id);
        Assert.Equal(0, reads.Count);
        saves.Count = 0;
        Assert.True(await movement.TryCreatePurchaseReceiptBatchAsync(variants.Select(id =>
            factory.CreatePurchaseReceipt(seed.WarehouseId, id, 1m, 12m, "RECEIPT-BATCH", id, "RECEIPT-BATCH", id)).ToArray()));
        Assert.Equal(4, saves.Count);
        var count = saves.Count;
        var retry = await movement.CreateAsync(factory.CreatePurchaseReceipt(seed.WarehouseId, variants[0], 1m, 12m,
            "RECEIPT-BATCH", variants[0], "RECEIPT-BATCH", variants[0]));
        Assert.True(retry.IsSkipped); Assert.Equal(count, saves.Count);
        await movement.CreateAsync(factory.CreatePurchaseReceipt(seed.WarehouseId, variants[0], 5m, 12m,
            "RECEIPT-BATCH", 999999, "RECEIPT-BATCH", 999999));
        Assert.Equal(8, saves.Count);
        await transaction.CommitAsync();

        await using var check = database.CreateTenantContext(seed.StoreId);
        var resolved = await check.InventoryCostLayerAllocations.AsNoTracking().ToArrayAsync();
        Assert.Equal(24, resolved.Length);
        Assert.Equal(4m, resolved.Single(x => x.IsResolved).ResolvedQuantity);
        Assert.All(resolved.Where(x => !x.IsResolved), row => Assert.Equal(1m, row.ResolvedQuantity));
        foreach (var balance in await check.InventoryBalances.AsNoTracking().ToArrayAsync())
        {
            Assert.Equal(balance.ProductVariantId == variants[0] ? 2m : -3m, balance.OnHandQty);
            Assert.Equal(balance.InventoryValue, await check.InventoryValuationEntries
                .Where(x => x.ProductVariantId == balance.ProductVariantId).SumAsync(x => x.Amount));
        }
        Assert.Equal(25, await check.InventoryValuationEntries.CountAsync(x =>
            x.EntryType == GaoApp.Domain.Enums.InventoryValuationEntryType.Revaluation));
    }

    [Fact]
    public async Task Receipt_batch_matches_serial_costing_for_partial_and_zero_difference_resolution_and_rolls_back()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync(allowNegativeInventory: true);
        await SeedNegatives(database, seed, 1);
        int secondWarehouse;
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var original = await db.Warehouses.SingleAsync();
            var warehouse = new Warehouse { StoreId = seed.StoreId, LegalEntityId = original.LegalEntityId,
                Code = "PARITY-WH", Name = "Batch parity", IsActive = true, AllowNegativeInventory = true };
            db.Warehouses.Add(warehouse); await db.SaveChangesAsync(); secondWarehouse = warehouse.Id;
            await Movement(db, new InventoryCostLayerAllocationRepository(db)).CreateAsync(new InventoryMovementFactory()
                .CreateSaleFinalize(secondWarehouse, seed.ProductVariantId, seed.ProductVariantId, 1, "Provisional test source", 4m, 10m));
        }
        var values = new[] { (Qty: .5m, Cost: 12m), (Qty: 1.25m, Cost: 10m), (Qty: 2.25m, Cost: 13m), (Qty: 2m, Cost: 11m) };
        async Task Post(int warehouse, bool batch, bool commit)
        {
            await using var db = database.CreateTenantContext(seed.StoreId);
            await using var tx = await db.Database.BeginTransactionAsync();
            var movement = Movement(db, new InventoryCostLayerAllocationRepository(db));
            var keys = new[] { new GaoApp.Application.DTOs.Inventory.InventoryPostingLockKey(seed.StoreId, warehouse, seed.ProductVariantId) };
            await movement.PreLockBalancesAsync(keys); await movement.PreparePurchaseReceiptAsync(keys);
            var requests = values.Select((x, i) => new InventoryMovementFactory().CreatePurchaseReceipt(
                warehouse, seed.ProductVariantId, x.Qty, x.Cost, "PARITY", i + 1, "PARITY", i + 1)).ToArray();
            if (batch) Assert.True(await movement.TryCreatePurchaseReceiptBatchAsync(requests));
            else foreach (var request in requests) await movement.CreateAsync(request);
            if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        }
        await Post(seed.WarehouseId, false, true);
        await Post(secondWarehouse, true, false);
        await using (var check = database.CreateTenantContext(seed.StoreId))
        {
            Assert.Equal(-4m, (await check.InventoryBalances.SingleAsync(x => x.WarehouseId == secondWarehouse)).OnHandQty);
            Assert.False(await check.InventoryTransactions.AnyAsync(x => x.WarehouseId == secondWarehouse && x.ReferenceId == "PARITY"));
            Assert.Equal(0m, (await check.InventoryCostLayerAllocations.Include(x => x.InventoryValuationEntry)
                .SingleAsync(x => x.InventoryValuationEntry.WarehouseId == secondWarehouse)).ResolvedQuantity);
        }
        await Post(secondWarehouse, true, true);
        async Task<string> Snapshot(int warehouse)
        {
            await using var db = database.CreateTenantContext(seed.StoreId);
            var balance = await db.InventoryBalances.Where(x => x.WarehouseId == warehouse)
                .Select(x => new { x.OnHandQty, x.InventoryValue, x.AverageUnitCost, x.LastInboundUnitCost }).SingleAsync();
            var transactions = await db.InventoryTransactions.Where(x => x.WarehouseId == warehouse && x.ReferenceId == "PARITY")
                .OrderBy(x => x.ReferenceLineId).Select(x => new { x.BeforeQty, x.AfterQty, x.TotalCost,
                    x.BeforeInventoryValue, x.AfterInventoryValue, x.RunningAverageUnitCostAfter }).ToArrayAsync();
            var entries = await db.InventoryValuationEntries.Where(x => x.WarehouseId == warehouse && x.InventoryTransaction.ReferenceId == "PARITY")
                .OrderBy(x => x.InventoryTransaction.ReferenceLineId).ThenBy(x => x.EntryType).ThenBy(x => x.Id)
                .Select(x => new { x.EntryType, x.Quantity, x.UnitCost, x.Amount, x.RunningQtyAfter, x.RunningValueAfter, x.RunningAverageUnitCostAfter }).ToArrayAsync();
            var layers = await db.InventoryCostLayers.Where(x => x.WarehouseId == warehouse).OrderBy(x => x.ReferenceLineId)
                .Select(x => new { x.OriginalQuantity, x.RemainingQuantity, x.ResolvedProvisionalQty, x.RemainingOpenProvisionalQty, x.UnitCost }).ToArrayAsync();
            var allocation = await db.InventoryCostLayerAllocations.Where(x => x.InventoryValuationEntry.WarehouseId == warehouse)
                .Select(x => new { x.Quantity, x.UnitCost, x.Amount, x.ResolvedQuantity, x.ResolvedAmount, x.IsResolved }).SingleAsync();
            return System.Text.Json.JsonSerializer.Serialize(new { balance, transactions, entries, layers, allocation });
        }
        Assert.Equal(await Snapshot(seed.WarehouseId), await Snapshot(secondWarehouse));
    }

    [Fact]
    public async Task Receipt_batch_saves_four_phases_and_preserves_running_snapshots_for_repeated_products()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var saves = new Saves();
        await using var db = Context(database, seed.StoreId, saves);
        var movement = Movement(db, new InventoryCostLayerAllocationRepository(db));
        var keys = new[] { new GaoApp.Application.DTOs.Inventory.InventoryPostingLockKey(seed.StoreId, seed.WarehouseId, seed.ProductVariantId) };
        var requests = Enumerable.Range(1, 8).Select(index => new InventoryMovementFactory().CreatePurchaseReceipt(
            seed.WarehouseId, seed.ProductVariantId, 1m, 12m, "BULK-RECEIPT", index, "BULK-RECEIPT", index)).ToArray();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await movement.PreLockBalancesAsync(keys);
            await movement.PreparePurchaseReceiptAsync(keys);
            saves.Count = 0;
            Assert.True(await movement.TryCreatePurchaseReceiptBatchAsync(requests));
            Assert.Equal(4, saves.Count);
            await tx.CommitAsync();
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await movement.PreLockBalancesAsync(keys);
            await movement.PreparePurchaseReceiptAsync(keys);
            saves.Count = 0;
            Assert.False(await movement.TryCreatePurchaseReceiptBatchAsync(requests));
            foreach (var request in requests) Assert.True((await movement.CreateAsync(request)).IsSkipped);
            Assert.Equal(0, saves.Count);
            await tx.CommitAsync();
        }
        await using var check = database.CreateTenantContext(seed.StoreId);
        var transactions = await check.InventoryTransactions.OrderBy(x => x.ReferenceLineId).ToArrayAsync();
        Assert.Equal(8, transactions.Length);
        Assert.Equal(Enumerable.Range(0, 8).Select(x => (decimal)x), transactions.Select(x => x.BeforeQty));
        Assert.Equal(Enumerable.Range(1, 8).Select(x => x * 12m), transactions.Select(x => x.AfterInventoryValue));
        var balance = await check.InventoryBalances.SingleAsync();
        Assert.Equal(8m, balance.OnHandQty); Assert.Equal(96m, balance.InventoryValue);
        var entries = await check.InventoryValuationEntries.ToDictionaryAsync(x => x.Id);
        var layers = await check.InventoryCostLayers.ToArrayAsync();
        Assert.Equal(8, layers.Length);
        Assert.All(layers, layer => Assert.Equal(layer.Id, entries[layer.InventoryValuationEntryId].InventoryCostLayerId));
    }

    [Fact]
    public async Task Prepared_snapshots_refresh_pretracked_evidence_and_expire_after_rollback()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync(allowNegativeInventory: true);
        await SeedNegatives(database, seed, 1);
        await using var db = database.CreateTenantContext(seed.StoreId);
        var stale = await db.InventoryCostLayerAllocations.Include(x => x.InventoryValuationEntry).SingleAsync();
        var balances = new InventoryBalanceRepository(db);
        await using var firstTransaction = await db.Database.BeginTransactionAsync();
        await using (var other = database.CreateTenantContext(seed.StoreId))
        {
            await using var writer = await other.Database.BeginTransactionAsync();
            await new InventoryBalanceRepository(other).LockAndGetOrCreateAsync(seed.StoreId, seed.WarehouseId, seed.ProductVariantId);
            var row = await other.InventoryCostLayerAllocations.SingleAsync();
            row.ResolvedQuantity = 1m; row.ResolvedAmount = 10m;
            await other.SaveChangesAsync();
            var waitingLock = balances.LockAndGetOrCreateAsync(seed.StoreId, seed.WarehouseId, seed.ProductVariantId);
            await Task.Delay(150);
            Assert.False(waitingLock.IsCompleted);
            await writer.CommitAsync();
            await waitingLock;
        }
        var repository = new InventoryCostLayerAllocationRepository(db);
        {
            await repository.PreparePurchaseReceiptAsync(seed.StoreId, seed.WarehouseId, [seed.ProductVariantId]);
            var row = Assert.Single(await repository.GetOpenProvisionalAllocationsForUpdateAsync(seed.WarehouseId, seed.ProductVariantId));
            Assert.Same(stale, row); Assert.Equal(1m, row.ResolvedQuantity);
            Assert.Equal(EntityState.Unchanged, db.Entry(row).State);
            row.ResolvedQuantity = 2m;
            var reread = Assert.Single(await new InventoryCostLayerAllocationRepository(db)
                .GetOpenProvisionalAllocationsForUpdateAsync(seed.WarehouseId, seed.ProductVariantId));
            Assert.Same(row, reread); Assert.Equal(2m, reread.ResolvedQuantity);
            await db.SaveChangesAsync();
            await firstTransaction.RollbackAsync();
        }
        await firstTransaction.DisposeAsync();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await balances.LockAndGetOrCreateAsync(seed.StoreId, seed.WarehouseId, seed.ProductVariantId);
            await repository.PreparePurchaseReceiptAsync(seed.StoreId, seed.WarehouseId, [seed.ProductVariantId]);
            Assert.Equal(1m, Assert.Single(await repository.GetOpenProvisionalAllocationsForUpdateAsync(
                seed.WarehouseId, seed.ProductVariantId)).ResolvedQuantity);
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Receipt_preparation_rejects_unlocked_foreign_and_dirty_evidence()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync(allowNegativeInventory: true);
        await SeedNegatives(database, seed, 1);
        await using var db = database.CreateTenantContext(seed.StoreId);
        var row = await db.InventoryCostLayerAllocations.SingleAsync();
        var repository = new InventoryCostLayerAllocationRepository(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PreparePurchaseReceiptAsync(
            seed.StoreId, seed.WarehouseId, [seed.ProductVariantId]));
        await using var tx = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PreparePurchaseReceiptAsync(
            seed.StoreId, seed.WarehouseId, [seed.ProductVariantId]));
        await new InventoryBalanceRepository(db).LockAndGetOrCreateAsync(seed.StoreId, seed.WarehouseId, seed.ProductVariantId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PreparePurchaseReceiptAsync(
            seed.StoreId + 100, seed.WarehouseId, [seed.ProductVariantId]));
        row.ResolvedQuantity = 2m;
        await Assert.ThrowsAsync<BusinessRuleException>(() => repository.PreparePurchaseReceiptAsync(
            seed.StoreId, seed.WarehouseId, [seed.ProductVariantId]));
        Assert.Equal(2m, row.ResolvedQuantity);
        await tx.RollbackAsync();
        await using var host = database.CreateHostContext();
        await using var hostTransaction = await host.Database.BeginTransactionAsync();
        await new InventoryBalanceRepository(host).LockAndGetOrCreateAsync(seed.StoreId, seed.WarehouseId, seed.ProductVariantId);
        var hostRepository = new InventoryCostLayerAllocationRepository(host);
        await hostRepository.PreparePurchaseReceiptAsync(seed.StoreId, seed.WarehouseId, [seed.ProductVariantId]);
        Assert.Single(await hostRepository.GetOpenProvisionalAllocationsForUpdateAsync(seed.WarehouseId, seed.ProductVariantId));
        await hostTransaction.RollbackAsync();
    }

    private static async Task<int[]> SeedNegatives(InventoryPostingLocalDb database, InventoryPostingSeed seed, int count)
    {
        await using var db = database.CreateTenantContext(seed.StoreId);
        var original = await db.ProductVariants.SingleAsync();
        for (var i = 1; i < count; i++) db.ProductVariants.Add(new ProductVariant
        { StoreId = seed.StoreId, ProductId = original.ProductId, Sku = $"RECEIPT-PERF-{i}", CostPrice = 10m, Price = 20m, IsActive = true });
        await db.SaveChangesAsync();
        var ids = await db.ProductVariants.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var movement = Movement(db, new InventoryCostLayerAllocationRepository(db));
        foreach (var id in ids)
            await movement.CreateAsync(new InventoryMovementFactory().CreateSaleFinalize(
                seed.WarehouseId, id, id, 1, "Provisional test source", 4m, 10m));
        return ids;
    }

    private static AppDbContext Context(InventoryPostingLocalDb database, int storeId, params IInterceptor[] interceptors)
    {
        var tenant = new GaoApp.Infrastructure.Tenant.TenantContext(); tenant.SetStore(storeId, "receipt-preparation-test");
        return new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString)
            .AddInterceptors(interceptors).Options, tenant, new Actor());
    }
    private static InventoryMovementService Movement(AppDbContext db, InventoryCostLayerAllocationRepository allocations)
        => new(new InventoryBalanceRepository(db), new InventoryTransactionRepository(db), new InventoryValuationEntryRepository(db),
            new InventoryCostLayerRepository(db), allocations, new WarehouseRepository(db), new InventoryPostingTransactionCoordinator(db),
            new InventoryReservationRepository(db), new InventorySourceCostEvidenceRepository(db));
    private sealed class Actor : GaoApp.Application.Common.Interfaces.ICurrentUser
    {
        public int? UserId => null; public string? UserName => null; public int? TerminalId => null;
        public string? TerminalCode => null; public bool IsAuthenticated => false;
    }
    private sealed class Reads : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default) { Count++; return ValueTask.FromResult(result); }
    }
    private sealed class Saves : SaveChangesInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default) { Count++; return ValueTask.FromResult(result); }
    }
}
