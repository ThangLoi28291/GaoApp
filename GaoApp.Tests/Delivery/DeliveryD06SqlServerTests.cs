using System.Diagnostics;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Infrastructure.Data;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD06"), Trait("Category", "DeliveryD06")]
public sealed class DeliveryD06SqlServerTests(DeliveryD02Fixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task T01_Planning_and_abandonment_bind_ready_orders_without_export_custody_or_underlying_order_effects()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture);
        var before = await c.StockAsync(); var created = await c.CreateDraftAsync();
        Assert.False(created.Replayed); Assert.Equal("create", created.Operation);
        Assert.Equal(before, await c.StockAsync());
        var detail = await c.DetailAsync(); Assert.Equal(nameof(DeliveryTripState.Draft), detail.State);
        Assert.Equal(c.Courier.UserId, detail.CourierUserId);
        Assert.All(detail.Orders, x => { Assert.Null(x.Custody); Assert.Equal(nameof(DeliveryState.ReadyForHandover), x.State); });
        await using (var db = c.Context())
        {
            (await db.Users.SingleAsync(x => x.Id == c.Courier.UserId)).IsActive = false;
            foreach (var id in c.Orders.Select(x => x.Account.Store.WarehouseId))
                (await db.Warehouses.SingleAsync(x => x.Id == id)).IsActive = false;
            await db.SaveChangesAsync();
        }
        await using (var db = c.Context())
            await c.Service(db).AbandonDraftAsync(c.TripId, new(Guid.NewGuid(), detail.Version, "Đổi kế hoạch; không hủy đơn"));
        Assert.Equal(before, await c.StockAsync());
        await using var verify = c.Context();
        Assert.Empty(await verify.DeliveryTripOrders.Where(x => x.TripId == c.TripId && x.IsActive).ToArrayAsync());
        Assert.Empty(await verify.DeliveryCustodyEvents.Where(x => x.TripId == c.TripId).ToArrayAsync());
        Assert.Equal(DeliveryTripState.Abandoned, (await verify.DeliveryTrips.SingleAsync(x => x.Id == c.TripId)).State);
    }

    [Fact]
    public async Task T02_Multiorder_handover_keeps_distinct_customers_sources_actors_and_exact_FIFO_exports()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); var finance = await c.Orders[0].Inventory.FinanceAsync();
        await c.CreateDraftAsync(); var request = await c.HandoverRequestAsync();
        var beforeUtc = DateTimeOffset.UtcNow; var ack = await c.HandoverAsync(request);
        Assert.Equal("handover", ack.Operation); Assert.False(ack.Replayed);
        Assert.All(ack.Orders, x => Assert.Equal(nameof(DeliveryState.HandedOver), x.State));
        await using var db = c.Context();
        var events = await db.DeliveryCustodyEvents.Where(x => x.TripId == c.TripId).OrderBy(x => x.DeliveryOrderId).ToArrayAsync();
        Assert.Equal(2, events.Length);
        Assert.All(events, x => {
            Assert.Equal(DeliveryCustodyKind.Handover, x.Kind); Assert.Equal(c.Courier.UserId, x.CourierUserId);
            Assert.Equal(c.Actor.UserId, x.ActorUserId); Assert.NotEqual(x.ActorUserId, x.CourierUserId);
            Assert.Equal(DeliveryTripPolicy.StaffAttestation, x.ConfirmationNote); Assert.Null(x.PreviousCustodyEventId);
            Assert.InRange(new DateTimeOffset(DateTime.SpecifyKind(x.ConfirmedAtUtc, DateTimeKind.Utc)), beforeUtc, DateTimeOffset.UtcNow); });
        var expected = new[] { (2m, 18000m), (3m, 33000m) };
        for (var index = 0; index < c.Orders.Count; index++)
        {
            var item = c.Orders[index]; var origin = item.Picking.Source.Detail;
            var journal = Assert.Single(await db.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == item.Id).ToArrayAsync());
            Assert.Equal(DeliveryJournalKind.Dispatch, journal.Kind); Assert.Equal(expected[index].Item1, journal.BaseQuantity);
            Assert.Equal(expected[index].Item2, journal.CostAmount); Assert.Equal(0m, journal.MoneyAmount); Assert.Null(journal.SaleOrderId);
            var custody = Assert.Single(events, x => x.DeliveryOrderId == item.Id);
            var line = Assert.Single(await db.DeliveryCustodyLines.Where(x => x.CustodyEventId == custody.Id).ToArrayAsync());
            Assert.Equal(journal.Id, line.DispatchJournalEntryId); Assert.Equal(journal.DeliveryOrderLineId, line.DeliveryOrderLineId);
            Assert.Equal(origin.SourceWarehouseId, line.SourceWarehouseId); Assert.Equal(origin.SourceLegalEntityId, line.SourceLegalEntityId);
            Assert.Equal(expected[index].Item1, line.ConfirmedBaseQuantity); Assert.Equal(0m, line.CumulativeAcceptedReturnAtConfirmation);
            var summary = await item.Inventory.GetAsync(); Assert.NotNull(summary.Custody);
            Assert.Equal(c.Courier.UserId, summary.Custody.CourierUserId);
            Assert.Null(summary.InCustodyBaseQuantityText);
            var quantity = Assert.Single(summary.Lines, x => x.LineId == journal.DeliveryOrderLineId);
            Assert.Equal(quantity.DispatchedBaseQuantityText, quantity.InCustodyBaseQuantityText);
            Assert.Equal("0", quantity.AcceptedReturnedBaseQuantityText);
            var childKey = DeliveryTripPolicy.ChildCommandId(request.ClientRequestId, item.Id);
            Assert.Single(await db.DeliveryCommandReceipts.Where(x => x.ClientRequestId == childKey).ToArrayAsync());
        }
        var detail = await c.DetailAsync();
        Assert.Equal(2, detail.Orders.Select(x => x.RecipientName).Distinct().Count());
        Assert.Equal(2, detail.Orders.Select(x => x.SourceWarehouseId).Distinct().Count());
        Assert.Equal(2, detail.Orders.Select(x => x.SourceLegalEntityId).Distinct().Count());
        var ownVariants = await Task.WhenAll(c.Orders.Select(x => x.Inventory.VariantAsync()));
        var ownWarehouses = c.Orders.Select(x => x.Account.Store.WarehouseId).ToArray();
        Assert.All(await db.InventoryBalances.Where(x => ownVariants.Contains(x.ProductVariantId) && ownWarehouses.Contains(x.WarehouseId)).ToArrayAsync(),
            x => { Assert.Equal(0m, x.OnHandQty); Assert.Equal(0m, x.ReservedQty); Assert.Equal(0m, x.InventoryValue); });
        Assert.Equal(finance, await c.Orders[0].Inventory.FinanceAsync());
    }

    [Fact]
    public async Task T03_Failure_after_the_first_export_rolls_back_the_entire_trip_and_retries_exactly_once()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var request = await c.HandoverRequestAsync(); var before = await c.SnapshotAsync();
        await Assert.ThrowsAsync<D06InjectedFailure>(() => c.HandoverAsync(request,
            interceptor: new D06SecondExportFailure(c.Orders[1].Id)));
        Assert.Equal(before, await c.SnapshotAsync());
        var ack = await c.HandoverAsync(request); Assert.False(ack.Replayed);
        var after = await c.SnapshotAsync(); Assert.True((await c.HandoverAsync(request)).Replayed);
        Assert.Equal(after, await c.SnapshotAsync());
        await using var db = c.Context();
        Assert.Equal(2, await db.DeliveryCustodyEvents.CountAsync(x => x.TripId == c.TripId));
        Assert.Equal(2, await db.DeliveryJournalEntries.CountAsync(x => c.Orders.Select(o => o.Id).Contains(x.DeliveryOrderId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task T04_A_reopened_or_cancelled_draft_member_rejects_every_export_atomically_and_abandon_keeps_other_holds(bool cancel)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var request = await c.HandoverRequestAsync(); var changed = c.Orders[1];
        var current = await changed.Picking.GetAsync();
        if (cancel)
        {
            await using var db = changed.Picking.Source.Context();
            await changed.Inventory.Service(db).CancelBeforeHandoverAsync(changed.Id,
                new(Guid.NewGuid(), current.Delivery.Version, "Khách hủy trước xuất"));
        }
        else await changed.Picking.CommandAsync("reopen", new DeliveryPickingReopenRequest(Guid.NewGuid(), current.Delivery.Version, "Kiểm lại lượng"));
        var before = await c.SnapshotAsync();
        var error = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.HandoverAsync(request));
        Assert.Equal(409, error.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
        await using (var db = c.Context())
            await c.Service(db).AbandonDraftAsync(c.TripId, new(Guid.NewGuid(), (await c.DetailAsync()).Version, "Lập chuyến mới từ đơn còn hợp lệ"));
        Assert.Equal("2", (await c.Orders[0].Inventory.GetAsync()).HeldBaseQuantityText);
        await using var verify = c.Context(); Assert.Empty(await verify.DeliveryCustodyEvents.Where(x => x.TripId == c.TripId).ToArrayAsync());
        Assert.Empty(await verify.DeliveryJournalEntries.Where(x => c.Orders.Select(o => o.Id).Contains(x.DeliveryOrderId)).ToArrayAsync());
    }

    [Fact]
    public async Task T05_Handover_replay_returns_the_old_ack_before_versions_and_never_reexports_stock()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var request = await c.HandoverRequestAsync(); var first = await c.HandoverAsync(request);
        var before = await c.SnapshotAsync(); var replay = await c.HandoverAsync(request);
        Assert.True(replay.Replayed); Assert.Equal(first.AppliedVersion, replay.AppliedVersion);
        Assert.Equal(first.RecordedAtUtc, replay.RecordedAtUtc); Assert.Equal(first.Orders, replay.Orders);
        Assert.Equal(before, await c.SnapshotAsync());
        var wrong = request with { ConfirmationNote = "Different physical attestation" };
        var conflict = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.HandoverAsync(wrong));
        Assert.Equal(409, conflict.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task T06_Ordered_route_payload_is_not_an_unordered_set_and_reorder_insert_remove_keeps_unique_positions()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); var createKey = Guid.NewGuid();
        var original = await c.SelectionsAsync(); await c.CreateDraftAsync(createKey);
        await using (var db = c.Context())
        {
            var before = await c.SnapshotAsync();
            var error = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.Service(db).CreateDraftAsync(
                new(createKey, c.Courier.UserId, original.Reverse().ToArray(), "D06 actual multi-order route")));
            Assert.Equal(409, error.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
        }
        var inventory = await c.StockAsync(); var version = (await c.DetailAsync()).Version;
        var edit = new DeliveryTripEditDraftRequest(Guid.NewGuid(), version, c.Courier.UserId,
            original.Reverse().ToArray(), "Đảo thứ tự điểm giao");
        await using (var db = c.Context()) await c.Service(db).EditDraftAsync(c.TripId, edit);
        var reversed = await c.DetailAsync();
        Assert.Equal(c.Orders.Select(x => x.Id).Reverse(), reversed.Orders.Where(x => x.IsActive).OrderBy(x => x.Position).Select(x => x.DeliveryOrderId));
        Assert.Equal(new[] { 1, 2 }, reversed.Orders.Where(x => x.IsActive).OrderBy(x => x.Position).Select(x => x.Position));
        Assert.Equal(inventory, await c.StockAsync());
        var editAfter = await c.SnapshotAsync();
        await using (var db = c.Context()) Assert.True((await c.Service(db).EditDraftAsync(c.TripId, edit)).Replayed);
        Assert.Equal(editAfter, await c.SnapshotAsync());
        await using (var db = c.Context())
        {
            var changed = edit with { Orders = original };
            var conflict = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.Service(db).EditDraftAsync(c.TripId, changed));
            Assert.Equal("REQUEST_KEY_CONFLICT", conflict.Code); Assert.Equal(editAfter, await c.SnapshotAsync());
        }
        using var spare = await DeliveryD06Order.CreateAsync(fixture, quantity: 1, distinctSource: true); await spare.ReadyAsync();
        var spareSelection = new DeliveryTripOrderSelection(spare.Id, (await spare.Picking.GetAsync()).Delivery.Version);
        var beforeReplacement = await c.StockAsync();
        await using (var db = c.Context()) await c.Service(db).EditDraftAsync(c.TripId,
            new(Guid.NewGuid(), reversed.Version, c.Courier.UserId, [spareSelection, original[0]], "Thay điểm đầu và bỏ điểm cũ"));
        var replaced = await c.DetailAsync();
        Assert.Equal(new[] { spare.Id, c.Orders[0].Id }, replaced.Orders.Where(x => x.IsActive).OrderBy(x => x.Position).Select(x => x.DeliveryOrderId));
        Assert.Equal(new[] { 1, 2 }, replaced.Orders.Where(x => x.IsActive).OrderBy(x => x.Position).Select(x => x.Position));
        Assert.Equal(beforeReplacement, await c.StockAsync());
        await using var verify = c.Context();
        Assert.False((await verify.DeliveryTripOrders.SingleAsync(x => x.TripId == c.TripId && x.DeliveryOrderId == c.Orders[1].Id)).IsActive);
        var removalHistory = await verify.DeliveryTripRevisions.AsNoTracking().SingleAsync(x => x.TripId == c.TripId && x.Revision == replaced.Revision);
        using var removedDocument = JsonDocument.Parse(removalHistory.SnapshotJson);
        Assert.False(Assert.Single(removedDocument.RootElement.GetProperty("bindings").EnumerateArray(),
            x => x.GetProperty("deliveryOrderId").GetInt32() == c.Orders[1].Id).GetProperty("isActive").GetBoolean());
        await using (var db = c.Context()) await c.Service(db).EditDraftAsync(c.TripId,
            new(Guid.NewGuid(), replaced.Version, c.Courier.UserId, [original[1], original[0]], "Re-add the removed order without duplicate display identity"));
        var readded = await c.DetailAsync();
        Assert.Equal(new[] { c.Orders[1].Id, c.Orders[0].Id }, readded.Orders.Where(x => x.IsActive).OrderBy(x => x.Position).Select(x => x.DeliveryOrderId));
        Assert.Equal(new[] { 1, 2 }, readded.Orders.Where(x => x.IsActive).OrderBy(x => x.Position).Select(x => x.Position));
        Assert.Equal(readded.Orders.Count, readded.Orders.Select(x => x.DeliveryOrderId).Distinct().Count());
        Assert.Single(readded.Orders, x => x.DeliveryOrderId == c.Orders[1].Id);
        Assert.Single(await verify.DeliveryTripOrders.AsNoTracking().Where(x => x.TripId == c.TripId && x.DeliveryOrderId == c.Orders[1].Id && x.IsActive).ToArrayAsync());
        Assert.Equal(removalHistory.SnapshotJson, (await verify.DeliveryTripRevisions.AsNoTracking().SingleAsync(x => x.Id == removalHistory.Id)).SnapshotJson);
        var restorationHistory = await verify.DeliveryTripRevisions.AsNoTracking().SingleAsync(x => x.TripId == c.TripId && x.Revision == readded.Revision);
        using var restoredDocument = JsonDocument.Parse(restorationHistory.SnapshotJson);
        var restoredBinding = Assert.Single(restoredDocument.RootElement.GetProperty("bindings").EnumerateArray(),
            x => x.GetProperty("deliveryOrderId").GetInt32() == c.Orders[1].Id && x.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, restoredBinding.GetProperty("position").GetInt32());
        Assert.Equal(beforeReplacement, await c.StockAsync());
    }

    [Fact]
    public async Task T07_Physical_whole_trip_transfer_confirms_only_remaining_custody_after_actual_accepted_returns()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync(); await c.HandoverAsync();
        await c.Orders[1].Inventory.ReceiveAsync("1");
        var before = await c.InventoryOnlyAsync(); var detail = await c.DetailAsync();
        await using (var db = c.Context()) await c.Service(db).ReassignCourierAsync(c.TripId,
            new(Guid.NewGuid(), detail.Version, await c.SelectionsAsync(), c.OtherCourier.UserId,
                c.OtherCourier.UserId, true, "Bàn giao toàn chuyến trước đi", "Người giao mới đã nhận hàng"));
        Assert.Equal(before, await c.InventoryOnlyAsync());
        await using var verify = c.Context();
        var events = await verify.DeliveryCustodyEvents.Where(x => x.TripId == c.TripId).ToArrayAsync();
        Assert.Equal(4, events.Length);
        foreach (var item in c.Orders)
        {
            var first = Assert.Single(events, x => x.DeliveryOrderId == item.Id && x.Kind == DeliveryCustodyKind.Handover);
            var next = Assert.Single(events, x => x.DeliveryOrderId == item.Id && x.Kind == DeliveryCustodyKind.Transfer);
            Assert.Equal(first.Id, next.PreviousCustodyEventId); Assert.Equal(c.Courier.UserId, next.PreviousCourierUserId);
            Assert.Equal(c.OtherCourier.UserId, next.CourierUserId); Assert.Equal(c.Actor.UserId, next.ActorUserId);
            var line = Assert.Single(await verify.DeliveryCustodyLines.Where(x => x.CustodyEventId == next.Id).ToArrayAsync());
            Assert.Equal(2m, line.ConfirmedBaseQuantity);
            Assert.Equal(item == c.Orders[1] ? 1m : 0m, line.CumulativeAcceptedReturnAtConfirmation);
            var summary = await item.Inventory.GetAsync(); Assert.Null(summary.InCustodyBaseQuantityText);
            Assert.Equal("2", Assert.Single(summary.Lines, x => x.LineId == line.DeliveryOrderLineId).InCustodyBaseQuantityText);
            Assert.Equal(c.OtherCourier.UserId, summary.Custody!.CourierUserId);
        }
    }

    [Fact]
    public async Task T08_Departure_records_server_time_once_and_blocks_wholetrip_transfer_after_departure()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync(); await c.HandoverAsync();
        var request = new DeliveryTripDepartureRequest(Guid.NewGuid(), (await c.DetailAsync()).Version, await c.SelectionsAsync());
        var beforeStock = await c.InventoryOnlyAsync(); var beforeUtc = DateTimeOffset.UtcNow;
        var ack = await c.DepartAsync(request); var detail = await c.DetailAsync();
        Assert.InRange(detail.DepartedAtUtc!.Value, beforeUtc, DateTimeOffset.UtcNow);
        Assert.Equal(c.Actor.UserId, detail.DepartedByUserId); Assert.Equal(c.Courier.UserId, detail.CourierUserId);
        Assert.Equal(nameof(DeliveryTripState.Departed), detail.State); Assert.Equal(beforeStock, await c.InventoryOnlyAsync());
        var before = await c.SnapshotAsync(); var replay = await c.DepartAsync(request);
        Assert.True(replay.Replayed); Assert.Equal(ack.AppliedVersion, replay.AppliedVersion); Assert.Equal(ack.RecordedAtUtc, replay.RecordedAtUtc);
        await using var db = c.Context();
        var denied = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.Service(db).ReassignCourierAsync(c.TripId,
            new(Guid.NewGuid(), detail.Version, detail.Orders.Select(x => new DeliveryTripOrderSelection(x.DeliveryOrderId, x.Version)).ToArray(),
                c.OtherCourier.UserId, c.OtherCourier.UserId, true, "Không được đổi trực tiếp khi đã đi")));
        Assert.Equal(409, denied.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task T09_Per_order_recall_before_or_after_departure_preserves_stock_custody_and_other_orders(bool departed)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync(); await c.HandoverAsync();
        if (departed) await c.DepartAsync();
        var beforeInventory = await c.InventoryOnlyAsync(); var trip = await c.DetailAsync();
        var other = trip.Orders.Single(x => x.DeliveryOrderId == c.Orders[1].Id);
        var request = new DeliveryTripCancellationRequest(Guid.NewGuid(), trip.Version,
            trip.Orders.Single(x => x.DeliveryOrderId == c.Orders[0].Id).Version, "Thu hồi riêng điểm đầu");
        await using (var db = c.Context()) await c.Service(db).RequestCancellationAsync(c.TripId, c.Orders[0].Id, request);
        Assert.Equal(beforeInventory, await c.InventoryOnlyAsync()); var after = await c.DetailAsync();
        Assert.Equal(trip.State, after.State); Assert.Equal(trip.CourierUserId, after.CourierUserId);
        Assert.Equal(nameof(DeliveryState.CancellationPending), after.Orders.Single(x => x.DeliveryOrderId == c.Orders[0].Id).State);
        Assert.Equal(other.Version, after.Orders.Single(x => x.DeliveryOrderId == c.Orders[1].Id).Version);
        Assert.Equal(other.State, after.Orders.Single(x => x.DeliveryOrderId == c.Orders[1].Id).State);
        Assert.All(after.Orders, x => Assert.NotNull(x.Custody));
        var snapshot = await c.SnapshotAsync();
        await using (var db = c.Context()) Assert.True((await c.Service(db).RequestCancellationAsync(c.TripId, c.Orders[0].Id, request)).Replayed);
        Assert.Equal(snapshot, await c.SnapshotAsync());
        if (!departed)
        {
            var denied = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.DepartAsync());
            Assert.Equal(409, denied.StatusCode); Assert.Equal(snapshot, await c.SnapshotAsync());
        }
    }

    [Fact]
    public async Task T10_D05_export_without_D06_custody_stays_unknown_and_is_not_adopted_or_exported_again()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture, count: 1);
        await c.Orders[0].Inventory.DispatchAsync(); var inventory = await c.Orders[0].Inventory.GetAsync();
        Assert.Null(inventory.Custody); Assert.Null(inventory.InCustodyBaseQuantityText);
        var before = await c.SnapshotAsync();
        await using var db = c.Context(); var options = await c.Service(db).GetOptionsAsync();
        Assert.DoesNotContain(options.Orders, x => x.DeliveryOrderId == c.Orders[0].Id);
        var denied = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.CreateDraftAsync());
        Assert.Equal(409, denied.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task T11_Two_SQL_workers_competing_for_one_draft_binding_have_one_owner_and_no_stock_effects()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture, count: 1); var selections = await c.SelectionsAsync();
        var before = await c.StockAsync();
        await using var dbA = c.Context(); await using var dbB = c.Context();
        await dbA.Database.OpenConnectionAsync(); await dbB.Database.OpenConnectionAsync();
        var workers = new[] { await Worker(dbA), await Worker(dbB) };
        async Task<Exception?> Create(AppDbContext db, int courier)
        {
            try { await c.Service(db).CreateDraftAsync(new(Guid.NewGuid(), courier, selections)); return null; }
            catch (DeliveryFoundationException ex) { return ex; }
        }
        var result = await ApplicationGate<Exception?>(c, "GaoApp:Delivery:order:" + c.Actor.Store.StoreId + ":" + c.Orders[0].Id,
            workers, () => [Create(dbA, c.Courier.UserId), Create(dbB, c.OtherCourier.UserId)], "D06_SQL_ASSIGNMENT_BARRIER_PROOF");
        Assert.Single(result, x => x is null); Assert.Equal(409, Assert.IsType<DeliveryFoundationException>(Assert.Single(result, x => x is not null)).StatusCode);
        Assert.Equal(before, await c.StockAsync());
        await using var verify = c.Context(); Assert.Single(await verify.DeliveryTripOrders.Where(x => x.DeliveryOrderId == c.Orders[0].Id && x.IsActive).ToArrayAsync());
        Assert.Empty(await verify.DeliveryCustodyEvents.Where(x => x.DeliveryOrderId == c.Orders[0].Id).ToArrayAsync());
    }

    [Fact]
    public async Task T12_Same_handover_command_two_SQL_workers_observably_wait_then_share_one_export_and_ack()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync(); var request = await c.HandoverRequestAsync();
        await using var dbA = c.Context(); await using var dbB = c.Context();
        await dbA.Database.OpenConnectionAsync(); await dbB.Database.OpenConnectionAsync();
        var workers = new[] { await Worker(dbA), await Worker(dbB) };
        var results = await ApplicationGate<DeliveryTripCommandAck>(c, "GaoApp:Delivery:order:" + c.Actor.Store.StoreId + ":" + c.Orders[0].Id,
            workers, () => [c.Service(dbA).HandoverAsync(c.TripId, request), c.Service(dbB).HandoverAsync(c.TripId, request)],
            "D06_SQL_HANDOVER_REPLAY_BARRIER_PROOF");
        Assert.Single(results, x => !x.Replayed); Assert.Single(results, x => x.Replayed);
        Assert.Equal(results[0].AppliedVersion, results[1].AppliedVersion);
        await using var verify = c.Context();
        Assert.Equal(2, await verify.DeliveryCustodyEvents.CountAsync(x => x.TripId == c.TripId));
        Assert.Equal(2, await verify.DeliveryJournalEntries.CountAsync(x => c.Orders.Select(o => o.Id).Contains(x.DeliveryOrderId)));
    }

    [Fact]
    public async Task T13_Opposite_routes_prelock_the_same_complete_balance_union_before_either_multiorder_export()
    {
        using var first = await DeliveryD06Case.CreateAsync(fixture);
        var a = first.Orders[0]; var b = first.Orders[1];
        await a.AddStockAsync(2, 9000); await b.AddStockAsync(3, 11000);
        var variantA = await a.Inventory.VariantAsync(); var variantB = await b.Inventory.VariantAsync();
        var sharedA = await DeliveryD06Order.CreateAsync(fixture, quantity: 2, sharedVariant: variantA,
            sourceWarehouseId: a.Account.Store.WarehouseId);
        var sharedB = await DeliveryD06Order.CreateAsync(fixture, quantity: 3, sharedVariant: variantB,
            sourceWarehouseId: b.Account.Store.WarehouseId);
        await sharedA.ReadyAsync(); await sharedB.ReadyAsync();
        using var second = new DeliveryD06Case(fixture.Web, [sharedB, sharedA], first.Courier, first.OtherCourier);
        await first.CreateDraftAsync(); await second.CreateDraftAsync();
        var requestA = await first.HandoverRequestAsync(); var requestB = await second.HandoverRequestAsync();
        await using var dbA = first.Context(); await using var dbB = second.Context();
        await dbA.Database.OpenConnectionAsync(); await dbB.Database.OpenConnectionAsync();
        var workers = new[] { await Worker(dbA), await Worker(dbB) };
        var balance = await dbA.InventoryBalances.AsNoTracking().SingleAsync(x => x.WarehouseId == a.Account.Store.WarehouseId && x.ProductVariantId == variantA);
        Assert.True(a.Account.Store.WarehouseId < b.Account.Store.WarehouseId);
        var finance = await a.Inventory.FinanceAsync();
        var result = await BalanceGate(first, balance.Id, a.Account.Store.WarehouseId, variantA, workers,
            () => [first.Service(dbA).HandoverAsync(first.TripId, requestA), second.Service(dbB).HandoverAsync(second.TripId, requestB)]);
        Assert.All(result, x => { Assert.False(x.Replayed); Assert.Equal(2, x.Orders.Count); });
        await using var verify = first.Context();
        var ids = first.Orders.Concat(second.Orders).Select(x => x.Id).ToArray();
        Assert.Equal(4, await verify.DeliveryJournalEntries.CountAsync(x => ids.Contains(x.DeliveryOrderId)));
        Assert.Equal(4, await verify.DeliveryCustodyEvents.CountAsync(x => ids.Contains(x.DeliveryOrderId)));
        Assert.All(await verify.InventoryBalances.Where(x => x.Id == balance.Id ||
            x.WarehouseId == b.Account.Store.WarehouseId && x.ProductVariantId == variantB).ToArrayAsync(),
            x => { Assert.Equal(0m, x.OnHandQty); Assert.Equal(0m, x.ReservedQty); Assert.Equal(0m, x.InventoryValue); });
        Assert.Equal(finance, await a.Inventory.FinanceAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task T14_Root_or_derived_child_owned_by_a_real_order_command_cannot_be_adopted_for_handover(bool child)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        Guid root;
        if (child)
        {
            root = Guid.NewGuid(); var order = c.Orders[0]; var current = await order.Picking.GetAsync();
            await order.Picking.CommandAsync("reopen", new DeliveryPickingReopenRequest(
                DeliveryTripPolicy.ChildCommandId(root, order.Id), current.Delivery.Version, "A real earlier order command owns the child key"));
            await order.Picking.ReportAsync("2"); var restored = await order.Picking.SubmitAsync();
            if (restored.Delivery.State == nameof(DeliveryState.AwaitingApproval)) await order.Picking.ApproveAsync();
            await using var refresh = c.Context();
            await c.Service(refresh).EditDraftAsync(c.TripId,
                new(Guid.NewGuid(), (await c.DetailAsync()).Version, c.Courier.UserId, await c.SelectionsAsync(), "Refresh after genuine re-picking"));
        }
        else
        {
            await using var read = c.Context();
            root = await read.DeliveryCommandReceipts.Where(x => x.DeliveryOrderId == c.Orders[0].Id)
                .OrderBy(x => x.Id).Select(x => x.ClientRequestId).FirstAsync();
        }
        var request = await c.HandoverRequestAsync(root); var before = await c.SnapshotAsync();
        var error = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.HandoverAsync(request));
        Assert.Equal(409, error.StatusCode); Assert.Equal("REQUEST_KEY_CONFLICT", error.Code);
        Assert.Equal(before, await c.SnapshotAsync());
        await using var verify = c.Context();
        Assert.Empty(await verify.DeliveryCustodyEvents.Where(x => x.TripId == c.TripId).ToArrayAsync());
        Assert.Empty(await verify.DeliveryJournalEntries.Where(x => c.Orders.Select(o => o.Id).Contains(x.DeliveryOrderId)).ToArrayAsync());
    }

    [Fact]
    public async Task T15_Transfer_and_departure_two_SQL_workers_have_one_revision_winner_and_preserve_every_export()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync(); await c.HandoverAsync();
        var detail = await c.DetailAsync(); var selections = await c.SelectionsAsync();
        var depart = new DeliveryTripDepartureRequest(Guid.NewGuid(), detail.Version, selections);
        var transfer = new DeliveryTripReassignRequest(Guid.NewGuid(), detail.Version, selections,
            c.OtherCourier.UserId, c.OtherCourier.UserId, true, "Competing genuine physical transfer");
        await using var dbA = c.Context(); await using var dbB = c.Context();
        await dbA.Database.OpenConnectionAsync(); await dbB.Database.OpenConnectionAsync();
        var workers = new[] { await Worker(dbA), await Worker(dbB) }; var stock = await c.InventoryOnlyAsync();
        async Task<(DeliveryTripCommandAck? Ack, DeliveryFoundationException? Error)> Run(Func<Task<DeliveryTripCommandAck>> command)
        {
            try { return (await command(), null); } catch (DeliveryFoundationException ex) { return (null, ex); }
        }
        var result = await ApplicationGate<(DeliveryTripCommandAck? Ack, DeliveryFoundationException? Error)>(c, "GaoApp:Delivery:trip:" + c.Actor.Store.StoreId + ":" + c.TripId,
            workers, () => [Run(() => c.Service(dbA).DepartAsync(c.TripId, depart)), Run(() => c.Service(dbB).ReassignCourierAsync(c.TripId, transfer))],
            "D06_SQL_TRANSFER_DEPARTURE_BARRIER_PROOF");
        var winner = Assert.Single(result, x => x.Ack is not null).Ack!;
        Assert.Equal(409, Assert.Single(result, x => x.Error is not null).Error!.StatusCode);
        Assert.Equal(stock, await c.InventoryOnlyAsync()); var after = await c.DetailAsync();
        Assert.Equal(detail.Revision + 1, after.Revision);
        Assert.Equal(winner.Operation == "depart" ? nameof(DeliveryTripState.Departed) : nameof(DeliveryTripState.HandedOver), after.State);
        Assert.Equal(winner.Operation == "depart" ? c.Courier.UserId : c.OtherCourier.UserId, after.CourierUserId);
        var beforeReplay = await c.SnapshotAsync(); await using var replayDb = c.Context();
        var replay = winner.Operation == "depart" ? await c.Service(replayDb).DepartAsync(c.TripId, depart)
            : await c.Service(replayDb).ReassignCourierAsync(c.TripId, transfer);
        Assert.True(replay.Replayed); Assert.Equal(winner.AppliedVersion, replay.AppliedVersion);
        Assert.Equal(beforeReplay, await c.SnapshotAsync());
    }

    [Fact]
    public async Task T16_Removed_order_handed_to_a_new_trip_never_leaks_that_custody_into_the_old_inactive_binding()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        using var spare = await DeliveryD06Order.CreateAsync(fixture, quantity: 1, distinctSource: true); await spare.ReadyAsync();
        var primary = await c.SelectionsAsync(); var version = (await c.DetailAsync()).Version;
        await using (var db = c.Context()) await c.Service(db).EditDraftAsync(c.TripId,
            new(Guid.NewGuid(), version, c.Courier.UserId,
                [primary[0], new(spare.Id, (await spare.Picking.GetAsync()).Delivery.Version)], "Move the removed order to another trip"));
        using var next = new DeliveryD06Case(fixture.Web, [c.Orders[1]], c.OtherCourier, c.Courier);
        await next.CreateDraftAsync(); await next.HandoverAsync();
        var old = await c.DetailAsync(); var inactive = Assert.Single(old.Orders, x => x.DeliveryOrderId == c.Orders[1].Id);
        Assert.False(inactive.IsActive); Assert.Null(inactive.Custody); Assert.False(inactive.CanRequestCancellation);
        Assert.All(inactive.Lines, x => Assert.Null(x.InCustodyBaseQuantityText));
        var actual = await c.Orders[1].Inventory.GetAsync(); Assert.NotNull(actual.Custody);
        Assert.Equal(next.TripId, actual.Custody.TripId); Assert.Equal(c.OtherCourier.UserId, actual.Custody.CourierUserId);
        Assert.Equal("3", Assert.Single(actual.Lines).InCustodyBaseQuantityText);
        Assert.All(old.Orders.Where(x => x.IsActive), x => Assert.Null(x.Custody));
        await using var verify = c.Context();
        Assert.Single(await verify.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == c.Orders[1].Id).ToArrayAsync());
        Assert.Empty(await verify.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == c.Orders[0].Id || x.DeliveryOrderId == spare.Id).ToArrayAsync());
        Assert.Equal("2", (await c.Orders[0].Inventory.GetAsync()).HeldBaseQuantityText);
        Assert.Equal("1", (await spare.Inventory.GetAsync()).HeldBaseQuantityText);
    }

    private async Task<DeliveryTripCommandAck[]> BalanceGate(DeliveryD06Case c, int balanceId, int warehouseId, int variantId, int[] workers,
        Func<Task<DeliveryTripCommandAck>[]> start)
    {
        Assert.Equal(2, workers.Distinct().Count()); Assert.All(workers, x => Assert.True(x > 0));
        await using var gate = LocalDbSqlConnectionFactory.Create(c.Web.Database.ConnectionString);
        await gate.OpenAsync(); Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", gate.Database);
        await using var tx = (SqlTransaction)await gate.BeginTransactionAsync();
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = tx; command.CommandText = "SELECT TOP (1) Id FROM InventoryBalances WITH (UPDLOCK,HOLDLOCK,ROWLOCK) WHERE StoreId=@storeId AND WarehouseId=@warehouseId AND ProductVariantId=@productVariantId";
            command.Parameters.AddWithValue("@storeId", c.Actor.Store.StoreId); command.Parameters.AddWithValue("@warehouseId", warehouseId);
            command.Parameters.AddWithValue("@productVariantId", variantId); Assert.Equal(balanceId, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        var pending = start(); var observed = false; var paths = Array.Empty<int[]>();
        var statements = Array.Empty<object>(); var lockRows = Array.Empty<object>();
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(8))
            {
                await using var command = gate.CreateCommand(); command.Transaction = tx;
                command.CommandText = """
                    SELECT r.session_id,r.blocking_session_id,r.wait_type,
                      SUBSTRING(t.text,(r.statement_start_offset/2)+1,((CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(t.text) ELSE r.statement_end_offset END-r.statement_start_offset)/2)+1)
                    FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
                    WHERE r.database_id=DB_ID() AND r.session_id<>@@SPID;
                    SELECT l.request_session_id,l.request_mode,l.request_status,l.resource_description
                    FROM sys.dm_tran_locks l JOIN sys.partitions p ON p.hobt_id=l.resource_associated_entity_id
                    WHERE l.resource_database_id=DB_ID() AND l.resource_type='KEY' AND p.object_id=OBJECT_ID(N'dbo.InventoryBalances');
                    """;
                var requests = new List<(int Id, int Blocker, string? Wait, string Statement)>();
                var locks = new List<(int Id, string Mode, string Status, string Resource)>();
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) requests.Add((reader.GetInt16(0), reader.GetInt16(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
                    await reader.NextResultAsync();
                    while (await reader.ReadAsync()) locks.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
                }
                int[] Path(int id)
                {
                    var path = new List<int>(); var seen = new HashSet<int>();
                    for (var depth = 0; depth < 32 && seen.Add(id); depth++)
                    {
                        path.Add(id); if (id == gate.ServerProcessId) return path.ToArray();
                        var row = requests.SingleOrDefault(x => x.Id == id); if (row.Blocker <= 0) break; id = row.Blocker;
                    }
                    return [];
                }
                paths = workers.Select(Path).Where(x => x.Length > 1).ToArray();
                var held = locks.Where(x => x.Id == gate.ServerProcessId && x.Mode == "U" && x.Status == "GRANT").Select(x => x.Resource).ToHashSet();
                observed = paths.Length == 2 && pending.All(x => !x.IsCompleted) && held.Count > 0 && workers.All(id =>
                    requests.Any(x => x.Id == id && x.Wait == "LCK_M_U" && x.Statement.Contains("FROM InventoryBalances WITH (UPDLOCK,HOLDLOCK,ROWLOCK)", StringComparison.Ordinal)) &&
                    locks.Any(x => x.Id == id && x.Mode == "U" && x.Status == "WAIT" && held.Contains(x.Resource))) &&
                    !locks.Any(x => workers.Contains(x.Id) && x.Mode == "U" && x.Status == "GRANT");
                statements = requests.Where(x => workers.Contains(x.Id)).Select(x => (object)new { SessionId = x.Id, x.Blocker, x.Wait, x.Statement }).ToArray();
                lockRows = locks.Where(x => workers.Contains(x.Id) || x.Id == gate.ServerProcessId).Select(x => (object)new { SessionId = x.Id, x.Mode, x.Status, x.Resource }).ToArray();
                if (observed) break; await Task.Delay(25);
            }
            output.WriteLine("D06_SQL_GLOBAL_BALANCE_UNION_BARRIER_PROOF " + JsonSerializer.Serialize(new {
                OwnedDatabase = gate.Database, BalanceId = balanceId, GateSessionId = gate.ServerProcessId,
                DistinctWorkerSessionIds = workers, AcyclicGatePaths = paths, BothPendingBeforeRelease = pending.All(x => !x.IsCompleted),
                BothWorkersAtFirstUnionBalanceWithNoHigherBalanceGrant = observed, ActualStatements = statements, BalanceKeyLocks = lockRows }));
        }
        finally { await tx.RollbackAsync(); }
        var result = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(35)); Assert.True(observed); return result;
    }

    private static async Task<int> Worker(AppDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT @@SPID";
        var id = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(id > 0); return id;
    }

    private async Task<T[]> ApplicationGate<T>(DeliveryD06Case c, string resource, int[] workers,
        Func<Task<T>[]> start, string marker)
    {
        Assert.Equal(2, workers.Distinct().Count()); Assert.All(workers, x => Assert.True(x > 0));
        await using var gate = LocalDbSqlConnectionFactory.Create(c.Web.Database.ConnectionString);
        await gate.OpenAsync(); Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", gate.Database);
        await using var transaction = (SqlTransaction)await gate.BeginTransactionAsync();
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=1000; SELECT @r;";
            command.Parameters.AddWithValue("@resource", resource); Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 0);
        }
        string physicalResource;
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = "SELECT resource_description FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION' AND request_session_id=@@SPID AND request_mode='X' AND request_status='GRANT'";
            physicalResource = Assert.IsType<string>(await command.ExecuteScalarAsync());
        }
        var pending = start(); var paths = Array.Empty<int[]>(); var held = false; var waiting = false; var bothPending = false;
        var observedLocks = Array.Empty<object>();
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(8))
            {
                await using var command = gate.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "SELECT session_id,blocking_session_id,wait_type FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND session_id<>@@SPID; SELECT request_session_id,request_mode,request_status,resource_description FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION';";
                var requests = new List<(int Id, int Blocker, string? Wait)>();
                var locks = new List<(int Id, string Mode, string Status, string Resource)>();
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) requests.Add((reader.GetInt16(0), reader.GetInt16(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                    await reader.NextResultAsync();
                    while (await reader.ReadAsync()) locks.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
                }
                int[] Path(int id)
                {
                    var path = new List<int>(); var seen = new HashSet<int>();
                    for (var depth = 0; depth < 32 && seen.Add(id); depth++)
                    {
                        path.Add(id); if (id == gate.ServerProcessId) return path.ToArray();
                        var request = requests.SingleOrDefault(x => x.Id == id); if (request.Blocker <= 0) break; id = request.Blocker;
                    }
                    return [];
                }
                paths = workers.Select(Path).Where(x => x.Length > 1).ToArray();
                held = locks.Count(x => x.Id == gate.ServerProcessId && x.Mode == "X" && x.Status == "GRANT" && x.Resource == physicalResource) == 1;
                // The second replay can wait on the first worker's command lock,
                // while the first waits on the exact held order lock. Both must
                // be actual application-lock waiters in that gate's ancestry.
                waiting = workers.All(id => requests.Any(x => x.Id == id && x.Wait == "LCK_M_X") &&
                    locks.Any(x => x.Id == id && x.Mode == "X" && x.Status is "WAIT" or "CONVERT"));
                bothPending = pending.Length == 2 && pending.All(x => !x.IsCompleted);
                observedLocks = locks.Where(x => workers.Contains(x.Id) && x.Status is "WAIT" or "CONVERT")
                    .Select(x => (object)new { SessionId = x.Id, x.Mode, x.Status, x.Resource }).ToArray();
                if (held && waiting && bothPending && paths.Length == 2) break;
                await Task.Delay(25);
            }
            output.WriteLine(marker + " " + JsonSerializer.Serialize(new {
                OwnedDatabase = gate.Database, GateSessionId = gate.ServerProcessId, RequestedResource = resource,
                ExactGateApplicationResource = physicalResource, DistinctWorkerSessionIds = workers,
                AcyclicGatePaths = paths, GateHeldBeforeRelease = held, BothActualSqlWorkersWaiting = waiting,
                BothTasksPendingBeforeRelease = bothPending, WaitingApplicationLocks = observedLocks }));
        }
        finally { await transaction.RollbackAsync(); }
        var results = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(35));
        Assert.Equal(2, paths.Length); Assert.True(held); Assert.True(waiting); Assert.True(bothPending); return results;
    }
}

internal sealed class D06InjectedFailure : Exception;
internal sealed class D06SecondExportFailure(int secondOrderId) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<DeliveryJournalEntry>()
            .Any(x => x.State == EntityState.Added && x.Entity.DeliveryOrderId == secondOrderId))
            throw new D06InjectedFailure();
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
