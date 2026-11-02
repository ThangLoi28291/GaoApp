using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05PricingAndAuthorityTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("discount")][InlineData("gift")]
    public async Task QR_amount_uses_real_retail_POS_promotion_output_and_shipping_including_actual_gift_lines(string program)
    {
        var c = await fixture.Web05Async();
        await Web04PromotionTests.Promotion(c.Web, program == "gift"
            ? new() { Name = "WEB05 actual gift", Type = PromotionType.BuyXGetY, BuyQuantity = 2, GetQuantity = 1, RequireGiftQuantityInCart = true,
                Items = [new() { ProductId = c.Seed.ProductId, VariantId = c.Seed.VariantId }] }
            : new() { Name = "WEB05 actual discount", Type = PromotionType.ProductDiscount, DiscountType = PromotionDiscountType.Percentage, DiscountValue = 15,
                Items = [new() { ProductId = c.Seed.ProductId, VariantId = c.Seed.VariantId }] });
        var request = c.Request(); request.Items[0].Quantity = 3; Web04PosResult pos;
        await using (var db = c.Context()) pos = await Web04PosOracle.PriceAsync(db, request.Items);
        var quote = await c.QuoteAsync(request); Assert.Equal(pos.Order.GrandTotal, quote.GoodsAmount); Assert.Equal(pos.Order.GrandTotal + quote.ShippingCharge, quote.TotalDue);
        Assert.Equal(pos.Approved.Sum(x => x.Net), quote.Lines.Sum(x => x.NetAmount));
        Assert.Equal(pos.Order.Lines.Count(x => x.IsPromotionGift && !x.IsDeleted), quote.Lines.Count(x => x.IsGift));
        if (program == "gift") Assert.Single(quote.Lines, x => x.IsGift);
        var placed = await c.PlaceAsync(Web04Case.PlaceRequest(quote)); Assert.Equal(StorefrontPlacementStatus.Placed, placed.Status);
        await using var check = c.Context(); Assert.Equal(quote.TotalDue, (await check.Set<StorefrontPaymentIntent>().SingleAsync()).AmountDue);
        Assert.Equal(quote.Lines.Sum(x => x.BaseQuantity), await check.InventoryReservations.SumAsync(x => x.ReservedQty));
        Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync());
    }

    [Theory]
    [InlineData("4000", "1")][InlineData("25000", "5")]
    public async Task QR_total_preserves_actual_POS_aggregate_rounding_instead_of_cart_estimate_rounding(string priceText, string goodsText)
    {
        var c = await fixture.Web05Async(); var price = decimal.Parse(priceText, System.Globalization.CultureInfo.InvariantCulture); await Web04PromotionTests.SetBasePrice(c.Web, price);
        var second = await c.Seed.AddProductAsync("WEB05 fractional second", price: price);
        await using (var db = c.Context()) await db.ProductVariants.Where(x => x.Id == second.VariantId).ExecuteUpdateAsync(x => x.SetProperty(v => v.HasInputInvoice, true));
        var request = c.Request(); request.Items[0].Quantity = .0001m; request.Items.Add(new() { ProductId = second.ProductId, VariantId = second.VariantId, UnitId = second.UnitId, Quantity = .0001m });
        Web04PosResult pos; await using (var db = c.Context()) pos = await Web04PosOracle.PriceAsync(db, request.Items);
        var quote = await c.QuoteAsync(request); Assert.Equal(decimal.Parse(goodsText, System.Globalization.CultureInfo.InvariantCulture), quote.GoodsAmount); Assert.Equal(pos.Order.GrandTotal, quote.GoodsAmount);
        Assert.Equal(quote.GoodsAmount, quote.Lines.Sum(x => x.NetAmount)); Assert.All(quote.Lines, x => Assert.Equal(price, x.UnitPrice));
        await c.PlaceAsync(Web04Case.PlaceRequest(quote)); await using var check = c.Context(); Assert.Equal(quote.GoodsAmount + 20_000m, (await check.Set<StorefrontPaymentIntent>().SingleAsync()).AmountDue);
    }

    [Fact]
    public async Task Unused_candidate_stock_owner_without_collection_authority_does_not_block_authorized_actual_A_only_QR()
    {
        var c = await fixture.Web05Async(); await AddCandidate(c); var quote = await c.QuoteAsync(); var placed = await c.PlaceAsync(Web04Case.PlaceRequest(quote));
        Assert.Equal(StorefrontPlacementStatus.Placed, placed.Status); await using var db = c.Context();
        var source = Assert.Single(await db.Set<StorefrontFulfillmentSource>().ToListAsync()); Assert.Equal(c.Seed.LegalEntityId, source.SourceLegalEntityId);
        Assert.Equal(c.Seed.WarehouseId, Assert.Single(await db.InventoryReservations.ToListAsync()).WarehouseId);
        Assert.Single(await db.Set<StorefrontPaymentIntent>().ToListAsync());
    }

    [Fact]
    public async Task Reallocation_to_unauthorized_candidate_after_quote_cannot_leave_QR_order_intent_or_holds()
    {
        var c = await fixture.Web05Async(); await AddCandidate(c); var quote = await c.QuoteAsync();
        await using (var db = c.Context()) await db.InventoryBalances.Where(x => x.WarehouseId == c.Seed.WarehouseId).ExecuteUpdateAsync(x => x.SetProperty(b => b.OnHandQty, 0));
        await Assert.ThrowsAsync<ValidationAppException>(() => c.PlaceAsync(Web04Case.PlaceRequest(quote)));
        await using var check = c.Context(); Assert.Empty(await check.StorefrontOrders.ToListAsync()); Assert.Empty(await check.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Empty(await check.InventoryReservations.ToListAsync());
        Assert.All(await check.InventoryBalances.ToListAsync(), x => Assert.Equal(0, x.ReservedQty));
    }

    private static async Task AddCandidate(Web05Case c)
    {
        await using var db = c.Context(); var owner = await db.LegalEntities.SingleAsync(x => x.Id == c.Seed.LegalEntityId); owner.SalePriority = 1; owner.DefaultWarehouseId = c.Seed.WarehouseId;
        var other = new LegalEntity { StoreId = c.StoreId, Code = "W05-UNUSED", Name = "Unused candidate owner", LegalName = "Unused candidate owner", IsActive = true, SalePriority = 2 }; db.Add(other); await db.SaveChangesAsync();
        var warehouse = new Warehouse { StoreId = c.StoreId, LegalEntityId = other.Id, Code = "W05-UNUSED", Name = "Candidate source", IsActive = true, AllowNegativeInventory = true }; db.Add(warehouse); await db.SaveChangesAsync();
        other.DefaultWarehouseId = warehouse.Id; var store = await db.Stores.SingleAsync(x => x.Id == c.StoreId); store.IsMultiLegalEntityEnabled = true; store.MultiLegalEntityActivatedAtUtc = DateTime.UtcNow.AddDays(-1);
        db.InventoryBalances.Add(new() { StoreId = c.StoreId, WarehouseId = warehouse.Id, ProductVariantId = c.Seed.VariantId, OnHandQty = 100 }); await db.SaveChangesAsync();
        Assert.False(await db.Set<StorefrontBankCollectionAuthority>().AnyAsync(x => x.SourceLegalEntityId == other.Id && x.IsActive));
    }
}
