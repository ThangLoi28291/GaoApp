using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb04"), Trait("Category", "StorefrontWeb04")]
public sealed class Web04AllocationTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false, 4, 2)][InlineData(true, 6, 4)]
    public async Task Actual_multi_entity_allocator_splits_holds_by_priority_and_assigns_allowed_shortage_only_to_last_source(bool negative, decimal quantity, decimal secondHeld)
    {
        var c = await fixture.Web04Async(); await c.SettingsAsync(negative: negative); var second = await Sources(c);
        var quote = await c.QuoteAsync(c.Request(quantity)); Assert.Equal(negative, quote.HasInventoryShortage); var result = await c.PlaceAsync(Web04Case.PlaceRequest(quote)); Assert.Equal(StorefrontPlacementStatus.Placed, result.Status);
        await using var db = c.Context(); var holds = await db.InventoryReservations.ToListAsync(); Assert.Equal(2, holds.Count);
        Assert.Equal(2, holds.Single(x => x.WarehouseId == c.Seed.WarehouseId).ReservedQty); Assert.Equal(secondHeld, holds.Single(x => x.WarehouseId == second.Warehouse).ReservedQty);
        Assert.All(holds, x => Assert.Equal(InventoryReferenceType.Storefront, x.ReferenceType)); Assert.Equal(quantity, holds.Sum(x => x.ReservedQty));
        var sources = await db.Set<StorefrontFulfillmentSource>().ToListAsync(); Assert.Equal(2, sources.Count); Assert.Equal(second.Legal, sources.Single(x => x.SourceWarehouseId == second.Warehouse).SourceLegalEntityId);
        Assert.Equal(2, (await db.InventoryBalances.SingleAsync(x => x.WarehouseId == c.Seed.WarehouseId)).OnHandQty); Assert.Equal(3, (await db.InventoryBalances.SingleAsync(x => x.WarehouseId == second.Warehouse)).OnHandQty);
        Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
        var line = await db.Set<StorefrontOrderLine>().SingleAsync(); Assert.Equal(quantity, line.BaseQuantity); Assert.Equal(quantity * 25000, line.NetAmount);
    }
    [Fact]
    public async Task Source_priority_change_after_quote_requires_requote_before_any_allocation_is_reserved()
    {
        var c = await fixture.Web04Async(); await Sources(c); var quote = await c.QuoteAsync(c.Request(4));
        await using (var db = c.Context()) await db.LegalEntities.ExecuteUpdateAsync(x => x.SetProperty(e => e.SalePriority, e => e.SalePriority + 10));
        var before = await c.CountsAsync(); var result = await c.PlaceAsync(Web04Case.PlaceRequest(quote)); Assert.Equal(StorefrontPlacementStatus.QuoteChanged, result.Status); Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Pack_only_publication_is_enforced_at_authoritative_quote_without_fabricating_base_conversion()
    {
        var c = await fixture.Web04Async(); await using (var db = c.Context())
        {
            var edit = (await c.Seed.Products(db).GetAsync(c.Seed.ProductId)).Product; edit.Variants.Single().BaseUnitIsPublished = false; await c.Seed.Products(db).SaveAsync(c.Seed.ProductId, edit);
        }
        await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync()); var quote = await c.QuoteAsync(c.Request(1.2345m, pack: true));
        Assert.Equal(1.2345m, quote.Lines.Single().Quantity); Assert.Equal(14.814m, quote.Lines.Single().BaseQuantity); Assert.Equal(180000, quote.Lines.Single().UnitPrice);
    }
    [Fact]
    public async Task Free_shipping_uses_actual_net_promoted_goods_and_cannot_bypass_required_kg()
    {
        var c = await fixture.Web04Async(); await c.RateAsync(free: 25000);
        await Web04PromotionTests.Promotion(c, new() { Name = "Net goods20", Type = PromotionType.ProductDiscount, DiscountType = PromotionDiscountType.Percentage, DiscountValue = 20, Items = [new() { ProductId = c.Seed.ProductId }] });
        var quote = await Web04PromotionTests.Parity(c, c.Request()); Assert.Equal(20000, quote.GoodsAmount); Assert.Equal(20000, quote.ShippingCharge);
        await c.SettingsAsync(kg: true); await c.RateAsync(free: 1); await using (var db = c.Context()) await db.StorefrontVariants.ExecuteUpdateAsync(x => x.SetProperty(v => v.BaseUnitWeightKg, (decimal?)null));
        var before = await c.CountsAsync(); await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync()); Assert.Equal(before, await c.CountsAsync());
    }
    private static async Task<(int Legal, int Warehouse)> Sources(Web04Case c)
    {
        await using var db = c.Context(); var legal = await db.LegalEntities.SingleAsync(x => x.Id == c.Seed.LegalEntityId); legal.SalePriority = 1; legal.DefaultWarehouseId = c.Seed.WarehouseId;
        var other = new LegalEntity { StoreId = c.StoreId, Code = "W04-SECOND", Name = "Second fixture source", LegalName = "Second fixture source", IsActive = true, SalePriority = 2 }; db.Add(other); await db.SaveChangesAsync();
        var warehouse = new Warehouse { StoreId = c.StoreId, LegalEntityId = other.Id, Code = "W04-SECOND", Name = "Second fixture warehouse", IsActive = true, AllowNegativeInventory = true }; db.Add(warehouse); await db.SaveChangesAsync();
        other.DefaultWarehouseId = warehouse.Id; var store = await db.Stores.SingleAsync(x => x.Id == c.StoreId); store.IsMultiLegalEntityEnabled = true; store.MultiLegalEntityActivatedAtUtc = DateTime.UtcNow.AddDays(-1);
        (await db.InventoryBalances.SingleAsync()).OnHandQty = 2; db.InventoryBalances.Add(new() { StoreId = c.StoreId, WarehouseId = warehouse.Id, ProductVariantId = c.Seed.VariantId, OnHandQty = 3 }); await db.SaveChangesAsync();
        return (other.Id, warehouse.Id);
    }
}
