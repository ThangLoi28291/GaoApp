using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06InvoiceEligibilityTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_COD_two_goods_owners_require_invoice_provider_only_for_eligible_allocations_and_keep_ineligible_owner_physical_source()
    {
        Web03Product? second = null; int secondOwner = 0;
        var c = await Web06Case.CreateAsync(fixture, quantity: 1, multipleSources: true, beforeQuote: async web =>
        {
            second = await web.Seed.AddProductAsync("WEB06 ineligible physical owner B item", price: 30_000m);
            await using var db = web.Context(); var warehouse = await db.Warehouses.SingleAsync(x => x.Id != web.Seed.WarehouseId); secondOwner = warehouse.LegalEntityId;
            Assert.False((await db.ProductVariants.SingleAsync(x => x.Id == second.VariantId)).HasInputInvoice);
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(new InventoryMovementFactory().CreateAdjustmentIncrease(warehouse.Id, second.VariantId, 2, 20_000m, "WEB06 actual separate ineligible SKU stock"));
            await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(web, [web.Seed.LegalEntityId]);
        }, configureRequest: request => request.Items.Add(new() { ProductId = second!.ProductId, VariantId = second.VariantId, UnitId = second.UnitId, Quantity = 1 }));
        await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c); var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c);
        await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 80_000m));
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        await using var check = c.Context(); var sale = await check.Orders.Include(x => x.Lines).Include(x => x.LegalEntityAllocations).SingleAsync();
        Assert.Equal(80_000m, sale.GrandTotal); Assert.Equal(2, sale.LegalEntityAllocations.Select(x => x.LegalEntityId).Distinct().Count());
        Assert.Contains(sale.LegalEntityAllocations, x => x.LegalEntityId == secondOwner && x.ProductVariantId == second!.VariantId && x.BaseQuantity == 1);
        Assert.False(sale.Lines.Single(x => x.VariantId == second!.VariantId).HasInputInvoiceSnapshot); Assert.True(sale.Lines.Single(x => x.VariantId == c.Seed.VariantId).HasInputInvoiceSnapshot);
        Assert.Null((await check.LegalEntities.SingleAsync(x => x.Id == secondOwner)).InvoiceProviderSettingId);
        var invoice = Assert.Single(await check.InvoiceHeads.Include(x => x.Details).ToListAsync()); Assert.Equal(c.Seed.LegalEntityId, invoice.LegalEntityId); Assert.Equal(50_000m, invoice.GrandTotal);
        Assert.Single(invoice.Details); Assert.DoesNotContain(invoice.Details, x => x.ProductVariantId == second!.VariantId); Assert.Equal(2, await check.DeliveryDispatchCostFragments.CountAsync());
        Assert.NotNull((await check.Set<StorefrontInvoiceWork>().SingleAsync()).CompletedAtUtc); Assert.Equal(80_000m, (await check.POSShifts.SingleAsync()).CashInTotal);
    }
}
