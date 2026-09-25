using System.Data;
using System.Data.Common;
using System.Diagnostics;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Services.Reports;
using GaoApp.Infrastructure.Repositories.Reports;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace GaoApp.Tests.Reports;

[Collection("R1FinalDatabasePreflight")]
public sealed class ProfitReportSqlServerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("actual", 10, 100)]
    [InlineData("auto", 12, 120)]
    [InlineData("auto", 8, 80)]
    [InlineData("dedicated", 12, 120)]
    [InlineData("dedicated", 8, 80)]
    [InlineData("equal", 10, 100)]
    [InlineData("multiple", 12.6, 126)]
    [InlineData("mixed", 12, 112)]
    public async Task Real_writer_history_projects_final_cost_and_void_closure_without_report_writes(
        string writer, decimal unit, decimal expected)
    {
        await using var fixture = await SaleCostReversalIntegrationTests.CostFixture.CreateAsync(writer, unit);
        var capture = new ReadCapture();
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId, capture);
        var period = await Periods(db, fixture.Seed.OrderId);
        capture.Commands.Clear();
        var clock = Stopwatch.StartNew();
        var snapshot = await new ProfitReportReadRepository(db).ReadAsync(fixture.Seed.StoreId, period, true);
        var policy = ProfitReportAggregationPolicyTests.Policy();
        var cost = policy.EvaluateCosts(snapshot);
        var result = policy.Aggregate(snapshot, period.Current, cost).Summary;
        Assert.Equal(expected, result.Cogs.Value);
        Assert.Equal(200, result.NetSales.Value);
        Assert.Equal(200 - expected, result.GrossProfit.Value);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.InRange(capture.Commands.Count, 1, 30);
        Assert.All(capture.Isolations, x => Assert.Equal(IsolationLevel.Snapshot, x));
        Assert.DoesNotContain(capture.Commands, x => x.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("INSERT ", StringComparison.OrdinalIgnoreCase) || x.Contains("DELETE ", StringComparison.OrdinalIgnoreCase));
        output.WriteLine($"writer={writer}; queryCount={capture.Commands.Count}; readMs={clock.ElapsedMilliseconds}");
        await using (var writerDb = fixture.Database.CreateTenantContext(fixture.Seed.StoreId))
            await fixture.Pos(writerDb).VoidCompletedOrderAsync(fixture.Seed.OrderId, "profit read regression");
        var closed = await new ProfitReportReadRepository(db).ReadAsync(fixture.Seed.StoreId, period, false);
        var after = policy.Aggregate(closed, period.Current, policy.EvaluateCosts(closed)).Summary;
        Assert.Equal(0, after.Cogs.Value); Assert.Equal(0, after.NetSales.Value);
    }

    [Fact]
    public async Task Transaction_link_keeps_malformed_reference_evidence_in_report_source()
    {
        await using var fixture = await SaleCostReversalIntegrationTests.CostFixture.CreateAsync("actual", 10);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var periods = await Periods(db, fixture.Seed.OrderId);
        var root = await db.InventoryValuationEntries.SingleAsync(x =>
            x.EntryType == GaoApp.Domain.Enums.InventoryValuationEntryType.Outbound);
        var rootId = root.Id;
        root.ReferenceId = "broken-order-reference";
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var snapshot = await new ProfitReportReadRepository(db).ReadAsync(fixture.Seed.StoreId, periods, false);
        Assert.Contains(snapshot.Entries, x => x.Id == rootId && x.ReferenceId == "broken-order-reference");
        Assert.Equal(snapshot.Entries.Count, snapshot.Entries.Select(x => x.Id).Distinct().Count());
        var policy = ProfitReportAggregationPolicyTests.Policy();
        var summary = policy.Aggregate(snapshot, periods.Current, policy.EvaluateCosts(snapshot)).Summary;
        Assert.Equal(200, summary.NetSales.Value);
        Assert.Null(summary.Cogs.Value);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Snapshot_report_preserves_one_generation_and_allows_void_to_commit_before_read_finishes()
    {
        await using var fixture = await SaleCostReversalIntegrationTests.CostFixture.CreateAsync("auto", 12);
        await using var setup = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var periods = await Periods(setup, fixture.Seed.OrderId);
        var pause = new PauseOrders();
        await using var reader = fixture.Database.CreateTenantContext(fixture.Seed.StoreId, pause);
        var readTask = new ProfitReportReadRepository(reader).ReadAsync(fixture.Seed.StoreId, periods, false);
        await pause.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeTask = Task.Run(async () =>
        {
            await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
            writerStarted.SetResult();
            await fixture.Pos(db).VoidCompletedOrderAsync(fixture.Seed.OrderId, "concurrent report");
        });
        await writerStarted.Task;
        try
        {
            await writeTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(readTask.IsCompleted);
        }
        finally { pause.Release.TrySetResult(); }
        var before = await readTask.WaitAsync(TimeSpan.FromSeconds(30));
        await writeTask.WaitAsync(TimeSpan.FromSeconds(40));
        var policy = ProfitReportAggregationPolicyTests.Policy();
        var old = policy.Aggregate(before, periods.Current, policy.EvaluateCosts(before)).Summary;
        Assert.Equal(200, old.NetSales.Value); Assert.Equal(120, old.Cogs.Value);
        var after = await new ProfitReportReadRepository(reader).ReadAsync(fixture.Seed.StoreId, periods, false);
        var current = policy.Aggregate(after, periods.Current, policy.EvaluateCosts(after)).Summary;
        Assert.Equal(0, current.NetSales.Value); Assert.Equal(0, current.Cogs.Value);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("partial")]
    [InlineData("mixed-open")]
    public async Task Provisional_SQL_history_is_explicit_and_not_certified_as_final(string writer)
    {
        await using var fixture = await SaleCostReversalIntegrationTests.CostFixture.CreateAsync(writer, 12);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var periods = await Periods(db, fixture.Seed.OrderId);
        var snapshot = await new ProfitReportReadRepository(db).ReadAsync(fixture.Seed.StoreId, periods, false);
        var policy = ProfitReportAggregationPolicyTests.Policy();
        var result = policy.Aggregate(snapshot, periods.Current, policy.EvaluateCosts(snapshot)).Summary;
        Assert.Equal("Tạm tính", result.CostState);
        Assert.NotNull(result.ProvisionalCogs.Value);
        Assert.NotEqual(GaoApp.Application.DTOs.Reports.Profit.ProfitQuality.Finalized, result.Cogs.Quality);
    }

    private static async Task<SalesResolvedPeriodSet> Periods(GaoApp.Infrastructure.Data.AppDbContext db, int orderId)
    {
        var at = (await db.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId)).CompletedAtUtc!.Value;
        var policy = new SalesReportingPeriodPolicy();
        var date = policy.ConvertUtcToLocal(at).Date;
        return policy.Resolve(new SalesExecutiveDashboardQueryDto { FromDate = date, ToDate = date, Compare = "previous" }, DateTime.UtcNow);
    }

    [Theory]
    [InlineData(false, "auto")]
    [InlineData(false, "dedicated")]
    [InlineData(true, "auto")]
    [InlineData(true, "dedicated")]
    public async Task Real_partial_returns_and_NoRestock_preserve_normalized_report_cost(bool legal, string writer)
    {
        await using var fixture = await SaleCostReversalIntegrationTests.CostFixture.CreateAsync(writer, 12);
        await using (var write = fixture.Database.CreateTenantContext(fixture.Seed.StoreId))
        {
            if (legal) await fixture.AddLegalEntityAllocationAsync(write);
            var noRestock = fixture.ReturnRequest(2, GaoApp.Domain.Enums.SalesReturnLineAction.NoRestock);
            noRestock.Lines.Single().RefundUnitAmount = 20;
            await fixture.Returns(write).CreateAsync(noRestock);
            await Task.Delay(1100); // Existing second-precision return-number generator is outside report scope.
            var restock = fixture.ReturnRequest(2);
            restock.Lines.Single().RefundUnitAmount = 20;
            await fixture.Returns(write).CreateAsync(restock);
        }
        await using var read = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var periods = await Periods(read, fixture.Seed.OrderId);
        var s = await new ProfitReportReadRepository(read).ReadAsync(fixture.Seed.StoreId, periods, false);
        var p = ProfitReportAggregationPolicyTests.Policy();
        var result = p.Aggregate(s, periods.Current, p.EvaluateCosts(s)).Summary;
        Assert.Equal(96, result.Cogs.Value);
        Assert.Equal(120, result.NetSales.Value);
        Assert.Equal(24, result.GrossProfit.Value);
    }

    [Fact]
    public async Task Batch_read_of_one_hundred_additional_sales_has_bounded_queries_and_complete_totals()
    {
        await using var fixture = await SaleCostReversalIntegrationTests.CostFixture.CreateAsync("actual", 10);
        await using var seed = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var original = await seed.Orders.SingleAsync(x => x.Id == fixture.Seed.OrderId);
        var roots = new List<GaoApp.Domain.Entities.InventoryValuationEntry>();
        var orders = Enumerable.Range(1, 100).Select(i => new GaoApp.Domain.Entities.Order {
            StoreId = fixture.Seed.StoreId, OrderNumber = "PERF-" + i, POSShiftId = original.POSShiftId,
            Status = GaoApp.Domain.Enums.OrderStatus.Completed, CompletedAtUtc = original.CompletedAtUtc,
            Subtotal = 20, GrandTotal = 20 }).ToList();
        seed.Orders.AddRange(orders); await seed.SaveChangesAsync();
        var lines = orders.Select(o => new GaoApp.Domain.Entities.OrderLine {
            StoreId = fixture.Seed.StoreId, OrderId = o.Id, VariantId = fixture.Seed.LegacyVariantId,
            ItemName = "Synthetic rice", BaseQuantity = 1, Quantity = 1, LineTotal = 20 }).ToList();
        seed.OrderLines.AddRange(lines); await seed.SaveChangesAsync();
        var transactions = lines.Select(l => new GaoApp.Domain.Entities.InventoryTransaction {
            StoreId = fixture.Seed.StoreId, WarehouseId = fixture.Seed.LegacyWarehouseId,
            ProductVariantId = l.VariantId, TransactionType = GaoApp.Domain.Enums.InventoryTransactionType.SaleIssue,
            ReferenceType = GaoApp.Domain.Enums.InventoryReferenceType.Order, ReferenceId = l.OrderId.ToString(),
            ReferenceLineId = l.Id, QuantityChange = -1, OccurredAtUtc = original.CompletedAtUtc!.Value }).ToList();
        seed.InventoryTransactions.AddRange(transactions); await seed.SaveChangesAsync();
        foreach (var tx in transactions)
        {
            var root = new GaoApp.Domain.Entities.InventoryValuationEntry {
                StoreId = fixture.Seed.StoreId, InventoryTransactionId = tx.Id, WarehouseId = tx.WarehouseId,
                ProductVariantId = tx.ProductVariantId, ReferenceType = tx.ReferenceType, ReferenceId = tx.ReferenceId!,
                ReferenceLineId = tx.ReferenceLineId, EntryType = GaoApp.Domain.Enums.InventoryValuationEntryType.Outbound,
                Quantity = -1, UnitCost = 10, Amount = -10, OccurredAtUtc = tx.OccurredAtUtc };
            root.CostLayerAllocations.Add(new GaoApp.Domain.Entities.InventoryCostLayerAllocation {
                StoreId = fixture.Seed.StoreId, Quantity = 1, UnitCost = 10, Amount = 10, IsResolved = true,
                ResolvedQuantity = 1, ResolvedAmount = 10, ResolvedAtUtc = tx.OccurredAtUtc });
            roots.Add(root);
        }
        seed.InventoryValuationEntries.AddRange(roots); await seed.SaveChangesAsync();
        var periods = await Periods(seed, fixture.Seed.OrderId);
        var capture = new ReadCapture();
        await using var read = fixture.Database.CreateTenantContext(fixture.Seed.StoreId, capture);
        var clock = Stopwatch.StartNew();
        var s = await new ProfitReportReadRepository(read).ReadAsync(fixture.Seed.StoreId, periods, false);
        var p = ProfitReportAggregationPolicyTests.Policy();
        var result = p.Aggregate(s, periods.Current, p.EvaluateCosts(s)).Summary;
        Assert.Equal(2200, result.NetSales.Value); Assert.Equal(1100, result.Cogs.Value);
        Assert.InRange(capture.Commands.Count, 1, 25);
        output.WriteLine($"orders=101; queries={capture.Commands.Count}; totalReadAndProjectionMs={clock.ElapsedMilliseconds}");
    }
    private sealed class ReadCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public List<IsolationLevel> Isolations { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            if (command.Transaction is not null) Isolations.Add(command.Transaction.IsolationLevel);
            return ValueTask.FromResult(result);
        }
    }
    private sealed class PauseOrders : DbCommandInterceptor
    {
        private int paused;
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [Orders]") && Interlocked.Exchange(ref paused, 1) == 0)
            { ReadStarted.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken); }
            return result;
        }
    }
}
