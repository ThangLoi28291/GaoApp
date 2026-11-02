using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Orders;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06CostResolutionTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_mixed_source_provisional_exposure_and_partial_full_inbound_resolution_use_dispatch_warehouse_not_settling_shift()
    {
        var c = await Web06Case.CreateAsync(fixture, multipleSources: true, negative: true, warehouseNegative: false);
        await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await c.PrepareAsync(); await c.PickAllAsync();
        int sourceWarehouse; int closingWarehouse;
        await using (var db = c.Context())
        {
            sourceWarehouse = await db.Warehouses.Where(x => x.Id != c.Seed.WarehouseId).Select(x => x.Id).SingleAsync();
            var loss = new InventoryMovementFactory().CreateAdjustmentDecrease(sourceWarehouse, c.Seed.VariantId, 10, 20_000m, "WEB06 genuine stock loss after checkout reservation");
            loss.AllowNegativeBalance = true; await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(loss);
            var closing = new Warehouse { StoreId = c.StoreId, LegalEntityId = c.Seed.LegalEntityId, Code = "W06-CLOSING", Name = "WEB06 closing shift warehouse, no source goods", IsActive = true };
            db.Add(closing); await db.SaveChangesAsync(); closingWarehouse = closing.Id;
        }
        await c.DispatchAsync(); await c.OutcomeAllAsync(true); await Web06FinanceSupport.CollectAllAsync(c);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c, closingWarehouse); await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 200_000m));
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        int issueId; decimal originalDispatchCost; decimal originalProvisionalCost;
        await using (var db = c.Context())
        {
            var sale = await db.Orders.SingleAsync(); Assert.True(sale.HasInventoryIssue); Assert.Equal(InventoryResolutionStatus.PendingResolution, sale.InventoryResolutionStatus);
            var issue = await db.OrderInventoryIssues.Include(x => x.Lines).SingleAsync(); issueId = issue.Id; var line = Assert.Single(issue.Lines);
            var fragments = await db.DeliveryDispatchCostFragments.ToListAsync(); Assert.Contains(fragments, x => !x.IsProvisional && x.SourceWarehouseId == c.Seed.WarehouseId);
            Assert.All(fragments.Where(x => x.IsProvisional), x => Assert.Equal(sourceWarehouse, x.SourceWarehouseId));
            Assert.Equal(1.5m, fragments.Where(x => x.IsProvisional).Sum(x => x.BaseQuantity));
            Assert.Equal(1.5m, line.NegativeQty); Assert.Equal(4m, line.OrderedQty); originalProvisionalCost = fragments.Where(x => x.IsProvisional).Sum(x => x.CostAmount);
            Assert.Equal(originalProvisionalCost, line.ProvisionalCostAmount); originalDispatchCost = fragments.Sum(x => x.CostAmount);
            Assert.Equal(1.5m, await db.Set<StorefrontInventoryIssueSource>().SumAsync(x => x.UnresolvedQuantity));
            Assert.Equal(closingWarehouse, (await db.POSShifts.SingleAsync()).WarehouseId);
        }
        var before = await Web06FinanceSupport.ProfitAsync(c); Assert.Equal(GaoApp.Application.DTOs.Reports.Profit.ProfitQuality.Provisional, before.Current.Cogs.Quality);
        await Inbound(c, sourceWarehouse, .5m);
        await using (var db = c.Context())
        {
            var service = Issues(c, db); await service.RefreshAutoResolutionAsync(issueId); var check = await service.ValidateBeforeApproveAsync(issueId); Assert.False(check.CanApprove);
            var issue = await db.OrderInventoryIssues.Include(x => x.Lines).SingleAsync(); var line = Assert.Single(issue.Lines);
            Assert.Equal(.5m, line.AutoDetectedInboundQty); Assert.False(line.AutoDetectedCostResolved); Assert.False(line.IsResolved); Assert.Equal(1.5m, line.NegativeQty);
            Assert.Equal(originalProvisionalCost, line.ProvisionalCostAmount); Assert.True((await db.OrderLines.SingleAsync()).IsProvisionalCost);
        }
        await Inbound(c, sourceWarehouse, 1m);
        int actions; int revaluations;
        await using (var db = c.Context())
        {
            var service = Issues(c, db); await service.RefreshAutoResolutionAsync(issueId); Assert.True((await service.ValidateBeforeApproveAsync(issueId)).CanApprove);
            var line = (await db.OrderInventoryIssues.Include(x => x.Lines).SingleAsync()).Lines.Single(); Assert.Equal(1.5m, line.AutoDetectedInboundQty); Assert.True(line.AutoDetectedCostResolved); Assert.True(line.IsResolved);
            await service.ApproveAsync(issueId, c.Seed.UserId, "WEB06 actual source inbound/cost fully resolved");
            var sale = await db.Orders.Include(x => x.Lines).SingleAsync(); Assert.True(sale.HasInventoryIssue); Assert.Equal(InventoryResolutionStatus.Approved, sale.InventoryResolutionStatus);
            var actual = Assert.Single(sale.Lines); Assert.False(actual.IsProvisionalCost); Assert.Equal(70_000m, actual.LineCostTotal); Assert.Equal(130_000m, actual.GrossProfit);
            Assert.Equal(originalDispatchCost, await db.DeliveryDispatchCostFragments.SumAsync(x => x.CostAmount));
            Assert.Equal(2, await db.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.Delivery && x.TransactionType == InventoryTransactionType.DeliveryDispatchOut));
            Assert.Equal(0m, (await db.InventoryBalances.SingleAsync(x => x.WarehouseId == sourceWarehouse)).OnHandQty);
            Assert.False(await db.InventoryTransactions.AnyAsync(x => x.WarehouseId == closingWarehouse));
            Assert.Equal(200_000m, (await db.POSShifts.SingleAsync()).CashInTotal); Assert.Single(await db.Orders.ToListAsync());
            actions = await db.OrderInventoryIssueActions.CountAsync(); revaluations = await db.InventoryValuationEntries.CountAsync(x => x.EntryType == InventoryValuationEntryType.Revaluation);
        }
        await using (var db = c.Context())
        {
            await Issues(c, db).RefreshAutoResolutionAsync(issueId); Assert.Equal(actions, await db.OrderInventoryIssueActions.CountAsync()); Assert.Equal(revaluations, await db.InventoryValuationEntries.CountAsync(x => x.EntryType == InventoryValuationEntryType.Revaluation));
            var approved = await db.Orders.SingleAsync(); Assert.True(approved.HasInventoryIssue); Assert.Equal(InventoryResolutionStatus.Approved, approved.InventoryResolutionStatus); Assert.False((await db.OrderLines.SingleAsync()).IsProvisionalCost);
        }
        var final = await Web06FinanceSupport.ProfitAsync(c); Assert.Equal(70_000m, final.Current.Cogs.Value); Assert.Equal(130_000m, final.Current.GrossProfit.Value);
        Assert.Equal(GaoApp.Application.DTOs.Reports.Profit.ProfitQuality.Finalized, final.Current.Cogs.Quality); Assert.Empty(final.CostDiagnostics.Issues);
    }
    private static OrderInventoryIssueService Issues(Web06Case c, GaoApp.Infrastructure.Data.AppDbContext db)
        => new(new OrderInventoryIssueRepository(db), new OrderRepository(db), NullLogger<OrderInventoryIssueService>.Instance,
            new StorefrontInventoryIssueAdapter(db, new InventoryBalanceRepository(db), new Web01Actor(c.Seed.UserId), new Web01Permissions(c.StoreId, c.Seed.UserId, [StorefrontPermissions.Reconcile]), TimeProvider.System));
    private static async Task Inbound(Web06Case c, int warehouse, decimal quantity)
    {
        await using var db = c.Context(); await using var tx = await db.Database.BeginTransactionAsync();
        var movement = InventoryPosPostingContractTests.CreateRealMovementService(db);
        var before = (await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.WarehouseId == warehouse && x.ProductVariantId == c.Seed.VariantId)).OnHandQty;
        Assert.True(before < 0); Assert.False((await db.Warehouses.SingleAsync(x => x.Id == warehouse)).AllowNegativeInventory);
        var inbound = new InventoryMovementFactory().CreateAdjustmentIncrease(warehouse, c.Seed.VariantId, quantity, 30_000m, "WEB06 actual source warehouse cost replacement");
        inbound.AllowNegativeBalance = true; // Trusted positive receipt may improve an existing native-authorized deficit.
        await movement.CreateAsync(inbound);
        Assert.Equal(before + quantity, (await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.WarehouseId == warehouse && x.ProductVariantId == c.Seed.VariantId)).OnHandQty);
        Assert.False((await db.Warehouses.SingleAsync(x => x.Id == warehouse)).AllowNegativeInventory);
        var layer = await db.InventoryCostLayers.Where(x => x.WarehouseId == warehouse && x.ProductVariantId == c.Seed.VariantId).OrderByDescending(x => x.Id).FirstAsync();
        await new InventoryRevaluationService(new InventoryCostLayerRepository(db), new InventoryCostLayerAllocationRepository(db), new InventoryValuationEntryRepository(db),
            new InventoryTransactionRepository(db), new InventoryBalanceRepository(db), movement).ResolveByInboundLayerAsync(layer.Id, DateTime.UtcNow, "WEB06 actual FIFO source resolution");
        await tx.CommitAsync();
    }
}
