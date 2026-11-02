using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06ReturnIncidentTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Failed_delivery_report_is_not_a_stock_receipt_and_actual_incremental_returns_reuse_original_slices_once()
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync();
        await c.OutcomeAllAsync(false);
        await using (var db = c.Context())
        {
            Assert.Equal(6m, (await db.InventoryBalances.SingleAsync()).OnHandQty);
            Assert.Empty(await db.DeliveryReturnedCostFragments.ToListAsync());
            Assert.Equal(StorefrontOrderPhase.Shipping, (await db.StorefrontOrders.SingleAsync()).Phase);
        }
        var first = await c.ReturnRequestAsync(0.0001m);
        await using (var db = c.Context()) await c.Service(db).ReceiveReturnAsync(c.OrderId, first);
        await using (var db = c.Context())
        {
            Assert.Equal(6.0001m, (await db.InventoryBalances.SingleAsync()).OnHandQty);
            Assert.Equal(0.0001m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity));
            Assert.Equal(StorefrontOrderPhase.Shipping, (await db.StorefrontOrders.SingleAsync()).Phase);
        }
        var rest = await c.ReturnRequestAsync(); Assert.Equal("3.9999", Assert.Single(rest.Lines).BaseQuantity);
        await using (var db = c.Context()) await c.Service(db).ReceiveReturnAsync(c.OrderId, rest);
        var beforeReplay = await c.PhysicalSnapshotAsync();
        await using (var db = c.Context()) Assert.True((await c.Service(db).ReceiveReturnAsync(c.OrderId, first)).Replayed);
        Assert.Equal(beforeReplay, await c.PhysicalSnapshotAsync());
        await using (var db = c.Context())
        {
            Assert.Equal(10m, (await db.InventoryBalances.SingleAsync()).OnHandQty);
            Assert.Equal(4m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity));
            Assert.Equal(40_000m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.CostAmount));
            Assert.Equal(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.SingleAsync()).Phase);
            Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
            var dispatchIds = await db.DeliveryDispatchCostFragments.Select(x => x.Id).ToListAsync();
            Assert.All(await db.DeliveryReturnedCostFragments.ToListAsync(), x => Assert.Contains(x.DispatchCostFragmentId, dispatchIds));
        }
    }

    [Theory]
    [InlineData("4.0001")] [InlineData("-1")] [InlineData("0")]
    public async Task Return_above_actual_unreturned_budget_or_nonpositive_quantity_is_rejected_atomically(string quantity)
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync(); await c.OutcomeAllAsync(false);
        var request = await c.ReturnRequestAsync(); request.Lines[0] = request.Lines[0] with { BaseQuantity = quantity };
        var before = await c.PhysicalSnapshotAsync();
        await using (var db = c.Context())
        {
            var error = await Record.ExceptionAsync(() => c.Service(db).ReceiveReturnAsync(c.OrderId, request));
            Assert.NotNull(error);
            if (quantity == "4.0001")
            {
                var typed = Assert.IsType<DeliveryFoundationException>(error); Assert.Equal(409, typed.StatusCode); Assert.Equal("RETURN_BUDGET_EXCEEDED", typed.Code);
            }
            else if (quantity == "0") Assert.IsType<ConflictAppException>(error);
            else Assert.IsType<ValidationAppException>(error);
        }
        Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }

    [Fact]
    public async Task Before_ledger_all_recovered_count_clears_incident_without_fictitious_return_and_allows_single_new_dispatch()
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); await c.PickAllAsync(); var incident = await c.IncidentRequestAsync();
        await using (var db = c.Context()) await c.Service(db).RecordIncidentAsync(c.OrderId, incident);
        Assert.True((await c.DetailAsync()).HasIncident); var dispatch = await c.DispatchRequestAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => c.Service(db).DispatchAsync(c.OrderId, dispatch));
        int id; await using (var db = c.Context()) id = (await db.Set<StorefrontCustodyIncident>().SingleAsync()).Id;
        var request = await c.ReconcileRequestAsync(id, StorefrontIncidentResolution.AllGoodsRecoveredAtSource);
        await using (var db = c.Context()) await c.Service(db).ReconcileIncidentAsync(c.OrderId, request);
        await using (var db = c.Context())
        {
            Assert.Empty(await db.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Delivery).ToListAsync());
            Assert.Empty(await db.DeliveryReturnedCostFragments.ToListAsync());
            Assert.Equal(10m, (await db.InventoryBalances.SingleAsync()).OnHandQty); Assert.Equal(4m, (await db.InventoryBalances.SingleAsync()).ReservedQty);
            Assert.NotNull((await db.Set<StorefrontCustodyIncident>().SingleAsync()).ReconciledByEvidenceId);
        }
        Assert.False((await c.DetailAsync()).HasIncident); await c.DispatchAsync();
        await using var verify = c.Context(); Assert.Single(await verify.DeliveryJournalEntries.Where(x => x.Kind == DeliveryJournalKind.Dispatch).ToListAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task All_handed_reconciliation_records_prior_handover_or_reuses_existing_export_without_double_stock_or_cost(bool alreadyExported)
    {
        var c = await Web06Case.CreateAsync(fixture, multipleSources: true); await c.PrepareAsync(); await c.PickAllAsync(); if (alreadyExported) await c.DispatchAsync();
        var incident = await c.IncidentRequestAsync(); await using (var db = c.Context()) await c.Service(db).RecordIncidentAsync(c.OrderId, incident);
        int id; await using (var db = c.Context()) id = (await db.Set<StorefrontCustodyIncident>().SingleAsync()).Id;
        var request = await c.ReconcileRequestAsync(id, StorefrontIncidentResolution.AllGoodsHandedToCarrier);
        await using (var db = c.Context()) await c.Service(db).ReconcileIncidentAsync(c.OrderId, request);
        var snapshot = await c.PhysicalSnapshotAsync();
        await using (var db = c.Context()) Assert.True((await c.Service(db).ReconcileIncidentAsync(c.OrderId, request)).Replayed);
        Assert.Equal(snapshot, await c.PhysicalSnapshotAsync());
        await using (var db = c.Context())
        {
            Assert.Equal(2, await db.DeliveryJournalEntries.CountAsync(x => x.Kind == DeliveryJournalKind.Dispatch));
            Assert.Equal(4m, await db.DeliveryDispatchCostFragments.SumAsync(x => x.BaseQuantity));
            Assert.Equal(55_000m, await db.DeliveryDispatchCostFragments.SumAsync(x => x.CostAmount));
            Assert.Equal(8.5m, await db.InventoryBalances.SumAsync(x => x.OnHandQty)); Assert.Empty(await db.DeliveryReturnedCostFragments.ToListAsync());
        }
        Assert.False((await c.DetailAsync()).HasIncident); await c.OutcomeAllAsync(true);
        Assert.Equal(StorefrontOrderPhase.Delivered, (await c.DetailAsync()).Phase);
    }

    [Fact]
    public async Task Exported_incident_full_recovered_count_posts_only_residual_actual_returns_and_partial_count_cannot_clear_stop()
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync(); await c.OutcomeAllAsync(false);
        var partial = await c.ReturnRequestAsync(0.5m); await using (var db = c.Context()) await c.Service(db).ReceiveReturnAsync(c.OrderId, partial);
        var incident = await c.IncidentRequestAsync(); await using (var db = c.Context()) await c.Service(db).RecordIncidentAsync(c.OrderId, incident);
        int id; await using (var db = c.Context()) id = (await db.Set<StorefrontCustodyIncident>().SingleAsync()).Id;
        var incomplete = await c.ReconcileRequestAsync(id, StorefrontIncidentResolution.AllGoodsRecoveredAtSource);
        incomplete.CountedLines[0] = incomplete.CountedLines[0] with { BaseQuantity = "3.9999" };
        var before = await c.PhysicalSnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => c.Service(db).ReconcileIncidentAsync(c.OrderId, incomplete));
        Assert.Equal(before, await c.PhysicalSnapshotAsync()); Assert.True((await c.DetailAsync()).HasIncident);
        var complete = await c.ReconcileRequestAsync(id, StorefrontIncidentResolution.AllGoodsRecoveredAtSource);
        await using (var db = c.Context()) await c.Service(db).ReconcileIncidentAsync(c.OrderId, complete);
        await using var verify = c.Context();
        Assert.Equal(4m, await verify.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity));
        Assert.Equal(40_000m, await verify.DeliveryReturnedCostFragments.SumAsync(x => x.CostAmount));
        Assert.Equal(10m, (await verify.InventoryBalances.SingleAsync()).OnHandQty);
        Assert.Single(await verify.DeliveryJournalEntries.Where(x => x.Kind == DeliveryJournalKind.Dispatch).ToListAsync());
        Assert.Equal(StorefrontOrderPhase.Cancelled, (await verify.StorefrontOrders.SingleAsync()).Phase);
        Assert.NotNull((await verify.Set<StorefrontCustodyIncident>().SingleAsync()).ReconciledByEvidenceId);
    }

    [Fact]
    public async Task Exported_recovered_incident_needs_receive_permission_not_only_reconcile_permission()
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync();
        var incident = await c.IncidentRequestAsync(); await using (var db = c.Context()) await c.Service(db).RecordIncidentAsync(c.OrderId, incident);
        int id; await using (var db = c.Context()) id = (await db.Set<StorefrontCustodyIncident>().SingleAsync()).Id;
        var request = await c.ReconcileRequestAsync(id, StorefrontIncidentResolution.AllGoodsRecoveredAtSource); var before = await c.PhysicalSnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ForbiddenAppException>(() => c.Service(db,
            Web06Support.PhysicalPermissions.Where(x => x != StorefrontPermissions.ReceiveReturns)).ReconcileIncidentAsync(c.OrderId, request));
        Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }
}
