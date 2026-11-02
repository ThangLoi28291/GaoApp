using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb04"), Trait("Category", "StorefrontWeb04")]
public sealed class Web04InventoryRaceTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("price")][InlineData("factor")][InlineData("active")][InlineData("promotionActivation")][InlineData("promotionInsert")]
    public async Task Blocked_actual_balance_lock_does_not_allow_admin_committed_price_source_or_promotion_change_to_be_accepted_stale(string change)
    {
        var c = await fixture.Web04Async(); var request = Web04Case.PlaceRequest(await c.QuoteAsync()); int promotion = 0;
        if (change == "promotionActivation")
        {
            var p = await Web04PromotionTests.Promotion(c, new() { Name = "Initially inactive", Type = PromotionType.ProductDiscount, DiscountType = PromotionDiscountType.Percentage,
                DiscountValue = 20, Items = [new() { ProductId = c.Seed.ProductId }] }); promotion = p.Id;
            await using var inactive = c.Context(); await inactive.Promotions.Where(x => x.Id == promotion).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false));
            request = Web04Case.PlaceRequest(await c.QuoteAsync());
        }
        await using var blocker = c.Context(); await using var blockerTx = await blocker.Database.BeginTransactionAsync();
        await new InventoryBalanceRepository(blocker).LockBatchAsync([new(c.StoreId, c.Seed.WarehouseId, c.Seed.VariantId)]);
        await using var placing = c.Context(); var gate = new ObserveBalance(new InventoryBalanceRepository(placing));
        var placeTask = c.Service(placing, balances: gate).PlaceAsync(request); await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var adminTask = Change(); var adminCommittedBeforeRelease = await Task.WhenAny(adminTask, Task.Delay(350)) == adminTask;
        await blockerTx.CommitAsync(); StorefrontPlacementResult? result = null; Exception? failure = null;
        try { result = await placeTask.WaitAsync(TimeSpan.FromSeconds(30)); } catch (Exception ex) { failure = ex; }
        await adminTask.WaitAsync(TimeSpan.FromSeconds(30)); await using var verify = c.Context();
        if (result?.Status == StorefrontPlacementStatus.Placed)
        {
            Assert.False(adminCommittedBeforeRelease, "Admin committed a conflicting live fact before the blocked placement accepted its old quote.");
            Assert.Equal(25000, (await verify.StorefrontOrders.SingleAsync()).GoodsAmount); Assert.Equal(1, (await verify.InventoryReservations.SingleAsync()).ReservedQty);
        }
        else
        {
            Assert.True(result?.Status == StorefrontPlacementStatus.QuoteChanged || failure is ValidationAppException or ConflictAppException || SqlNumber(failure) == 1205, failure?.ToString());
            Assert.Empty(await verify.StorefrontOrders.ToListAsync()); Assert.Empty(await verify.InventoryReservations.ToListAsync()); Assert.Equal(0, (await verify.InventoryBalances.SingleAsync()).ReservedQty);
        }
        await File.WriteAllTextAsync(Web04Support.Artifact("races/" + change + ".json"), System.Text.Json.JsonSerializer.Serialize(new { change, adminCommittedBeforeRelease, Status = result?.Status.ToString(), Error = failure?.GetType().Name, Sql = SqlNumber(failure) }));
        async Task Change()
        {
            await using var db = c.Context();
            switch (change)
            {
                case "price": await db.ProductUnitConversions.Where(x => x.Id == c.Seed.ConversionId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, 27000)); break;
                case "factor": await db.ProductUnitConversions.Where(x => x.Id == c.Seed.ConversionId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Factor, 2)); break;
                case "active": await db.ProductUnitConversions.Where(x => x.Id == c.Seed.ConversionId).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false)); break;
                case "promotionActivation": await db.Promotions.Where(x => x.Id == promotion).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, true)); break;
                default: await Web04PromotionTests.Promotion(c, new() { Name = "Concurrent new program", Type = PromotionType.ProductDiscount, DiscountType = PromotionDiscountType.Percentage,
                    DiscountValue = 20, Items = [new() { ProductId = c.Seed.ProductId }] }); break;
            }
        }
    }
    [Theory]
    [InlineData("blocked")][InlineData("warehouseAllows")][InlineData("requestAllows")]
    public async Task Actual_POS_movement_protects_website_holds_when_forbidding_negative_and_preserves_existing_negative_policy(string policy)
    {
        var c = await fixture.Web04Async(); await c.StockAsync(0); await c.SettingsAsync(negative: false);
        await using (var db = c.Context())
        {
            await db.Warehouses.ExecuteUpdateAsync(x => x.SetProperty(w => w.AllowNegativeInventory, policy == "warehouseAllows"));
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(new InventoryMovementFactory().CreatePurchaseReceipt(c.Seed.WarehouseId, c.Seed.VariantId, 10, 100, "WEB04-REAL-INBOUND", 1, "WEB04-REAL-INBOUND", 1));
        }
        await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync(c.Request(10)))); var before = await c.CountsAsync();
        await using (var db = c.Context())
        {
            var movement = new CreateInventoryMovementRequest { WarehouseId = c.Seed.WarehouseId, ProductVariantId = c.Seed.VariantId, QuantityChange = -1,
                TransactionType = InventoryTransactionType.SaleIssue, ReferenceType = InventoryReferenceType.Order, ReferenceId = "WEB04-INDEPENDENT-POS", ReferenceLineId = 1, AllowNegativeBalance = policy == "requestAllows" };
            var service = InventoryPosPostingContractTests.CreateRealMovementService(db);
            if (policy == "blocked") await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(movement)); else Assert.True((await service.CreateAsync(movement)).IsCreated);
        }
        if (policy == "blocked") Assert.Equal(before, await c.CountsAsync());
        await using var verify = c.Context(); var hold = await verify.InventoryReservations.SingleAsync(); Assert.Equal(10, hold.ReservedQty); Assert.Equal(InventoryReservationStatus.Active, hold.Status);
        var balance = await verify.InventoryBalances.SingleAsync(); Assert.Equal(10, balance.ReservedQty); Assert.Equal(policy == "blocked" ? 10 : 9, balance.OnHandQty);
        Assert.Equal(policy == "blocked" ? 1 : 2, await verify.InventoryTransactions.CountAsync());
    }
    [Theory]
    [InlineData(1)][InlineData(-1)]
    public async Task Storefront_reference_is_hold_only_and_cannot_create_physical_posting_through_generic_service_or_direct_SQL(decimal delta)
    {
        var c = await fixture.Web04Async(); var before = await c.CountsAsync();
        await using (var db = c.Context())
        {
            var request = new CreateInventoryMovementRequest { WarehouseId = c.Seed.WarehouseId, ProductVariantId = c.Seed.VariantId, QuantityChange = delta,
                TransactionType = delta > 0 ? InventoryTransactionType.PurchaseReceipt : InventoryTransactionType.SaleIssue, ReferenceType = InventoryReferenceType.Storefront,
                ReferenceId = "WEB04-FORGED-PHYSICAL", ReferenceLineId = 1, UnitCost = 1 };
            await Assert.ThrowsAsync<BusinessRuleException>(() => InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(request));
        }
        Assert.Equal(before, await c.CountsAsync());
        await using (var db = c.Context())
        {
            db.InventoryTransactions.Add(new() { StoreId = c.StoreId, WarehouseId = c.Seed.WarehouseId, ProductVariantId = c.Seed.VariantId,
                TransactionType = delta > 0 ? InventoryTransactionType.PurchaseReceipt : InventoryTransactionType.SaleIssue, ReferenceType = InventoryReferenceType.Storefront,
                ReferenceId = "WEB04-FORGED-SQL", ReferenceLineId = 1, QuantityChange = delta, BeforeQty = 100, AfterQty = 100 + delta,
                OccurredAtUtc = DateTime.UtcNow, CostSourceType = InventoryCostSourceType.Manual });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(547, SqlNumber(error));
        }
        Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Quote_expiring_after_balance_lock_creates_requote_without_committing_new_empty_balance_or_order()
    {
        var c = await fixture.Web04Async(); var quote = await c.QuoteAsync(); await using (var remove = c.Context()) await remove.InventoryBalances.ExecuteDeleteAsync();
        var before = await c.CountsAsync(); await using var db = c.Context();
        var observer = new ObserveBalance(new InventoryBalanceRepository(db), () => c.Clock.Utc = quote.ExpiresAtUtc.AddSeconds(1));
        var result = await c.Service(db, balances: observer).PlaceAsync(Web04Case.PlaceRequest(quote)); Assert.Equal(StorefrontPlacementStatus.QuoteChanged, result.Status);
        Assert.Null(result.Order); Assert.NotNull(result.ReplacementQuote); Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Cancelled_actual_balance_wait_rolls_back_and_same_request_can_retry_once_after_lock_releases()
    {
        var c = await fixture.Web04Async(); var request = Web04Case.PlaceRequest(await c.QuoteAsync()); var before = await c.CountsAsync();
        await using var blocker = c.Context(); await using var tx = await blocker.Database.BeginTransactionAsync();
        await new InventoryBalanceRepository(blocker).LockBatchAsync([new(c.StoreId, c.Seed.WarehouseId, c.Seed.VariantId)]);
        using var cancellation = new CancellationTokenSource();
        await using (var placing = c.Context())
        {
            var observer = new ObserveBalance(new InventoryBalanceRepository(placing)); var task = c.Service(placing, balances: observer).PlaceAsync(request, cancellation.Token);
            await observer.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); Assert.Equal(before, await c.CountsAsync());
        }
        await tx.CommitAsync(); var result = await c.PlaceAsync(request); Assert.Equal(StorefrontPlacementStatus.Placed, result.Status);
        await using var verify = c.Context(); Assert.Single(await verify.StorefrontOrders.ToListAsync()); Assert.Single(await verify.InventoryReservations.ToListAsync());
    }
    [Fact]
    public async Task Different_request_id_cannot_consume_same_accepted_quote_or_disclose_its_private_outcome()
    {
        var c = await fixture.Web04Async(); var quote = await c.QuoteAsync(); var first = Web04Case.PlaceRequest(quote); var original = await c.PlaceAsync(first); var before = await c.CountsAsync();
        var conflict = await Assert.ThrowsAsync<ConflictAppException>(() => c.PlaceAsync(Web04Case.PlaceRequest(quote))); Assert.DoesNotContain(Web04Support.GuestToken(original.Order!), conflict.Message);
        Assert.Equal(before, await c.CountsAsync()); Assert.Equal(original.Order, (await c.PlaceAsync(first)).Order);
    }
    private static int? SqlNumber(Exception? exception) { while (exception is not null) { if (exception is SqlException sql) return sql.Number; exception = exception.InnerException; } return null; }
    private sealed class ObserveBalance(IInventoryBalanceRepository inner, Action? after = null) : IInventoryBalanceRepository
    {
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task LockBatchAsync(IReadOnlyList<InventoryPostingLockKey> keys, CancellationToken ct = default) { Reached.TrySetResult(); await inner.LockBatchAsync(keys, ct); after?.Invoke(); }
        public Task<InventoryBalance?> GetByWarehouseAndVariantAsync(int warehouseId, int productVariantId, CancellationToken ct = default) => inner.GetByWarehouseAndVariantAsync(warehouseId, productVariantId, ct);
        public Task<InventoryBalance> GetOrCreateAsync(int warehouseId, int productVariantId, CancellationToken ct = default) => inner.GetOrCreateAsync(warehouseId, productVariantId, ct);
        public Task<InventoryBalance> LockAndGetOrCreateAsync(int storeId, int warehouseId, int productVariantId, CancellationToken ct = default) => inner.LockAndGetOrCreateAsync(storeId, warehouseId, productVariantId, ct);
        public Task AddAsync(InventoryBalance balance, CancellationToken ct = default) => inner.AddAsync(balance, ct);
        public Task<List<InventoryBalance>> GetByVariantAsync(int productVariantId, CancellationToken ct = default) => inner.GetByVariantAsync(productVariantId, ct);
        public Task SaveChangesAsync(CancellationToken ct = default) => inner.SaveChangesAsync(ct);
        public Task<List<InventoryBalance>> GetNegativeBalancesAsync(CancellationToken ct = default) => inner.GetNegativeBalancesAsync(ct);
        public Task<InventoryBalance?> GetDetailByWarehouseAndVariantAsync(int warehouseId, int productVariantId, CancellationToken ct = default) => inner.GetDetailByWarehouseAndVariantAsync(warehouseId, productVariantId, ct);
        public Task<(List<InventoryBalance> Items, int TotalItems)> QueryCurrentBalancesAsync(InventoryBalanceQueryRequest request, CancellationToken ct = default) => inner.QueryCurrentBalancesAsync(request, ct);
        public Task<Dictionary<int, decimal>> GetAvailableQtyMapByVariantIdsAsync(int storeId, int warehouseId, IReadOnlyCollection<int> variantIds, CancellationToken ct = default) => inner.GetAvailableQtyMapByVariantIdsAsync(storeId, warehouseId, variantIds, ct);
    }
}
