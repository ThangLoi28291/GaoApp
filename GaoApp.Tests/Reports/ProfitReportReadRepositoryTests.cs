using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Services.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Reports;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Reports;

public sealed class ProfitReportReadRepositoryTests
{
    [Fact]
    public async Task Explicit_store_is_required_even_for_host_and_cannot_be_overridden()
    {
        await using var host = Context(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProfitReportReadRepository(host).ReadAsync(1, Periods(), false));
        await using var scoped = Context(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProfitReportReadRepository(scoped).ReadAsync(2, Periods(), false));
    }

    [Fact]
    public async Task Missing_navigation_survives_materialization_and_cost_fails_closed()
    {
        await using var db = Context(1);
        var shift = await SeedShiftAsync(db, 1);
        var order = new Order { StoreId = 1, OrderNumber = "MISSING", Status = OrderStatus.Completed,
            CompletedAtUtc = ProfitReportAggregationPolicyTests.At, Subtotal = 200, POSShiftId = shift.Id, POSShift = shift };
        db.Orders.Add(order); await db.SaveChangesAsync();
        var line = new OrderLine { StoreId = 1, OrderId = order.Id, VariantId = 999, Quantity = 10, BaseQuantity = 10 };
        db.OrderLines.Add(line); await db.SaveChangesAsync();
        db.InventoryValuationEntries.Add(new InventoryValuationEntry { StoreId = 1, WarehouseId = 999,
            ProductVariantId = 999, InventoryTransactionId = 999, ReferenceType = InventoryReferenceType.Order,
            ReferenceId = order.Id.ToString(), ReferenceLineId = line.Id, EntryType = InventoryValuationEntryType.Outbound,
            Quantity = -10, UnitCost = 10, Amount = -100, OccurredAtUtc = ProfitReportAggregationPolicyTests.At });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var source = await new ProfitReportReadRepository(db).ReadAsync(1, Periods(), false);
        var root = Assert.Single(source.Entries);
        Assert.Null(root.InventoryTransaction); Assert.Null(root.Warehouse);
        var policy = ProfitReportAggregationPolicyTests.Policy();
        var result = policy.Aggregate(source, Periods().Current, policy.EvaluateCosts(source));
        Assert.Equal(200, result.Summary.NetSales.Value); Assert.Null(result.Summary.Cogs.Value);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Foreign_orders_are_not_returned_and_orphan_activity_is_not_hidden()
    {
        var database = Guid.NewGuid().ToString();
        await using var db = Context(1, database);
        await using (var foreign = Context(2, database))
        {
            var foreignShift = await SeedShiftAsync(foreign, 2);
            foreign.Orders.Add(new Order { StoreId = 2, OrderNumber = "FOREIGN", CompletedAtUtc = ProfitReportAggregationPolicyTests.At,
                POSShiftId = foreignShift.Id, POSShift = foreignShift });
            await foreign.SaveChangesAsync();
        }
        var shift = await SeedShiftAsync(db, 1);
        db.Orders.Add(new Order { StoreId = 1, OrderNumber = "OWN", CompletedAtUtc = ProfitReportAggregationPolicyTests.At,
            POSShiftId = shift.Id, POSShift = shift });
        // Tenant enforcement on writes is preserved; seed foreign fixture through its own context.
        await db.SaveChangesAsync();
        db.InventoryValuationEntries.Add(new InventoryValuationEntry { StoreId = 1,
            InventoryTransactionId = 999, WarehouseId = 999, ProductVariantId = 999,
            ReferenceType = InventoryReferenceType.Order, ReferenceId = "unknown",
            EntryType = InventoryValuationEntryType.Revaluation, Amount = 20, OccurredAtUtc = ProfitReportAggregationPolicyTests.At });
        await db.SaveChangesAsync();
        var s = await new ProfitReportReadRepository(db).ReadAsync(1, Periods(), true);
        Assert.All(s.Orders, x => Assert.Equal(1, x.StoreId));
        var p = ProfitReportAggregationPolicyTests.Policy();
        var activity = p.Activity(s, Periods().Current, p.EvaluateCosts(s));
        Assert.Null(activity.Net); Assert.Null(activity.Count);
        Assert.Single(activity.Rows);
    }

    [Fact]
    public async Task Invalid_terminal_does_not_fall_back_to_all_terminals()
    {
        await using var db = Context(1);
        var periods = Periods(); periods.Current.TerminalId = 444;
        await Assert.ThrowsAsync<ArgumentException>(() => new ProfitReportReadRepository(db).ReadAsync(1, periods, false));
    }

    internal static SalesResolvedPeriodSet Periods() => new SalesReportingPeriodPolicy().Resolve(
        new SalesExecutiveDashboardQueryDto { FromDate = new DateTime(2026, 9, 8), ToDate = new DateTime(2026, 9, 8), Compare = "none" },
        ProfitReportAggregationPolicyTests.At);

    [Fact]
    public async Task Oversized_source_fails_instead_of_returning_truncated_financial_totals()
    {
        await using var db = Context(1);
        var shift = await SeedShiftAsync(db, 1);
        db.Orders.AddRange(Enumerable.Range(0, 101).Select(i => new Order { StoreId = 1,
            OrderNumber = "LIMIT-" + i, CompletedAtUtc = ProfitReportAggregationPolicyTests.At,
            POSShiftId = shift.Id, POSShift = shift }));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var limits = new GaoApp.Application.Common.Options.ProfitReportLimits { MaxSourceRows = 100 };
        await Assert.ThrowsAsync<GaoApp.Application.Common.Exceptions.ValidationAppException>(() =>
            new ProfitReportReadRepository(db, limits).ReadAsync(1, Periods(), false));
        Assert.Empty(db.ChangeTracker.Entries());
    }
    private static async Task<POSShift> SeedShiftAsync(InMemoryAppDbContext db, int store)
    {
        db.Stores.Add(new Store { Id = store, Name = "Profit store " + store,
            SubDomain = "profit-" + store, SubDomainNormalized = "PROFIT-" + store });
        var shift = new POSShift
        {
            StoreId = store, OpenedByUserId = 1, OpenedAtUtc = ProfitReportAggregationPolicyTests.At.AddHours(-1),
            Terminal = new POSTerminal { StoreId = store, Code = "POS-PROFIT", Name = "Profit report POS" },
            Warehouse = new Warehouse
            {
                StoreId = store, Code = "WH-PROFIT", Name = "Profit report warehouse",
                LegalEntity = new LegalEntity { StoreId = store, Code = "LEGAL-PROFIT", Name = "Profit report owner", LegalName = "Profit report owner" }
            }
        };
        db.POSShifts.Add(shift);
        await db.SaveChangesAsync();
        return shift;
    }

    private static InMemoryAppDbContext Context(int? store, string? database = null)
    {
        var tenant = new TenantContext();
        if (store.HasValue) tenant.SetStore(store.Value, "test"); else tenant.SetHostAdmin();
        return new(new DbContextOptionsBuilder<InMemoryAppDbContext>().UseInMemoryDatabase(database ?? Guid.NewGuid().ToString()).Options,
            tenant, new User());
    }
    private sealed class User : ICurrentUser
    {
        public int? UserId => 1; public string? UserName => "profit-test"; public int? TerminalId => null;
        public string? TerminalCode => null; public bool IsAuthenticated => true;
    }
}
