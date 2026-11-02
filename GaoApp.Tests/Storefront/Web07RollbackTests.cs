using GaoApp.Domain.Entities;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07RollbackTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("second-child-return")] [InlineData("refund-funding")]
    public async Task Actual_multi_source_return_and_cancellation_liability_are_one_transaction_and_same_scope_retry_cannot_duplicate_stock_or_money(string point)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true, multipleSources: true); var physical = c.Physical!;
        await physical.PrepareAsync(); await physical.PickAllAsync(); await physical.DispatchAsync();
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        await physical.OutcomeAllAsync(false); var command = await physical.ReturnRequestAsync();
        Assert.Equal(2, command.Lines.Select(x => x.FulfillmentSourceId).Distinct().Count());
        var before = await physical.PhysicalSnapshotAsync(); var allBefore = await c.SnapshotAsync();
        var fault = new ReturnFault(point); await using var scope = c.Context(fault); var service = physical.Service(scope);
        await Assert.ThrowsAsync<InjectedReturnFailure>(() => service.ReceiveReturnAsync(c.OrderId, command)); Assert.Equal(1, fault.Failures);
        Assert.Equal(before, await physical.PhysicalSnapshotAsync()); Assert.Equal(allBefore, await c.SnapshotAsync());
        await using (var db = c.Context())
        {
            Assert.Empty(await db.DeliveryReturnedCostFragments.ToListAsync());
            Assert.NotNull((await Web07Support.Cancellation(c, db).GetAsync(c.OrderId)).PendingRequestId);
            Assert.Empty(await db.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Empty(await db.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Equal(230_000m, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
        }
        await service.ReceiveReturnAsync(c.OrderId, command);
        await using (var db = c.Context())
        {
            Assert.Equal(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.SingleAsync()).Phase);
            Assert.Equal(4m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity)); Assert.Equal(55_000m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.CostAmount));
            var decision = Assert.Single(await db.Set<StorefrontCancellationDecision>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Equal(StorefrontCancellationDecisionKind.SupersededByActualReturn, decision.Kind); Assert.NotNull(decision.ActualReturnEvidenceId);
            Assert.Equal(230_000m, Assert.Single(await db.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync()).Amount);
            Assert.Equal(230_000m, Assert.Single(await db.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync()).Amount);
            Assert.Empty(await db.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync()); Assert.Empty(await db.Orders.ToListAsync());
        }
        var after = await c.SnapshotAsync();
        await using (var restart = c.Context()) await physical.Service(restart).ReceiveReturnAsync(c.OrderId, command);
        Assert.Equal(after, await c.SnapshotAsync()); Assert.Equal(1, fault.Failures);
    }

    private sealed class InjectedReturnFailure : Exception;
    private sealed class ReturnFault(string point) : SaveChangesInterceptor
    {
        internal int Failures;
        private int fragmentSaves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Failures > 0) return ValueTask.FromResult(result);
            var db = data.Context!;
            var second = db.ChangeTracker.Entries<DeliveryReturnedCostFragment>().Any(x => x.State == EntityState.Added) && ++fragmentSaves >= 2;
            var funding = db.ChangeTracker.Entries<StorefrontRefundFunding>().Any(x => x.State == EntityState.Added);
            if (point == "second-child-return" && second || point == "refund-funding" && funding)
            { Failures++; throw new InjectedReturnFailure(); }
            return ValueTask.FromResult(result);
        }
    }
}
