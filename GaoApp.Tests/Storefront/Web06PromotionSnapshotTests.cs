using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06PromotionSnapshotTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_shared_engine_normal_discount_and_gift_keep_distinct_roles_IDs_components_and_old_names_through_sale()
    {
        int normalId = 0, giftId = 0;
        var c = await Web06Case.CreateAsync(fixture, quantity: 2, unitPrice: 100_000m, multipleSources: true, primaryMultiStock: 2,
            beforeQuote: async web =>
            {
                normalId = (await Web04PromotionTests.Promotion(web, new() { Name = "WEB06 sealed lower-ID normal discount", Type = PromotionType.ProductDiscount,
                    DiscountType = PromotionDiscountType.Percentage, DiscountValue = 10, Items = [new() { ProductId = web.Seed.ProductId, VariantId = web.Seed.VariantId }] })).Id;
                giftId = (await Web04PromotionTests.Promotion(web, new() { Name = "WEB06 sealed higher-ID gift", Type = PromotionType.BuyXGetY,
                    BuyQuantity = 2, GetQuantity = 1, RequireGiftQuantityInCart = true, Items = [new() { ProductId = web.Seed.ProductId, VariantId = web.Seed.VariantId }] })).Id;
            });
        Assert.True(normalId < giftId);
        await Web06Support.AssertPurchasedTwoGiftParityAsync(c, 180_000m);
        await using (var db = c.Context())
        {
            var lines = await db.Set<StorefrontOrderLine>().ToListAsync(); Assert.Equal(2, lines.Count); Assert.Single(lines, x => x.IsGift); Assert.Equal(180_000m, lines.Sum(x => x.NetAmount)); Assert.Equal(3m, lines.Sum(x => x.BaseQuantity));
            await db.Promotions.Where(x => x.Id == normalId).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false).SetProperty(p => p.Name, "Current renamed discount").SetProperty(p => p.DiscountValue, 100m));
            await db.Promotions.Where(x => x.Id == giftId).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false).SetProperty(p => p.Name, "Current renamed gift").SetProperty(p => p.BuyQuantity, 100m));
        }
        await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 180_000m));
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        await using var check = c.Context(); var sale = await check.Orders.Include(x => x.Lines).SingleAsync(); var normal = Assert.Single(sale.Lines, x => !x.IsPromotionGift); var gift = Assert.Single(sale.Lines, x => x.IsPromotionGift);
        Assert.Equal(normalId, normal.PromotionId); Assert.Equal(PromotionType.ProductDiscount, normal.PromotionType); Assert.Equal(20_000m, normal.PromotionDiscount); Assert.Equal(0m, normal.LineDiscount); Assert.Equal(180_000m, normal.LineTotal);
        Assert.Equal("WEB06 sealed lower-ID normal discount", normal.PromotionName); Assert.Equal(giftId, gift.GiftPromotionId); Assert.Equal("WEB06 sealed higher-ID gift", gift.GiftPromotionName);
        Assert.Equal(normal.Id, gift.GiftSourceLineId); Assert.Equal(1m, gift.Quantity); Assert.Equal(0m, gift.LineTotal); Assert.Equal(180_000m, sale.GrandTotal); Assert.Equal(20_000m, sale.PromotionDiscountTotal);
        Assert.Equal(3m, sale.Lines.Sum(x => x.BaseQuantity)); Assert.Equal(40_000m, sale.Lines.Sum(x => x.LineCostTotal)); Assert.Equal(2, await check.InvoiceHeads.CountAsync());
        var zero = await check.InvoiceHeads.Include(x => x.Details).SingleAsync(x => x.GrandTotal == 0); Assert.Equal(InvoiceProviderStatus.LocalDraft, zero.ProviderStatus);
        Assert.All(zero.Details, x => Assert.NotNull(x.OrderLegalEntityAllocationId)); // Genuine owner B gift source; no provider-issued zero invoice claim.
    }
}
