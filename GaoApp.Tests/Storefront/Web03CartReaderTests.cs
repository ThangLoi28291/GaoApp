using System.Globalization;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Storefront.Web.Models;
using GaoApp.Storefront.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb03"), Trait("Category", "StorefrontWeb03")]
public sealed class Web03CartReaderTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("0")][InlineData("-1")][InlineData("0.00001")][InlineData("1.23456")][InlineData("1000000.0001")][InlineData("1000001")]
    public async Task Quantity_must_be_positive_bounded_and_at_most_four_decimals(string quantity)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context(); var reader = new StorefrontCartReader(new StorefrontCatalogService(db));
        await Assert.ThrowsAsync<ValidationAppException>(() => reader.ResolveAsync(new() { Items = [new() { ProductId = c.ProductId, VariantId = c.VariantId, UnitId = c.UnitId, Quantity = decimal.Parse(quantity, CultureInfo.InvariantCulture) }] }));
    }
    [Theory]
    [InlineData("0.0001")][InlineData("1.2345")][InlineData("1000000")]
    public async Task Fractional_and_boundary_quantities_resolve_exact_selling_unit_price_without_reservations(string quantity)
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.EnableSyntheticCatalogAsync(); await using var db = c.Context();
        await c.Products(db).SaveAsync(c.ProductId, c.Publication(baseUnit: false, pack: pack.Conversion));
        db.InventoryBalances.Add(new() { StoreId = c.StoreId, WarehouseId = c.WarehouseId, ProductVariantId = c.VariantId, OnHandQty = -3, ReservedQty = 2 }); await db.SaveChangesAsync();
        var before = await c.SideEffectsAsync(); var q = decimal.Parse(quantity, CultureInfo.InvariantCulture);
        var resolved = await new StorefrontCartReader(new StorefrontCatalogService(db)).ResolveAsync(new() { Items = [new() { ProductId = c.ProductId, VariantId = c.VariantId, UnitId = pack.Unit, Quantity = q }] });
        var line = Assert.Single(resolved.Items); Assert.True(line.Available); Assert.True(resolved.CanContinue); Assert.Equal(q, line.Quantity); Assert.Equal(180000, line.UnitPrice);
        Assert.Equal(decimal.Round(180000m * q, 0, MidpointRounding.AwayFromZero), line.LineAmount); Assert.Equal(line.LineAmount, resolved.GoodsEstimate);
        Assert.Equal(before, await c.SideEffectsAsync()); Assert.Equal(-3, (await db.InventoryBalances.SingleAsync()).OnHandQty); Assert.Equal(2, (await db.InventoryBalances.SingleAsync()).ReservedQty);
    }
    [Theory]
    [InlineData("nullItems")][InlineData("duplicate")][InlineData("overLimit")][InlineData("productZero")][InlineData("variantZero")][InlineData("unitZero")]
    public async Task Invalid_cart_shape_is_rejected_before_any_product_lookup(string input)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context(); var request = new StorefrontCartRequest { Items = [new() { ProductId = c.ProductId, VariantId = c.VariantId, UnitId = c.UnitId, Quantity = 1 }] };
        switch (input)
        {
            case "nullItems": request.Items = null!; break;
            case "duplicate": request.Items.Add(request.Items[0]); break;
            case "overLimit": request.Items = Enumerable.Range(1, 101).Select(x => new StorefrontCartSelection { ProductId = x, VariantId = x, UnitId = x, Quantity = 1 }).ToList(); break;
            case "productZero": request.Items[0].ProductId = 0; break;
            case "variantZero": request.Items[0].VariantId = 0; break;
            case "unitZero": request.Items[0].UnitId = 0; break;
        }
        await Assert.ThrowsAsync<ValidationAppException>(() => new StorefrontCartReader(new StorefrontCatalogService(db)).ResolveAsync(request));
    }
    [Fact]
    public async Task Revalidation_replaces_stale_prices_and_hidden_or_foreign_choices_are_unavailable_without_private_names()
    {
        var c = await fixture.CaseAsync(); var foreign = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.EnableSyntheticCatalogAsync();
        await using var db = c.Context(); await c.Products(db).SaveAsync(c.ProductId, c.Publication(baseUnit: false, pack: pack.Conversion));
        var request = new StorefrontCartRequest { Items = [new() { ProductId = c.ProductId, VariantId = c.VariantId, UnitId = pack.Unit, Quantity = 1.2345m }] };
        var reader = new StorefrontCartReader(new StorefrontCatalogService(db)); Assert.Equal(180000, Assert.Single((await reader.ResolveAsync(request)).Items).UnitPrice);
        await db.ProductUnitConversions.Where(x => x.Id == pack.Conversion).ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, 190000));
        var fresh = await reader.ResolveAsync(request); Assert.Equal(190000, Assert.Single(fresh.Items).UnitPrice); Assert.Equal(234555, fresh.GoodsEstimate);
        await db.StorefrontProducts.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsPublished, false));
        request.Items.Add(new() { ProductId = foreign.ProductId, VariantId = foreign.VariantId, UnitId = foreign.UnitId, Quantity = 1 });
        var hidden = await reader.ResolveAsync(request); Assert.False(hidden.CanContinue); Assert.Equal(0, hidden.GoodsEstimate);
        Assert.All(hidden.Items, line => { Assert.False(line.Available); Assert.Null(line.ProductName); Assert.Null(line.UnitPrice); Assert.Null(line.LineAmount); Assert.NotEmpty(line.Message); });
    }
    [Fact]
    public async Task Empty_cart_is_not_ready_to_continue_and_server_response_is_an_estimate_without_shipping_or_order_creation()
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        var response = await new StorefrontCartReader(new StorefrontCatalogService(db)).ResolveAsync(new()); Assert.Empty(response.Items); Assert.False(response.CanContinue); Assert.Equal(0, response.GoodsEstimate);
        Assert.Contains("chưa gồm phí giao", response.Notice); Assert.Empty(await db.StorefrontOrders.ToListAsync());
    }
    [Fact]
    public async Task VND_is_rounded_per_line_away_from_zero_before_summing_fractional_quantity_lines()
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync();
        var first = await c.AddProductAsync("WEB03 rounding A", price: 25000); var second = await c.AddProductAsync("WEB03 rounding B", price: 25000);
        await using var db = c.Context(); var response = await new StorefrontCartReader(new StorefrontCatalogService(db)).ResolveAsync(new() { Items = [
            new() { ProductId = first.ProductId, VariantId = first.VariantId, UnitId = first.UnitId, Quantity = 0.0001m },
            new() { ProductId = second.ProductId, VariantId = second.VariantId, UnitId = second.UnitId, Quantity = 0.0001m }] });
        Assert.Equal(2, response.Items.Count); Assert.All(response.Items, line => Assert.Equal(3, line.LineAmount)); Assert.Equal(6, response.GoodsEstimate);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task SQL_prices_cannot_produce_a_line_or_running_total_at_or_above_the_money_limit(bool sum)
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); var first = await c.AddProductAsync("WEB03 high A", price: sum ? 6000000000000000m : 5000000000000000m);
        var selections = new List<StorefrontCartSelection> { new() { ProductId = first.ProductId, VariantId = first.VariantId, UnitId = first.UnitId, Quantity = sum ? 1 : 2 } };
        if (sum) { var second = await c.AddProductAsync("WEB03 high B", price: 6000000000000000m); selections.Add(new() { ProductId = second.ProductId, VariantId = second.VariantId, UnitId = second.UnitId, Quantity = 1 }); }
        await using var db = c.Context(); await Assert.ThrowsAsync<ValidationAppException>(() => new StorefrontCartReader(new StorefrontCatalogService(db)).ResolveAsync(new() { Items = selections }));
    }
    [Fact]
    public async Task Formatted_server_money_keeps_integer_digits_above_the_JavaScript_exact_integer_range()
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); var product = await c.AddProductAsync("WEB03 exact integer", price: 9007199254740993m);
        await using var db = c.Context(); var response = await new StorefrontCartReader(new StorefrontCatalogService(db)).ResolveAsync(new() { Items = [new() { ProductId = product.ProductId, VariantId = product.VariantId, UnitId = product.UnitId, Quantity = 1 }] });
        Assert.Equal(9007199254740993m, response.GoodsEstimate); Assert.Equal("9.007.199.254.740.993 đ", response.GoodsEstimateText);
        Assert.Equal("9.007.199.254.740.993 đ", Assert.Single(response.Items).UnitPriceText); Assert.Equal("9.007.199.254.740.993 đ", Assert.Single(response.Items).LineAmountText);
    }
    [Fact]
    public async Task Decimal_overflow_from_a_catalog_source_becomes_validation_instead_of_wrapping()
    {
        var request = new StorefrontCartRequest { Items = [new() { ProductId = 1, VariantId = 1, UnitId = 1, Quantity = 2 }] };
        await Assert.ThrowsAsync<ValidationAppException>(() => new StorefrontCartReader(new OverflowCatalog()).ResolveAsync(request));
    }
    private sealed class OverflowCatalog : IStorefrontCatalogService
    {
        public Task<StorefrontCatalogProduct?> GetAsync(int id, CancellationToken ct = default) => Task.FromResult<StorefrontCatalogProduct?>(new(id, "Overflow fixture", null, null, 0,
            [new(1, "Variant", [new(1, 1, null, "Unit", 1, decimal.MaxValue, null)])], []));
        public Task<StorefrontCatalogPage> SearchAsync(StorefrontCatalogQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StorefrontCatalogUnit?> ResolveAsync(int variantId, int unitId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetGroupsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StorefrontCatalogMediaSource?> GetMediaAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
