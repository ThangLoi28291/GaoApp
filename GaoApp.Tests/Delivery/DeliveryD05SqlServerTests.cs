using System.Diagnostics;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD05"), Trait("Category", "DeliveryD05")]
public sealed class DeliveryD05SqlServerTests(DeliveryD02Fixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task T01_Claim_is_zero_hold_then_report_replaces_generation_and_cancel_releases_without_stock_or_finance()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 12, stock: [(12m, 10m)]);
        var finance = await c.FinanceAsync(); var physical = await Physical(c);
        await c.Picking.ClaimAsync();
        Assert.Equal("0", (await c.GetAsync()).HeldBaseQuantityText);
        await using (var db = c.Picking.Source.Context())
            Assert.Empty(await db.InventoryReservations.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
        await c.Picking.ReportAsync("10", "Thiếu 2 thực tế");
        Assert.Equal("10", (await c.GetAsync()).HeldBaseQuantityText);
        await c.Picking.ReportAsync("8", "Chỉ còn 8 thực tế");
        Assert.Equal("8", (await c.GetAsync()).HeldBaseQuantityText);
        await using (var db = c.Picking.Source.Context())
        {
            var rows = await db.InventoryReservations.Where(x => x.DeliveryOrderId == c.Id).OrderBy(x => x.Id).ToArrayAsync();
            Assert.Equal(2, rows.Length); Assert.Equal(10m, rows[0].ReservedQty); Assert.Equal(InventoryReservationStatus.Released, rows[0].Status);
            Assert.Equal(8m, rows[1].ReservedQty); Assert.Equal(InventoryReservationStatus.Active, rows[1].Status);
        }
        var detail = await c.Picking.GetAsync();
        await using (var db = c.Picking.Source.Context())
            await c.Service(db).CancelBeforeHandoverAsync(c.Id, new(Guid.NewGuid(), detail.Delivery.Version, "Khách hủy"));
        Assert.Equal("0", (await c.GetAsync()).HeldBaseQuantityText);
        Assert.Equal(physical, await Physical(c)); Assert.Equal(finance, await c.FinanceAsync());
    }

    [Fact]
    public async Task T02_Two_actual_SQL_workers_compete_for_last_stock_and_only_one_saved_report_has_a_hold()
    {
        using var first = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: [(1m, 10m)]);
        var variant = await first.VariantAsync();
        using var second = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: [], sharedVariant: variant);
        var a = await first.Picking.ClaimAsync(); var b = await second.Picking.ClaimAsync();
        await using var dbA = first.Picking.Source.Context(); await using var dbB = second.Picking.Source.Context();
        await dbA.Database.OpenConnectionAsync(); await dbB.Database.OpenConnectionAsync();
        var workers = new[] { await WorkerSessionId(dbA), await WorkerSessionId(dbB) };
        Assert.Equal(2, workers.Distinct().Count());
        async Task<Exception?> Save(DeliveryD05Case item, GaoApp.Infrastructure.Data.AppDbContext db, DeliveryPickingDetailDto detail)
        {
            try
            {
                await item.Picking.Service(db).ReportAsync(item.Id, new(Guid.NewGuid(), detail.Delivery.Version,
                    [new(Assert.Single(detail.Lines).LineId, "1", null)]));
                return null;
            }
            catch (DeliveryFoundationException ex) { return ex; }
            catch (BusinessRuleException ex) { return ex; }
        }
        var results = await WithBalanceGate<Exception?>(first, variant, workers, () => [Save(first, dbA, a), Save(second, dbB, b)]);
        Assert.Single(results, x => x is null);
        var error = Assert.Single(results, x => x is not null);
        Assert.Equal("INSUFFICIENT_STOCK", Assert.IsType<DeliveryFoundationException>(error).Code);
        await using var verify = first.Picking.Source.Context();
        var balance = await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant && x.WarehouseId == first.Picking.Account.Store.WarehouseId);
        Assert.Equal(1m, balance.OnHandQty); Assert.Equal(1m, balance.ReservedQty);
        var holds = await verify.InventoryReservations.Where(x => x.DeliveryOrderId == first.Id || x.DeliveryOrderId == second.Id).ToArrayAsync();
        Assert.Single(holds, x => x.Status == InventoryReservationStatus.Active);
        Assert.Equal(5, await verify.DeliveryCommandReceipts.CountAsync(x => x.DeliveryOrderId == first.Id || x.DeliveryOrderId == second.Id));
        var reports = await verify.DeliveryPickingLines.Where(x => x.DeliveryOrderId == first.Id || x.DeliveryOrderId == second.Id).ToArrayAsync();
        Assert.Single(reports, x => x.ReportedQuantity == 1); Assert.Single(reports, x => x.ReportedQuantity is null);
    }

    [Fact]
    public async Task T03_Approved_eight_from_reported_ten_releases_two_then_dispatches_FIFOpieces_without_sale_or_finance()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 12, stock: [(4m, 9000m), (4m, 11000m), (92m, 13000m)]);
        var finance = await c.FinanceAsync();
        await c.Picking.ClaimAsync(); await c.Picking.ReportAsync("10", "Thiếu 2");
        var detail = await c.Picking.SubmitAsync();
        Assert.Equal(nameof(DeliveryState.AwaitingApproval), detail.Delivery.State);
        await c.Picking.CommandAsync("approve", new DeliveryPickingApproveRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(Assert.Single(detail.Lines).LineId, "8", null)], "Khách chỉ nhận 8", "Khách đồng ý"));
        Assert.Equal("10", (await c.GetAsync()).HeldBaseQuantityText);
        await using (var db = c.Picking.Source.Context())
        {
            var holds = await db.InventoryReservations.Where(x => x.DeliveryOrderId == c.Id).OrderBy(x => x.Id).ToArrayAsync();
            Assert.Equal(10m, Assert.Single(holds, x => x.Status == InventoryReservationStatus.Active).ReservedQty);
        }
        var ack = await c.DispatchAsync(); Assert.False(ack.Replayed);
        await using (var db = c.Picking.Source.Context())
        {
            var variant = await c.VariantAsync();
            var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal(92m, balance.OnHandQty); Assert.Equal(0m, balance.ReservedQty);
            var journal = Assert.Single(await db.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
            Assert.Equal(DeliveryJournalKind.Dispatch, journal.Kind); Assert.Equal(8m, journal.BaseQuantity); Assert.Equal(80000m, journal.CostAmount);
            Assert.Equal(0m, journal.MoneyAmount); Assert.Null(journal.SaleOrderId);
            var parts = await db.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == c.Id).OrderBy(x => x.Id).ToArrayAsync();
            Assert.Equal(2, parts.Length); Assert.Equal(new[] { 36000m, 44000m }, parts.Select(x => x.CostAmount));
            Assert.All(parts, x => { Assert.Equal(4m, x.BaseQuantity); Assert.Equal(c.Picking.Source.Detail.SourceWarehouseId, x.SourceWarehouseId); });
            Assert.All(await db.InventoryReservations.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync(), x => Assert.NotEqual(InventoryReservationStatus.Active, x.Status));
            var generations = await db.InventoryReservations.Where(x => x.DeliveryOrderId == c.Id).OrderBy(x => x.Id).ToArrayAsync();
            Assert.Equal(10m, generations[0].ReservedQty); Assert.Equal(InventoryReservationStatus.Released, generations[0].Status);
            Assert.Equal(8m, generations[1].ReservedQty); Assert.Equal(InventoryReservationStatus.Consumed, generations[1].Status);
        }
        Assert.Equal(finance, await c.FinanceAsync());
    }

    [Fact]
    public async Task T04_Return_two_accepts_exact_source_cost_then_D08_residual_read_is_58000_and_has_no_effects()
    {
        var isolated = new DeliveryD02Fixture(); await isolated.InitializeAsync();
        try
        {
            using var c = await DeliveryD05Case.CreateAsync(isolated);
            var sourceLineId = Assert.Single((await c.Picking.GetAsync()).Lines).LineId;
            await using (var setup = c.Picking.Source.Context())
            {
                // Separate identity domains in this fresh, owned database so a fragment ID
                // cannot accidentally stand in for a delivery quote-line ID.
                var databaseName = new SqlConnectionStringBuilder(isolated.Web.Database.ConnectionString).InitialCatalog;
                Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", databaseName);
                Assert.Equal(databaseName, setup.Database.GetDbConnection().Database);
                Assert.Empty(await setup.DeliveryDispatchCostFragments.IgnoreQueryFilters().ToArrayAsync());
                Assert.InRange(sourceLineId, 1, 999999);
                await setup.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('DeliveryDispatchCostFragments', RESEED, 1000000)");
            }
            var finance = await c.FinanceAsync(); await c.ReadyAsync(); await c.DispatchAsync();
            var receive = await c.ReceiveAsync("2");
            var summary = await c.GetAsync();
            Assert.Equal("8", summary.DispatchedBaseQuantityText); Assert.Equal("2", summary.AcceptedReturnedBaseQuantityText);
            Assert.Equal("6", summary.StockOutsideBaseQuantityText); Assert.Null(summary.InCustodyBaseQuantityText);
            var line = Assert.Single(summary.Lines); Assert.Equal("22000", line.AcceptedReturnedCostText);
            var before = await c.SnapshotAsync();
            var cost = Assert.Single(await c.ResidualAsync("6"));
            Assert.Equal("58000", cost.DeliveredCostText); Assert.Equal("22000", cost.AcceptedReturnedCostText);
            Assert.Equal("0", cost.RemainingOutsideBaseQuantityText); Assert.Equal("0", cost.RemainingOutsideCostText);
            Assert.Equal(cost, Assert.Single(await c.ResidualAsync("6"))); Assert.Equal(before, await c.SnapshotAsync());
            Assert.Equal(finance, await c.FinanceAsync());
            await using var db = c.Picking.Source.Context();
            var returned = Assert.Single(await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
            Assert.Equal(2m, returned.BaseQuantity); Assert.Equal(22000m, returned.CostAmount); Assert.Equal(11000m, returned.UnitCost);
            var original = await db.DeliveryDispatchCostFragments.SingleAsync(x => x.Id == returned.DispatchCostFragmentId);
            Assert.InRange(original.Id, 1000000, int.MaxValue); Assert.NotEqual(sourceLineId, original.Id);
            Assert.Equal(sourceLineId, original.DeliveryOrderLineId); Assert.Equal(sourceLineId, returned.DeliveryOrderLineId);
            Assert.Equal(original.InventoryCostBudgetDebitId, (await db.InventoryCostBudgetDebits.SingleAsync(x => x.Id == original.InventoryCostBudgetDebitId)).Id);
            var journal = await db.DeliveryJournalEntries.SingleAsync(x => x.Id == returned.DeliveryJournalEntryId);
            Assert.Equal(DeliveryJournalKind.Return, journal.Kind); Assert.Equal(sourceLineId, journal.DeliveryOrderLineId);
            Assert.Equal(journal.Id, Assert.Single(receive.JournalEntryIds));
            Assert.Equal(returned.InventoryTransactionId, journal.InventoryTransactionId);
            var movement = await db.InventoryTransactions.SingleAsync(x => x.Id == returned.InventoryTransactionId);
            Assert.Equal(sourceLineId, movement.ReferenceLineId); Assert.Equal(InventoryReferenceType.Delivery, movement.ReferenceType);
            Assert.Equal(c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), movement.ReferenceId);
            Assert.Equal("return:" + receive.ClientRequestId.ToString("N") + ":" + original.Id, movement.ReferenceSubKey);
            var valuation = await db.InventoryValuationEntries.SingleAsync(x => x.Id == returned.InventoryValuationEntryId);
            Assert.Equal(movement.Id, valuation.InventoryTransactionId); Assert.Equal(sourceLineId, valuation.ReferenceLineId);
            Assert.Equal(2m, movement.QuantityChange); Assert.Equal(22000m, movement.TotalCost);
            Assert.Equal(2m, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == original.VariantId)).OnHandQty);
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task T05_Dispatch_and_receive_replay_old_versions_once_wrong_payload_or_overreturn_roll_back()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); var ready = await c.ReadyAsync();
        var dispatch = new DeliveryInventoryDispatchRequest(Guid.NewGuid(), ready.Delivery.Version, ready.ApprovedRevision!.Value);
        var original = await c.DispatchAsync(dispatch); var snapshot = await c.SnapshotAsync();
        var replay = await c.DispatchAsync(dispatch); Assert.True(replay.Replayed); Assert.Equal(original.JournalEntryIds, replay.JournalEntryIds);
        Assert.Equal(snapshot, await c.SnapshotAsync());
        var detail = await c.Picking.GetAsync(); var line = Assert.Single(detail.Lines);
        var receive = new DeliveryInventoryReceiveRequest(Guid.NewGuid(), detail.Delivery.Version, [new(line.LineId, "2")], "Nhận lại 2");
        var accepted = await c.ReceiveAsync("2", receive); snapshot = await c.SnapshotAsync();
        var repeated = await c.ReceiveAsync("2", receive); Assert.True(repeated.Replayed); Assert.Equal(accepted.JournalEntryIds, repeated.JournalEntryIds);
        Assert.Equal(snapshot, await c.SnapshotAsync());
        await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.ReceiveAsync("3", receive with { Lines = [new(line.LineId, "3")] }));
        Assert.Equal(snapshot, await c.SnapshotAsync());
        var excess = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.ReceiveAsync("7"));
        Assert.Equal("RETURN_BUDGET_EXCEEDED", excess.Code);
        Assert.Equal(snapshot, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task T06_Failure_after_stock_write_rolls_back_every_hold_budget_journal_and_receipt_then_retry_posts_once(bool returning)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); var ready = await c.ReadyAsync();
        if (returning) await c.DispatchAsync();
        var detail = await c.Picking.GetAsync();
        var dispatch = new DeliveryInventoryDispatchRequest(Guid.NewGuid(), ready.Delivery.Version, ready.ApprovedRevision!.Value);
        var receive = new DeliveryInventoryReceiveRequest(Guid.NewGuid(), detail.Delivery.Version, [new(Assert.Single(detail.Lines).LineId, "2")], "Nhận thực tế");
        var before = await c.SnapshotAsync();
        await Assert.ThrowsAsync<D05InjectedFailure>(async () =>
        {
            if (returning) await c.ReceiveAsync("2", receive, new D05AfterStockFailure());
            else await c.DispatchAsync(dispatch, new D05AfterStockFailure());
        });
        Assert.Equal(before, await c.SnapshotAsync());
        var ack = returning ? await c.ReceiveAsync("2", receive) : await c.DispatchAsync(dispatch);
        Assert.False(ack.Replayed);
        var again = returning ? await c.ReceiveAsync("2", receive) : await c.DispatchAsync(dispatch);
        Assert.True(again.Replayed);
    }

    [Fact]
    public async Task T07_POS_negative_override_cannot_cross_delivery_floor_but_can_consume_unheld_remainder()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, stock: [(10m, 10m)]);
        await c.Picking.ClaimAsync(); await c.Picking.ReportAsync("8"); var variant = await c.VariantAsync();
        var before = await c.SnapshotAsync();
        var factory = new InventoryMovementFactory();
        var denied = factory.CreateSaleFinalize(c.Picking.Account.Store.WarehouseId, variant, 90501, 1, "D05 protected", 3m, 10m);
        denied.AllowNegativeBalance = true;
        await using (var db = c.Picking.Source.Context())
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(denied));
            Assert.Contains("DELIVERY_STOCK_PROTECTED", ex.Message, StringComparison.Ordinal);
        }
        Assert.Equal(before, await c.SnapshotAsync());
        await using (var db = c.Picking.Source.Context())
        {
            var allowed = factory.CreateSaleFinalize(c.Picking.Account.Store.WarehouseId, variant, 90502, 1, "D05 unheld remainder", 2m, 10m);
            allowed.AllowNegativeBalance = true;
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(allowed);
        }
        await using var verify = c.Picking.Source.Context();
        var balance = await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        Assert.Equal(8m, balance.OnHandQty); Assert.Equal(8m, balance.ReservedQty);
    }

    [Theory]
    [InlineData("provisional")]
    [InlineData("missing-final")]
    [InlineData("missing-layer")]
    [InlineData("unknown-earlier")]
    public async Task T08_Strict_dispatch_rejects_unknown_FIFO_without_skipping_or_leaking_partial_posts(string fault)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); await c.ReadyAsync();
        var variant = await c.VariantAsync();
        await using (var db = c.Picking.Source.Context())
        {
            var layer = await db.InventoryCostLayers.Where(x => x.ProductVariantId == variant).OrderBy(x => x.Id).FirstAsync();
            var valuation = await db.InventoryValuationEntries.SingleAsync(x => x.Id == layer.InventoryValuationEntryId);
            if (fault == "provisional") layer.IsProvisionalSource = true;
            if (fault is "missing-final" or "unknown-earlier") valuation.CostFinalizedAtUtc = null;
            if (fault == "missing-layer") layer.IsDeleted = true;
            await db.SaveChangesAsync();
        }
        var before = await c.SnapshotAsync();
        var exception = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.DispatchAsync());
        Assert.Equal("COST_NOT_READY", exception.Code);
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task T09_New_report_or_dispatch_requires_active_original_source_but_physical_return_uses_original_source(bool warehouse)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); await c.ReadyAsync(); await c.DispatchAsync();
        await using var db = c.Picking.Source.Context();
        var source = c.Picking.Source.Detail;
        try
        {
            if (warehouse) (await db.Warehouses.SingleAsync(x => x.Id == source.SourceWarehouseId)).IsActive = false;
            else (await db.LegalEntities.SingleAsync(x => x.Id == source.SourceLegalEntityId)).IsActive = false;
            await db.SaveChangesAsync();
            // The internal executor is allowed to accept goods back to their frozen source.
            var rowVersion = await db.DeliveryOrders.AsNoTracking().Where(x => x.Id == c.Id).Select(x => x.RowVersion).SingleAsync();
            var lineId = await db.DeliveryOrderLines.Where(x => x.DeliveryOrderId == c.Id).Select(x => x.Id).SingleAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            await c.Service(db).ReceiveReturnsInTransactionAsync(c.Id,
                new(Guid.NewGuid(), Convert.ToBase64String(rowVersion), [new(lineId, "2")], "Nhận hàng thực tế tại kho nguồn"));
            await tx.CommitAsync();
            var returned = Assert.Single(await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
            Assert.Equal(source.SourceWarehouseId, returned.SourceWarehouseId); Assert.Equal(source.SourceLegalEntityId, returned.SourceLegalEntityId);
        }
        finally
        {
            if (warehouse) (await db.Warehouses.SingleAsync(x => x.Id == source.SourceWarehouseId)).IsActive = true;
            else (await db.LegalEntities.SingleAsync(x => x.Id == source.SourceLegalEntityId)).IsActive = true;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task T10_Two_actual_SQL_workers_cannot_receive_the_same_remaining_source_budget_twice()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); await c.ReadyAsync(); await c.DispatchAsync();
        var detail = await c.Picking.GetAsync(); var line = Assert.Single(detail.Lines);
        var requests = new[] {
            new DeliveryInventoryReceiveRequest(Guid.NewGuid(), detail.Delivery.Version, [new(line.LineId, "5")], "Nhận thực tế 5"),
            new DeliveryInventoryReceiveRequest(Guid.NewGuid(), detail.Delivery.Version, [new(line.LineId, "5")], "Nhận thực tế 5") };
        await using var dbA = c.Picking.Source.Context(); await using var dbB = c.Picking.Source.Context();
        await dbA.Database.OpenConnectionAsync(); await dbB.Database.OpenConnectionAsync();
        var workers = new[] { await WorkerSessionId(dbA), await WorkerSessionId(dbB) };
        Assert.Equal(2, workers.Distinct().Count());
        await using var beforeDb = c.Picking.Source.Context();
        var receiptsBefore = await beforeDb.DeliveryCommandReceipts.CountAsync(x => x.DeliveryOrderId == c.Id);
        async Task<DeliveryFoundationException?> Receive(GaoApp.Infrastructure.Data.AppDbContext db, DeliveryInventoryReceiveRequest request)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            try { await c.Service(db).ReceiveReturnsInTransactionAsync(c.Id, request); await tx.CommitAsync(); return null; }
            catch (DeliveryFoundationException ex) { return ex; }
        }
        var results = await WithOrderGate<DeliveryFoundationException?>(c, workers, () => [Receive(dbA, requests[0]), Receive(dbB, requests[1])]);
        Assert.Single(results, x => x is null);
        Assert.Equal("VERSION_CONFLICT", Assert.IsType<DeliveryFoundationException>(Assert.Single(results, x => x is not null)).Code);
        await using var verify = c.Picking.Source.Context();
        var accepted = await verify.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync();
        Assert.Equal(5m, accepted.Sum(x => x.BaseQuantity)); Assert.Equal(53000m, accepted.Sum(x => x.CostAmount));
        Assert.Equal(receiptsBefore + 1, await verify.DeliveryCommandReceipts.CountAsync(x => x.DeliveryOrderId == c.Id));
        var snapshot = await c.SnapshotAsync(); var winning = results[0] is null ? requests[0] : requests[1];
        Assert.True((await c.ReceiveAsync("5", winning)).Replayed); Assert.Equal(snapshot, await c.SnapshotAsync());
        var over = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.ReceiveAsync("4"));
        Assert.Equal("RETURN_BUDGET_EXCEEDED", over.Code); Assert.Equal(snapshot, await c.SnapshotAsync());
        Assert.Equal("27000", Assert.Single(await c.ResidualAsync("3")).DeliveredCostText);
    }

    [Fact]
    public async Task T11_Partial_return_chunks_share_tail_intervals_and_old_replay_cannot_reopen_the_budget()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); await c.ReadyAsync(); await c.DispatchAsync();
        var detail = await c.Picking.GetAsync();
        var first = new DeliveryInventoryReceiveRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(Assert.Single(detail.Lines).LineId, "2")], "Nhận đợt đầu 2");
        await c.ReceiveAsync("2", first); await c.ReceiveAsync("3");
        await using var db = c.Picking.Source.Context();
        var received = await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id)
            .OrderBy(x => x.DispatchCostFragmentId).ThenBy(x => x.StartQuantity).ToArrayAsync();
        Assert.Equal(5m, received.Sum(x => x.BaseQuantity)); Assert.Equal(53000m, received.Sum(x => x.CostAmount));
        foreach (var group in received.GroupBy(x => x.DispatchCostFragmentId))
        {
            var intervals = group.OrderBy(x => x.StartQuantity).ToArray();
            for (var index = 1; index < intervals.Length; index++) Assert.Equal(intervals[index - 1].EndQuantity, intervals[index].StartQuantity);
        }
        var before = await c.SnapshotAsync(); Assert.True((await c.ReceiveAsync("2", first)).Replayed);
        Assert.Equal(before, await c.SnapshotAsync());
        var cost = Assert.Single(await c.ResidualAsync("3")); Assert.Equal("27000", cost.DeliveredCostText); Assert.Equal("53000", cost.AcceptedReturnedCostText);
    }

    private static async Task<int> WorkerSessionId(GaoApp.Infrastructure.Data.AppDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT @@SPID";
        var worker = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(worker > 0);
        return worker;
    }

    private static async Task<string> Physical(DeliveryD05Case c)
    {
        await using var db = c.Picking.Source.Context();
        return JsonSerializer.Serialize(await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.OnHandQty, x.InventoryValue }).ToArrayAsync());
    }

    private async Task<T[]> WithBalanceGate<T>(DeliveryD05Case c, int variant, int[] workers, Func<Task<T>[]> start)
    {
        await using var gate = LocalDbSqlConnectionFactory.Create(c.Picking.Web.Database.ConnectionString);
        await gate.OpenAsync(); await using var tx = (SqlTransaction)await gate.BeginTransactionAsync();
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = tx; command.CommandText = "SELECT Id FROM InventoryBalances WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE StoreId=@s AND WarehouseId=@w AND ProductVariantId=@v";
            command.Parameters.AddWithValue("@s", c.Picking.Account.Store.StoreId); command.Parameters.AddWithValue("@w", c.Picking.Account.Store.WarehouseId); command.Parameters.AddWithValue("@v", variant);
            Assert.NotNull(await command.ExecuteScalarAsync());
        }
        var pending = start(); var paths = Array.Empty<int[]>(); var held = false; var pendingBoth = false; var actualStatements = false;
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(6))
            {
                await using var command = gate.CreateCommand(); command.Transaction = tx;
                command.CommandText = """
                    SELECT r.session_id,r.blocking_session_id,r.wait_type,
                     CASE WHEN t.text LIKE '%InventoryBalances%' THEN 1 ELSE 0 END
                    FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
                    WHERE r.database_id=DB_ID() AND r.session_id<>@@SPID;
                    SELECT COUNT(*) FROM sys.dm_tran_locks l JOIN sys.partitions p ON l.resource_associated_entity_id=p.hobt_id
                    WHERE l.resource_database_id=DB_ID() AND l.request_session_id=@@SPID AND l.request_status='GRANT'
                      AND l.resource_type='KEY' AND l.request_mode IN('U','X') AND p.object_id=OBJECT_ID('InventoryBalances');
                    """;
                var requests = new List<(int Id, int Blocker, string? Wait, bool Balance)>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) requests.Add((reader.GetInt16(0), reader.GetInt16(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3) == 1));
                await reader.NextResultAsync(); Assert.True(await reader.ReadAsync()); held = reader.GetInt32(0) > 0;
                int[] GatePath(int worker)
                {
                    var seen = new HashSet<int>(); var path = new List<int>(); var node = worker;
                    for (var depth = 0; depth < 32 && seen.Add(node); depth++)
                    {
                        path.Add(node); if (node == gate.ServerProcessId) return path.ToArray();
                        var request = requests.SingleOrDefault(x => x.Id == node);
                        if (request.Blocker <= 0) break; node = request.Blocker;
                    }
                    return [];
                }
                paths = workers.Select(GatePath).Where(x => x.Length > 1).ToArray();
                actualStatements = workers.All(id => requests.Any(x => x.Id == id && x.Balance && x.Wait is not null && x.Wait.StartsWith("LCK_M_", StringComparison.Ordinal)));
                pendingBoth = pending.Length == 2 && pending.All(x => !x.IsCompleted);
                if (held && paths.Length == 2 && actualStatements && pendingBoth) break;
                await Task.Delay(25);
            }
            output.WriteLine("D05_SQL_BALANCE_BARRIER_PROOF " + JsonSerializer.Serialize(new {
                OwnedDatabase = gate.Database, GateSessionId = gate.ServerProcessId, DistinctWorkerSessionIds = workers,
                AcyclicGatePaths = paths, GateHeldBeforeRelease = held, BothTasksPendingBeforeRelease = pendingBoth,
                BothActualBalanceStatementsBlocked = actualStatements }));
        }
        finally { await tx.RollbackAsync(); }
        var results = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(35));
        Assert.Equal(2, workers.Distinct().Count()); Assert.Equal(2, paths.Length);
        Assert.True(held); Assert.True(pendingBoth); Assert.True(actualStatements); return results;
    }

    private async Task<T[]> WithOrderGate<T>(DeliveryD05Case c, int[] workers, Func<Task<T>[]> start)
    {
        Assert.Equal(2, workers.Distinct().Count());
        await using var gate = LocalDbSqlConnectionFactory.Create(c.Picking.Web.Database.ConnectionString);
        await gate.OpenAsync(); await using var tx = (SqlTransaction)await gate.BeginTransactionAsync();
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=1000; SELECT @r;";
            command.Parameters.AddWithValue("@resource", "GaoApp:Delivery:order:" + c.Picking.Account.Store.StoreId + ":" + c.Id);
            Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 0);
        }
        string resource;
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = "SELECT resource_description FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION' AND request_session_id=@@SPID AND request_mode='X' AND request_status='GRANT'";
            resource = Assert.IsType<string>(await command.ExecuteScalarAsync());
        }
        var pending = start(); var paths = Array.Empty<int[]>(); var held = false; var bothWaiting = false; var bothPending = false;
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(6))
            {
                await using var command = gate.CreateCommand(); command.Transaction = tx;
                command.CommandText = "SELECT session_id,blocking_session_id,wait_type FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND session_id<>@@SPID; SELECT request_session_id,request_mode,request_status FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION' AND resource_description=@resource;";
                command.Parameters.AddWithValue("@resource", resource);
                var requests = new List<(int Id, int Blocker, string? Wait)>(); var locks = new List<(int Id, string Mode, string Status)>();
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) requests.Add((reader.GetInt16(0), reader.GetInt16(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                    await reader.NextResultAsync();
                    while (await reader.ReadAsync()) locks.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
                }
                int[] GatePath(int worker)
                {
                    var seen = new HashSet<int>(); var path = new List<int>(); var node = worker;
                    for (var depth = 0; depth < 32 && seen.Add(node); depth++)
                    {
                        path.Add(node); if (node == gate.ServerProcessId) return path.ToArray();
                        var request = requests.SingleOrDefault(x => x.Id == node); if (request.Blocker <= 0) break; node = request.Blocker;
                    }
                    return [];
                }
                paths = workers.Select(GatePath).Where(x => x.Length > 1).ToArray();
                held = locks.Count(x => x.Id == gate.ServerProcessId && x.Mode == "X" && x.Status == "GRANT") == 1;
                bothWaiting = workers.All(id => requests.Any(x => x.Id == id && x.Wait == "LCK_M_X") &&
                    locks.Any(x => x.Id == id && x.Mode == "X" && x.Status is "WAIT" or "CONVERT"));
                bothPending = pending.Length == 2 && pending.All(x => !x.IsCompleted);
                if (held && bothWaiting && bothPending && paths.Length == 2) break;
                await Task.Delay(25);
            }
            output.WriteLine("D05_SQL_RETURN_BARRIER_PROOF " + JsonSerializer.Serialize(new {
                OwnedDatabase = gate.Database, GateSessionId = gate.ServerProcessId, ExactGateApplicationResource = resource,
                DistinctWorkerSessionIds = workers, AcyclicGatePaths = paths, GateHeldBeforeRelease = held,
                BothTasksPendingBeforeRelease = bothPending, BothActualOrderApplicationLocksWaiting = bothWaiting }));
        }
        finally { await tx.RollbackAsync(); }
        var result = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(35));
        Assert.Equal(2, paths.Length); Assert.True(held); Assert.True(bothWaiting); Assert.True(bothPending); return result;
    }
}

internal sealed class D05InjectedFailure : Exception;
internal sealed class D05AfterStockFailure : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<DeliveryJournalEntry>().Any(x => x.State == EntityState.Added))
            throw new D05InjectedFailure();
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
