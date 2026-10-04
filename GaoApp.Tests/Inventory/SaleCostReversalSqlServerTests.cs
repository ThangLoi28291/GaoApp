using FluentAssertions;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using Microsoft.EntityFrameworkCore;
using static GaoApp.Tests.Inventory.SaleCostReversalIntegrationTests;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
public sealed class SaleCostReversalSqlServerTests
{
    [Fact]
    public async Task Concurrent_completed_voids_have_exactly_one_durable_cost_reversal()
    {
        await using var fixture = await CostFixture.CreateAsync("auto", 12);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Attempt()
        {
            await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
            await start.Task;
            try
            {
                await fixture.Pos(db).VoidCompletedOrderAsync(fixture.Seed.OrderId, "concurrent void");
                return true;
            }
            catch (InvalidOperationException) { return false; }
            catch (GaoApp.Application.Common.Exceptions.BusinessRuleException) { return false; }
        }
        var first = Attempt();
        var second = Attempt();
        start.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60));
        results.Count(x => x).Should().Be(1);
        await using var verify = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var mirrors = await verify.InventoryValuationEntries
            .Where(x => x.EntryType == InventoryValuationEntryType.Inbound &&
                x.InventoryTransaction.TransactionType == InventoryTransactionType.SaleVoidIn).ToListAsync();
        mirrors.Should().ContainSingle();
        mirrors[0].Amount.Should().Be(120);
        mirrors[0].Quantity.Should().Be(10);
        await fixture.AssertCostAsync(verify, 0);
        var count = await verify.InventoryValuationEntries.CountAsync();
        Func<Task> retry = () => fixture.Pos(verify).VoidCompletedOrderAsync(fixture.Seed.OrderId, "repeat command");
        await retry.Should().ThrowAsync<Exception>();
        (await verify.InventoryValuationEntries.CountAsync()).Should().Be(count);
    }

    [Fact]
    public async Task Unsupported_history_rolls_back_the_whole_void_including_shift_state()
    {
        await using var fixture = await CostFixture.CreateAsync("auto", 12);
        await using (var corruptFixture = fixture.Database.CreateTenantContext(fixture.Seed.StoreId))
        {
            var adjustment = await corruptFixture.InventoryValuationEntries
                .SingleAsync(x => x.EntryType == InventoryValuationEntryType.Revaluation);
            adjustment.CostSourceType = InventoryCostSourceType.RevaluationAdjustment;
            await corruptFixture.SaveChangesAsync();
        }
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var beforeEntries = await db.InventoryValuationEntries.CountAsync();
        var beforeShift = await db.POSShifts.AsNoTracking().SingleAsync();
        Func<Task> attempt = () => fixture.Pos(db).VoidCompletedOrderAsync(fixture.Seed.OrderId, "must rollback");
        var failure = await attempt.Should().ThrowAsync<GaoApp.Application.Common.Exceptions.Pos.PosAppException>();
        failure.Which.ErrorCode.Should().Be("POS_VOID_FAILED");
        failure.Which.InnerException.Should().BeAssignableTo<InvalidOperationException>();
        await using var verify = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        (await verify.InventoryValuationEntries.CountAsync()).Should().Be(beforeEntries);
        var order = await verify.Orders.SingleAsync(x => x.Id == fixture.Seed.OrderId);
        order.Status.Should().Be(OrderStatus.Completed);
        var afterShift = await verify.POSShifts.SingleAsync();
        (afterShift.VoidCount, afterShift.CashSalesTotal, afterShift.NonCashSalesTotal)
            .Should().Be((beforeShift.VoidCount, beforeShift.CashSalesTotal, beforeShift.NonCashSalesTotal));
    }

    [Fact]
    public async Task Cost_evidence_requires_explicit_store_even_for_host_context()
    {
        await using var fixture = await CostFixture.CreateAsync("actual", 10);
        await using var host = fixture.Database.CreateHostContext();
        var repo = new InventoryValuationEntryRepository(host);
        Func<Task> read = () => repo.GetSaleIssueEntriesByOrderLineAsync(
            fixture.Seed.OrderId, fixture.Seed.LegacyOrderLineId);
        await read.Should().ThrowAsync<InvalidOperationException>();
    }
}
