using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06SettlementTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_COD_cash_in_and_multiowner_sale_preserve_dispatch_cost_once_and_real_invoice_allocations(bool gift)
    {
        var c = await Web06Case.CreateAsync(fixture, multipleSources: true, quantity: gift ? 2 : 4,
            primaryMultiStock: gift ? 2 : 2.5m, sameSkuGift: gift, primaryUnitCost: gift ? 10_000m : 9_999.6m);
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        if (gift) await Web06Support.AssertPurchasedTwoGiftParityAsync(c, 100_000m);
        await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await Web06FinanceSupport.DeliveredAsync(c);
        await Web06FinanceSupport.CollectAllAsync(c);
        await using (var db = c.Context())
        {
            var due = (await db.StorefrontOrders.SingleAsync()).TotalDue;
            var summary = await Web06FinanceSupport.Settlement(c, db).GetAsync(c.OrderId);
            Assert.Equal(due, summary.CustomerPaid); Assert.Equal(0m, summary.ActualStoreReceived); Assert.Equal(due, summary.CollectorOutstanding); Assert.False(summary.CanPostSale);
        }
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); decimal total;
        await using (var db = c.Context()) total = (await db.StorefrontOrders.SingleAsync()).TotalDue;
        var cash = await Web06FinanceSupport.ActualCashInAsync(c, terminal, total); await Web06FinanceSupport.ReceiveCashAsync(c, cash);
        var command = await c.RequestAsync<StorefrontOperationRequest>(); int movementCount; decimal cost;
        await using (var db = c.Context()) { movementCount = await db.InventoryTransactions.CountAsync(); cost = await db.DeliveryDispatchCostFragments.SumAsync(x => x.CostAmount); Assert.Equal(gift ? 40_000m : 54_999m, cost); }
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, command);
        await using (var db = c.Context())
        {
            var sale = Assert.Single(await db.Orders.Include(x => x.Lines).Include(x => x.Payments).Include(x => x.LegalEntityAllocations).ToListAsync());
            Assert.Equal(OrderStatus.Completed, sale.Status); Assert.True(sale.POSShiftId > 0); Assert.Equal(total, sale.GrandTotal); Assert.Equal(0m, sale.BalanceDue);
            Assert.Equal(total, sale.Payments.Sum(x => x.Amount)); Assert.All(sale.Payments, x => { Assert.Equal(PaymentMethod.Cash, x.Method); Assert.NotNull(x.StorefrontCodReceiptId); });
            Assert.Equal(2, sale.LegalEntityAllocations.Select(x => x.LegalEntityId).Distinct().Count());
            Assert.Equal(c.OrderedBase, sale.LegalEntityAllocations.Sum(x => x.BaseQuantity)); Assert.Equal(total, sale.LegalEntityAllocations.Sum(x => x.NetAmount));
            Assert.All(sale.LegalEntityAllocations, x => Assert.True(x.InventoryTransactionId > 0));
            Assert.Equal(cost, sale.Lines.Sum(x => x.LineCostTotal)); Assert.Equal(movementCount, await db.InventoryTransactions.CountAsync());
            Assert.Equal(cost, await db.DeliveryDispatchCostFragments.SumAsync(x => x.CostAmount));
            var shift = await db.POSShifts.SingleAsync(); Assert.Equal(total, shift.CashInTotal); Assert.Equal(total, shift.ClosingCashExpected);
            Assert.Equal(0m, shift.CashSalesTotal); Assert.Equal(0m, shift.NonCashSalesTotal); Assert.Equal(0m, shift.CashOutTotal);
            Assert.Single(await db.POSShiftCashTransactions.ToListAsync()); Assert.Empty(await bankInspection.Receipts(db).ToListAsync()); await bankInspection.AssertDeltaAsync(db);
            var links = await db.Set<StorefrontSettlementLink>().ToListAsync(); Assert.Equal(2, links.Count); Assert.All(links, x => Assert.Equal(sale.Id, x.SaleOrderId));
            var invoices = await db.InvoiceHeads.Include(x => x.Details).ToListAsync(); Assert.Equal(2, invoices.Count);
            Assert.Equal(total, invoices.Sum(x => x.GrandTotal)); Assert.All(invoices.SelectMany(x => x.Details), x => Assert.NotNull(x.OrderLegalEntityAllocationId));
            Assert.NotNull((await db.Set<StorefrontInvoiceWork>().SingleAsync()).CompletedAtUtc);
            if (gift)
            {
                var actualGift = Assert.Single(sale.Lines, x => x.IsPromotionGift); Assert.Equal(0m, actualGift.LineTotal); Assert.NotNull(actualGift.GiftSourceLineId);
                Assert.Contains(sale.Lines, x => x.Id == actualGift.GiftSourceLineId && !x.IsPromotionGift);
            }
        }
        await using (var db = c.Context()) Assert.True((await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, command)).Replayed);
        await using (var db = c.Context()) { Assert.Single(await db.Orders.ToListAsync()); Assert.Equal(movementCount, await db.InventoryTransactions.CountAsync()); Assert.Equal(2, await db.InvoiceHeads.CountAsync()); }
        var profit = await Web06FinanceSupport.ProfitAsync(c); Assert.Equal(total, profit.Current.NetSales.Value); Assert.Equal(cost, profit.Current.Cogs.Value);
        Assert.Equal(total - cost, profit.Current.GrossProfit.Value); Assert.Equal(GaoApp.Application.DTOs.Reports.Profit.ProfitQuality.Finalized, profit.Current.Cogs.Quality); Assert.Empty(profit.CostDiagnostics.Issues);
        var flow = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date); Assert.Equal(total, flow.Summary.Single(x => x.Fund == "cash").Inflow); Assert.Equal(0m, flow.Summary.Single(x => x.Fund == "bank").Inflow);
    }

    [Theory]
    [InlineData(0)] [InlineData(8)]
    public async Task Customer_shipping_gross_is_split_to_independent_real_service_owner_without_extra_VAT_or_cash(decimal rate)
    {
        var c = await Web06Case.CreateAsync(fixture, shipping: 30_000m, national: true, multipleSources: true);
        var feeOwner = await Web06FinanceSupport.ServiceSettingAsync(c, rate, separateOwner: true);
        await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 230_000m));
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        await using var check = c.Context(); var sale = await check.Orders.SingleAsync(); Assert.Equal(200_000m, sale.GrandTotal);
        var fee = await check.Set<StorefrontServiceRevenue>().SingleAsync(); Assert.Equal(feeOwner, fee.LegalEntityId); Assert.Equal(30_000m, fee.GrossAmount);
        Assert.Equal(30_000m, fee.NetAmount + fee.TaxAmount); Assert.Equal(rate == 0 ? 30_000m : 27_777.78m, fee.NetAmount); Assert.Equal(rate == 0 ? 0m : 2_222.22m, fee.TaxAmount);
        var feeHead = await check.InvoiceHeads.Include(x => x.Details).SingleAsync(x => x.StorefrontServiceRevenueId == fee.Id);
        Assert.Equal(feeOwner, feeHead.LegalEntityId); Assert.Equal(30_000m, feeHead.GrandTotal); Assert.Equal(fee.NetAmount, feeHead.SubTotal); Assert.Equal(fee.TaxAmount, feeHead.VatAmount);
        var detail = Assert.Single(feeHead.Details); Assert.Equal(InvoiceDetailSourceType.FromStorefrontServiceRevenue, detail.SourceType); Assert.Equal(fee.Id, detail.StorefrontServiceRevenueId); Assert.Null(detail.OrderLineId);
        Assert.Equal(30_000m, await check.Set<StorefrontServiceFunding>().SumAsync(x => x.Amount));
        Assert.Equal(230_000m, (await check.POSShifts.SingleAsync()).CashInTotal); Assert.Equal(0m, (await check.POSShifts.SingleAsync()).CashSalesTotal);
        Assert.Equal(230_000m, await check.InvoiceHeads.SumAsync(x => x.GrandTotal)); Assert.Equal(3, await check.InvoiceHeads.CountAsync());
        Assert.Empty(await check.OperatingExpenses.ToListAsync()); Assert.Empty(await check.TreasuryEntries.ToListAsync());
        var reconciliation = await Web06FinanceSupport.ReconciliationAsync(c, terminal);
        Assert.Equal(230_000m, reconciliation.ExpectedCash); Assert.Equal(230_000m, reconciliation.DetailExpectedCash);
        var visibleSale = Assert.Single(reconciliation.Orders); Assert.Equal(200_000m, visibleSale.GrandTotal); Assert.Equal(0m, visibleSale.CashApplied);
    }

    [Fact]
    public async Task Cash_remitted_at_real_counter_C_then_sale_at_own_counter_A_stays_only_in_C_reconciliation_without_changing_current_carts()
    {
        var c = await Web06Case.CreateAsync(fixture, shipping: 30_000m); await Web06FinanceSupport.ServiceSettingAsync(c);
        await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var counterC = await Web06FinanceSupport.OwnOpenShiftAsync(c); var counterA = await Web06FinanceSupport.OwnOpenShiftAsync(c);
        await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, counterC, 230_000m));
        Dictionary<int, int?> carts;
        await using (var db = c.Context()) carts = await db.POSShifts.ToDictionaryAsync(x => x.Id, x => x.CurrentOrderId);
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, counterA).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        var cash = await Web06FinanceSupport.ReconciliationAsync(c, counterC); var sale = await Web06FinanceSupport.ReconciliationAsync(c, counterA);
        Assert.Equal(230_000m, cash.ExpectedCash); Assert.Equal(230_000m, cash.DetailExpectedCash); Assert.Empty(cash.Orders); Assert.Single(cash.CashTransactions);
        Assert.Equal(0m, sale.ExpectedCash); Assert.Equal(0m, sale.DetailExpectedCash); Assert.Empty(sale.CashTransactions);
        var visible = Assert.Single(sale.Orders); Assert.Equal(200_000m, visible.GrandTotal); Assert.Equal(0m, visible.CashApplied); Assert.Equal(0m, visible.NonCashApplied);
        await using var check = c.Context(); Assert.Equal(carts.OrderBy(x => x.Key), (await check.POSShifts.ToDictionaryAsync(x => x.Id, x => x.CurrentOrderId)).OrderBy(x => x.Key));
        Assert.Single(await check.OrderPayments.ToListAsync()); Assert.Equal(230_000m, await check.POSShiftCashTransactions.SumAsync(x => x.Amount));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_own_shift_or_positive_fee_configuration_preserves_delivered_unposted_order(bool missingFee)
    {
        var c = await Web06Case.CreateAsync(fixture, shipping: missingFee ? 30_000m : 0m);
        await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); var total = missingFee ? 230_000m : 200_000m;
        await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, total));
        var before = await c.PhysicalSnapshotAsync(); var command = await c.RequestAsync<StorefrontOperationRequest>();
        await using var db = c.Context(); await Assert.ThrowsAsync<ConflictAppException>(() => Web06FinanceSupport.Settlement(c, db, missingFee ? terminal : 0).PostSaleAsync(c.OrderId, command));
        Assert.Equal(before, await c.PhysicalSnapshotAsync()); Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.InvoiceHeads.ToListAsync());
        Assert.Equal(StorefrontOrderPhase.Delivered, (await c.DetailAsync()).Phase);
    }

    [Fact]
    public async Task Late_financial_failure_rolls_back_then_same_scoped_context_retry_and_restart_replay_create_only_one_sale_and_invoice()
    {
        var c = await CashReadyAsync(); var terminal = await Terminal(c); var command = await c.RequestAsync<StorefrontOperationRequest>();
        var fault = new LateInvoiceWorkFault(); await using var db = c.Context(fault); var service = Web06FinanceSupport.Settlement(c, db, terminal);
        await Assert.ThrowsAsync<FinancialFixtureFault>(() => service.PostSaleAsync(c.OrderId, command)); Assert.Equal(1, fault.Hits);
        await using (var verify = c.Context()) { Assert.Empty(await verify.Orders.ToListAsync()); Assert.Empty(await verify.InvoiceHeads.ToListAsync()); Assert.Empty(await verify.Set<StorefrontInvoiceWork>().ToListAsync()); Assert.Equal(StorefrontOrderPhase.Delivered, (await verify.StorefrontOrders.SingleAsync()).Phase); }
        await service.PostSaleAsync(c.OrderId, command);
        await using var restarted = c.Context(); Assert.True((await Web06FinanceSupport.Settlement(c, restarted, terminal).PostSaleAsync(c.OrderId, command)).Replayed);
        Assert.Single(await restarted.Orders.ToListAsync()); Assert.Single(await restarted.InvoiceHeads.ToListAsync()); Assert.Single(await restarted.Set<StorefrontInvoiceWork>().ToListAsync());
        Assert.Equal(1, await restarted.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.Delivery));
    }

    [Fact]
    public async Task After_commit_invoice_failure_survives_restart_and_recovers_from_durable_work_without_second_sale_or_cash()
    {
        var c = await CashReadyAsync(); var terminal = await Terminal(c); var command = await c.RequestAsync<StorefrontOperationRequest>();
        var fault = new CrashAfterCommit();
        await using (var db = c.Context())
        {
            var ack = await Web06FinanceSupport.Settlement(c, db, terminal, invoices: fault).PostSaleAsync(c.OrderId, command);
            Assert.Equal(c.OrderId, ack.OrderId); Assert.False(ack.Replayed); Assert.NotEmpty(ack.Version); Assert.Equal(1, fault.Calls);
        }
        await using (var db = c.Context()) { Assert.Equal(OrderStatus.Completed, (await db.Orders.SingleAsync()).Status); Assert.Null((await db.Set<StorefrontInvoiceWork>().SingleAsync()).CompletedAtUtc); Assert.Empty(await db.InvoiceHeads.ToListAsync()); }
        await using (var db = c.Context()) Assert.True((await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, command)).Replayed);
        await using var check = c.Context(); Assert.Single(await check.Orders.ToListAsync()); Assert.Single(await check.InvoiceHeads.ToListAsync()); Assert.NotNull((await check.Set<StorefrontInvoiceWork>().SingleAsync()).CompletedAtUtc);
        Assert.Equal(200_000m, (await check.POSShifts.SingleAsync()).CashInTotal); Assert.Single(await check.POSShiftCashTransactions.ToListAsync()); Assert.Single(await check.Set<StorefrontSaleBinding>().ToListAsync());
    }
    private async Task<Web06Case> CashReadyAsync()
    {
        var c = await Web06Case.CreateAsync(fixture); await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await Web06FinanceSupport.DeliveredAsync(c);
        await Web06FinanceSupport.CollectAllAsync(c); var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c);
        await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 200_000m)); return c;
    }
    private static async Task<int> Terminal(Web06Case c) { await using var db = c.Context(); return (await db.POSTerminals.SingleAsync()).Id; }
    private sealed class FinancialFixtureFault : Exception { }
    private sealed class CrashAfterCommit : IStorefrontInvoiceProcessor { internal int Calls; public Task ProcessAsync(int id, CancellationToken ct = default) { Calls++; throw new FinancialFixtureFault(); } }
    private sealed class LateInvoiceWorkFault : SaveChangesInterceptor
    {
        internal int Hits;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { if (Hits == 0 && data.Context!.ChangeTracker.Entries<StorefrontInvoiceWork>().Any(x => x.State == EntityState.Added)) { Hits++; throw new FinancialFixtureFault(); } return ValueTask.FromResult(result); }
    }
}
