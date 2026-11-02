using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06MoneySummaryTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(44_000, 44_000)] [InlineData(46_000, 46_000)]
    public async Task Real_authenticated_processor_under_or_overpayment_summary_retains_actual_unallocated_received_money(decimal received, decimal expected)
    {
        var c = await Web05Prepared.CreateAsync(fixture);
        c.Bank.Pay(c.Reference, received, "W06-SUMMARY-" + Guid.NewGuid().ToString("N")); await c.Case.ProcessAsync(c.IntentId, c.Bank);
        await using var db = c.Case.Context(); var service = ReadOnlyService(c, db); var summary = await service.GetAsync(c.OrderId);
        Assert.Equal(expected, summary.ActualStoreReceived); Assert.False(summary.CanPostSale);
        Assert.Equal(StorefrontPaymentState.Review, (await db.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Equal(received, await db.Set<GaoApp.Domain.Entities.BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.Case.StoreId).SumAsync(x => x.Amount));
        Assert.Empty(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Real_paid_allocation_then_extra_certified_transfer_displays46000_not_only45000_allocated_and_preservesReview()
    {
        var c = await Web05Prepared.CreateAsync(fixture); c.Bank.Pay(c.Reference, 45_000m, "W06-ORIGINAL-" + Guid.NewGuid().ToString("N"));
        await c.Case.ProcessAsync(c.IntentId, c.Bank);
        c.Bank.Pay(c.Reference, 1_000m, "W06-EXTRA-" + Guid.NewGuid().ToString("N")); await c.Case.ProcessAsync(c.IntentId, c.Bank);
        await using var db = c.Case.Context(); var summary = await ReadOnlyService(c, db).GetAsync(c.OrderId);
        Assert.Equal(46_000m, summary.ActualStoreReceived); Assert.False(summary.CanPostSale);
        Assert.Equal(45_000m, await db.Set<StorefrontPaymentAllocation>().SumAsync(x => x.Amount));
        Assert.Equal(46_000m, await db.Set<GaoApp.Domain.Entities.BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.Case.StoreId).SumAsync(x => x.Amount));
        Assert.Equal(StorefrontPaymentState.Review, (await db.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Empty(await db.Orders.ToListAsync());
    }

    // These dependencies must never run during a read-only summary. Positive
    // W06 financial tests use the real bridge and durable invoice processor.
    private static IStorefrontSettlementService ReadOnlyService(Web05Prepared c, GaoApp.Infrastructure.Data.AppDbContext db)
        => new StorefrontSettlementService(db, new Web01Actor(c.Case.Seed.UserId),
            new Web01Permissions(c.Case.StoreId, c.Case.Seed.UserId, [StorefrontPermissions.ViewOrders]),
            new InventoryBalanceRepository(db), InventoryPosPostingContractTests.CreateRealMovementService(db),
            new NoSaleAllowed(), new NoInvoiceAllowed(), c.Case.Web.Clock);
    private sealed class NoSaleAllowed : IStorefrontSaleBridge
    { public Task<int> PostAsync(int storefrontOrderId, Guid requestId, CancellationToken ct = default) => throw new InvalidOperationException("Read-only summary must not post sale."); }
    private sealed class NoInvoiceAllowed : IStorefrontInvoiceProcessor
    { public Task ProcessAsync(int storefrontOrderId, CancellationToken ct = default) => throw new InvalidOperationException("Read-only summary must not issue invoice."); }
}
