using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06InvoiceUnitTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Nonterminating_factor_three_source_split_uses_actual_base_invoice_unit_without_changing_original_selling_line()
    {
        var c = await Web06Case.CreateAsync(fixture, usePack: true, packFactor: 3, quantity: 1, multipleSources: true, primaryMultiStock: 1);
        await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 150_000m));
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        await using var check = c.Context(); var line = await check.OrderLines.SingleAsync(); Assert.Equal(1m, line.Quantity); Assert.Equal(3m, line.Multiplier); Assert.Equal(c.Checkout.PackUnit, line.SellingUnitId); Assert.Equal(150_000m, line.UnitPrice);
        var allocations = await check.OrderLegalEntityAllocations.OrderBy(x => x.BaseQuantity).ToListAsync(); Assert.Equal(new[] { 1m, 2m }, allocations.Select(x => x.Quantity));
        Assert.All(allocations, x => { Assert.Equal(OrderAllocationInvoiceUnitKind.NativeBaseUnit, x.InvoiceUnitKind); Assert.Equal(c.Seed.UnitId, x.InvoiceUnitId); Assert.Equal(x.BaseQuantity, x.Quantity); });
        var details = await check.InvoiceDetails.OrderBy(x => x.Quantity).ToListAsync(); Assert.Equal(new[] { 1m, 2m }, details.Select(x => x.Quantity)); Assert.All(details, x => Assert.NotNull(x.OrderLegalEntityAllocationId));
        Assert.Equal(150_000m, details.Sum(x => x.TotalAmount)); Assert.Equal(150_000m, await check.InvoiceHeads.SumAsync(x => x.GrandTotal));
    }

    [Fact]
    public async Task Representable_four_decimal_pack_quantity_remains_real_selling_unit_in_allocation_and_invoice()
    {
        var c = await Web06Case.CreateAsync(fixture, usePack: true, packFactor: 100, quantity: .0001m);
        await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 500m));
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        await using var check = c.Context(); var allocation = await check.OrderLegalEntityAllocations.SingleAsync(); Assert.Equal(OrderAllocationInvoiceUnitKind.SellingUnit, allocation.InvoiceUnitKind);
        Assert.Equal(c.Checkout.PackUnit, allocation.InvoiceUnitId); Assert.Equal(.0001m, allocation.Quantity); Assert.Equal(.01m, allocation.BaseQuantity);
        Assert.Equal(.0001m, (await check.InvoiceDetails.SingleAsync()).Quantity); Assert.Equal(.0001m, (await check.InvoiceHeads.SingleAsync()).TotalQuantity); Assert.Equal(500m, (await check.InvoiceHeads.SingleAsync()).GrandTotal);
    }
}
