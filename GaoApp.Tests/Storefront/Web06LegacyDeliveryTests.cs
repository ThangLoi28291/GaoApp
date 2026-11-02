using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Services.Delivery;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06LegacyDeliveryTests(Web01Fixture fixture)
{
    private static readonly string[] Rights = [PermissionCodes.Delivery.View, PermissionCodes.Delivery.Create, PermissionCodes.Delivery.Pick,
        PermissionCodes.Delivery.ApproveChanges, PermissionCodes.Delivery.Handover, PermissionCodes.Inventory.Valuation.View];
    [Fact]
    public async Task Ordinary_POS_origin_foundation_picking_and_real_trip_export_keep_typed_legacy_stock_cost_and_other_web_hold()
    {
        var c = await Web06Case.CreateAsync(fixture); var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c);
        await using var db = c.Context(); var actor = new Web01Actor(c.Seed.UserId, terminalId: terminal); var rights = new Web01Permissions(c.StoreId, c.Seed.UserId, Rights);
        var shift = await db.POSShifts.SingleAsync(x => x.TerminalId == terminal);
        var cart = new Order { StoreId = c.StoreId, POSShiftId = shift.Id, Status = OrderStatus.Draft, Subtotal = 100_000m, GrandTotal = 100_000m, BalanceDue = 100_000m };
        cart.Lines.Add(new() { StoreId = c.StoreId, ProductId = c.Seed.ProductId, VariantId = c.Seed.VariantId, Quantity = 2, BaseQuantity = 2, Multiplier = 1,
            ItemName = "WEB06 ordinary POS source fixture", SellingUnitId = c.Seed.UnitId, SellingUnitName = "unit", BaseUnitId = c.Seed.UnitId, BaseUnitName = "unit", UnitName = "unit", UnitPrice = 50_000m, LineTotal = 100_000m });
        db.Add(cart); await db.SaveChangesAsync(); var foundation = new DeliveryFoundationService(db, actor, rights); DeliveryDetailDto created;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            created = await foundation.CreateInTransactionAsync(new(Guid.NewGuid(), cart.Id, "Legacy POS recipient", "0901234567", "Synthetic owned fixture", "No real delivery"));
            // Arrangement of a legitimately transferred source state. This case
            // does not claim the DeliveryPosService/POS CreateDraft UI transition.
            cart.Status = OrderStatus.Cancelled; await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        Assert.True(created.SourceCartId > 0); Assert.Equal(cart.Id, created.SourceCartId); Assert.Equal(shift.Id, created.CreatedShiftId); Assert.Equal(terminal, created.CreatedTerminalId);
        var inventory = new DeliveryInventoryService(db, actor, rights, new InventoryBalanceRepository(db), InventoryPosPostingContractTests.CreateRealMovementService(db));
        var picking = new DeliveryPickingService(db, actor, rights, inventory);
        await picking.CompleteAsync(created.Id, new(Guid.NewGuid(), created.Version, [new(created.Lines.Single().Id, "2", null)]));
        var ready = await foundation.GetAsync(created.Id); Assert.Equal(DeliveryState.ReadyForHandover.ToString(), ready.State);
        var trips = new DeliveryTripService(db, actor, rights, inventory, new InventoryBalanceRepository(db));
        var draft = await trips.CreateDraftAsync(new(Guid.NewGuid(), c.CourierId, [new(created.Id, ready.Version)], "Actual legacy physical handover fixture"));
        var detail = await trips.GetAsync(draft.TripId);
        var request = new DeliveryTripHandoverRequest(Guid.NewGuid(), detail.Version, detail.Orders.Where(x => x.IsActive).Select(x => new DeliveryTripOrderSelection(x.DeliveryOrderId, x.Version)).ToArray(), c.CourierId, true, "WEB06 actual full receipt by assigned courier");
        await trips.HandoverAsync(draft.TripId, request);
        await using var check = c.Context(); var legacy = await check.DeliveryOrders.SingleAsync(); Assert.Equal(DeliverySourceKind.Pos, legacy.SourceKind); Assert.Null(legacy.StorefrontOrderId); Assert.Null(legacy.StorefrontFulfillmentSourceId);
        Assert.Equal(cart.Id, legacy.SourceCartId); Assert.Equal(shift.Id, legacy.CreatedShiftId); Assert.Equal(terminal, legacy.CreatedTerminalId);
        var export = await check.InventoryTransactions.SingleAsync(x => x.ReferenceType == InventoryReferenceType.Delivery); Assert.Equal(InventoryTransactionType.DeliveryDispatchOut, export.TransactionType); Assert.Equal(-2m, export.QuantityChange);
        Assert.Equal(8m, (await check.InventoryBalances.SingleAsync()).OnHandQty); Assert.Equal(4m, (await check.InventoryBalances.SingleAsync()).ReservedQty);
        Assert.Equal(20_000m, await check.DeliveryDispatchCostFragments.SumAsync(x => x.CostAmount)); Assert.Single(await check.DeliveryJournalEntries.ToListAsync());
        Assert.Equal(StorefrontOrderPhase.AwaitingPreparation, (await check.StorefrontOrders.SingleAsync()).Phase);
        Assert.Empty(await check.Set<StorefrontFulfillmentWork>().ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync()); Assert.Empty(await check.InvoiceHeads.ToListAsync());
        Assert.Equal(OrderStatus.Cancelled, (await check.Orders.SingleAsync()).Status);
    }
    [Fact]
    public async Task Native_source_cannot_enter_legacy_POS_picking_path_or_gain_a_fake_source_cart()
    {
        var c = await Web06Case.CreateAsync(fixture, usePack: true, packFactor: 100, quantity: .01m); await c.PrepareAsync();
        var before = await c.PhysicalSnapshotAsync(); var child = (await c.DetailAsync()).Children.Single(); await using var db = c.Context();
        var actor = new Web01Actor(c.Seed.UserId); var rights = new Web01Permissions(c.StoreId, c.Seed.UserId, Rights);
        var inventory = new DeliveryInventoryService(db, actor, rights, new InventoryBalanceRepository(db), InventoryPosPostingContractTests.CreateRealMovementService(db));
        var picking = new DeliveryPickingService(db, actor, rights, inventory); var source = await db.DeliveryOrders.SingleAsync();
        await Assert.ThrowsAsync<DeliveryFoundationException>(() => picking.CompleteAsync(child.DeliveryOrderId, new(Guid.NewGuid(), Convert.ToBase64String(source.RowVersion), [new(child.Lines.Single().DeliveryOrderLineId, "1", null)])));
        Assert.Equal(before, await c.PhysicalSnapshotAsync()); Assert.Null(source.SourceCartId); Assert.Null(source.CreatedShiftId); Assert.Null(source.CreatedTerminalId);
    }
}
