using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb04"), Trait("Category", "StorefrontWeb04")]
public sealed class Web04QuoteTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Quote_uses_POS_positive_retail_override_and_server_address_shipping_without_reserving_or_financial_effects()
    {
        var c = await fixture.Web04Async(); var before = await c.CountsAsync(); var quote = await c.QuoteAsync();
        Assert.NotEqual(Guid.Empty, quote.Token); Assert.Equal(64, quote.Fingerprint.Length); Assert.True(quote.ExpiresAtUtc > c.Clock.Utc);
        Assert.Equal(25000, quote.GoodsAmount); Assert.Equal(20000, quote.ShippingCharge); Assert.Equal(45000, quote.TotalDue); Assert.Equal(StorefrontDeliveryMethod.Local, quote.DeliveryMethod);
        Assert.Equal("Tỉnh thử nghiệm A", quote.Recipient.ProvinceName); Assert.Equal("Phường thử nghiệm A1", quote.Recipient.WardName);
        var line = Assert.Single(quote.Lines); Assert.Equal(25000, line.UnitPrice); Assert.Equal(1, line.BaseMultiplier); Assert.False(line.IsGift);
        Assert.Equal(before, await c.CountsAsync()); var json = JsonSerializer.Serialize(quote);
        Assert.DoesNotContain("CostPrice", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("SourceWarehouseId", json, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Pack_price_base_quantity_and_shipping_kg_each_convert_exactly_once()
    {
        var c = await fixture.Web04Async(); var quote = await c.QuoteAsync(c.Request(2, pack: true)); var line = Assert.Single(quote.Lines);
        Assert.Equal(180000, line.UnitPrice); Assert.Equal(360000, quote.GoodsAmount); Assert.Equal(12, line.BaseMultiplier); Assert.Equal(24, line.BaseQuantity);
        Assert.Equal(1.4m, line.WeightKg); Assert.Equal(2.8m, quote.TotalWeightKg); Assert.Equal(380000, quote.TotalDue);
    }
    [Theory]
    [InlineData("1", 20000)][InlineData("1.0001", 25000)][InlineData("1.5", 25000)][InlineData("1.5001", 30000)]
    public async Task Kg_fee_obeys_exact_ceil_boundaries_of_the_actual_selling_unit(string weight, decimal expected)
    {
        var c = await fixture.Web04Async(); await c.SettingsAsync(kg: true);
        await using (var db = c.Context()) await db.StorefrontVariants.ExecuteUpdateAsync(x => x.SetProperty(v => v.BaseUnitWeightKg, decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture)));
        var quote = await c.QuoteAsync(); Assert.Equal(expected, quote.ShippingCharge); Assert.Equal(quote.GoodsAmount + expected, quote.TotalDue);
    }
    [Fact]
    public async Task Kg_is_summed_across_lines_before_ceil_and_fixed_mode_preserves_missing_weight()
    {
        var c = await fixture.Web04Async(); var second = await c.Seed.AddProductAsync("WEB04 kg second"); await c.SettingsAsync(kg: true);
        await using (var db = c.Context()) await db.StorefrontVariants.ExecuteUpdateAsync(x => x.SetProperty(v => v.BaseUnitWeightKg, 0.50005m));
        var request = c.Request(); request.Items.Add(new() { ProductId = second.ProductId, VariantId = second.VariantId, UnitId = second.UnitId, Quantity = 1 });
        var quote = await c.QuoteAsync(request); Assert.Equal(1.0001m, quote.TotalWeightKg); Assert.Equal(25000, quote.ShippingCharge);
        await c.SettingsAsync(kg: false); await using (var db = c.Context()) await db.StorefrontVariants.ExecuteUpdateAsync(x => x.SetProperty(v => v.BaseUnitWeightKg, (decimal?)null));
        quote = await c.QuoteAsync(); Assert.Null(Assert.Single(quote.Lines).WeightKg); Assert.Null(quote.TotalWeightKg); Assert.Equal(20000, quote.ShippingCharge);
    }
    [Theory]
    [InlineData(24999, 0)][InlineData(25000, 0)][InlineData(25001, 20000)]
    public async Task Free_fee_is_inclusive_on_net_goods_and_excludes_shipping(decimal threshold, decimal expected)
    {
        var c = await fixture.Web04Async(); await c.RateAsync(free: threshold); var quote = await c.QuoteAsync(); Assert.Equal(25000, quote.GoodsAmount); Assert.Equal(expected, quote.ShippingCharge);
    }
    [Fact]
    public async Task Out_of_local_address_uses_national_fee_and_specific_local_ward_can_override_province_scope()
    {
        var c = await fixture.Web04Async(); var request = c.Request(); request.ProvinceCode = "02"; request.WardCode = "02001";
        var national = await c.QuoteAsync(request); Assert.Equal(StorefrontDeliveryMethod.Nationwide, national.DeliveryMethod); Assert.Equal(30000, national.ShippingCharge);
        await using (var db = c.Context())
        {
            var special = await c.Seed.ZoneAsync("WARD", "01", "01001"); special.BaseFee = 12000; special.Name = "Phường riêng fixture"; await c.Seed.FullService(db).SaveZoneAsync(special);
        }
        var local = await c.QuoteAsync(); Assert.Equal(12000, local.ShippingCharge); Assert.Equal("Phường riêng fixture", local.ShippingZoneName);
    }
    [Theory]
    [InlineData("name")][InlineData("nameLong")][InlineData("phone")][InlineData("street")][InlineData("streetLong")][InlineData("noteLong")]
    [InlineData("province")][InlineData("ward")][InlineData("crossProvince")][InlineData("itemsNull")][InlineData("duplicate")]
    [InlineData("zero")][InlineData("negative")][InlineData("precision")][InlineData("quantityLarge")][InlineData("tooMany")]
    public async Task Invalid_recipient_address_or_selection_never_creates_order_holds_or_receipt(string invalid)
    {
        var c = await fixture.Web04Async(); var request = c.Request();
        switch (invalid)
        {
            case "name": request.RecipientName = " "; break;
            case "nameLong": request.RecipientName = new string('n', 201); break;
            case "phone": request.RecipientPhone = "abc-not-phone"; break;
            case "street": request.StreetAddress = " "; break;
            case "streetLong": request.StreetAddress = new string('a', 801); break;
            case "noteLong": request.Note = new string('n', 1001); break;
            case "province": request.ProvinceCode = "99"; break;
            case "ward": request.WardCode = "99999"; break;
            case "crossProvince": request.WardCode = "02001"; break;
            case "itemsNull": request.Items = null!; break;
            case "duplicate": request.Items.Add(request.Items[0]); break;
            case "zero": request.Items[0].Quantity = 0; break;
            case "negative": request.Items[0].Quantity = -1; break;
            case "precision": request.Items[0].Quantity = 1.23456m; break;
            case "quantityLarge": request.Items[0].Quantity = 1000001; break;
            case "tooMany": request.Items = Enumerable.Range(1, 101).Select(x => new StorefrontCheckoutSelection { ProductId = x, VariantId = x, UnitId = x, Quantity = 1 }).ToList(); break;
        }
        var before = await c.CountsAsync(); await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync(request)); Assert.Equal(before, await c.CountsAsync());
    }
    [Theory]
    [InlineData("hidden")][InlineData("productInactive")][InlineData("unsellable")][InlineData("variantInactive")][InlineData("unitInactive")][InlineData("codOff")][InlineData("settingsOff")]
    public async Task Live_publication_source_and_COD_settings_are_revalidated_at_quote(string reason)
    {
        var c = await fixture.Web04Async(); await using (var db = c.Context())
        {
            switch (reason)
            {
                case "hidden": await db.StorefrontProducts.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsPublished, false)); break;
                case "productInactive": await db.Products.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false)); break;
                case "unsellable": await db.Products.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsSellable, false)); break;
                case "variantInactive": await db.ProductVariants.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false)); break;
                case "unitInactive": await db.Units.Where(x => x.Id == c.Seed.UnitId).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false)); break;
                case "codOff": await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(p => p.CodEnabled, false)); break;
                case "settingsOff": await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsEnabled, false)); break;
            }
        }
        var before = await c.CountsAsync(); await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync()); Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Foreign_selection_and_money_total_at_limit_are_rejected_without_scope_or_reservation_leak()
    {
        var c = await fixture.Web04Async(); var foreign = await fixture.Web04Async(); var request = c.Request();
        request.Items = [new() { ProductId = foreign.Seed.ProductId, VariantId = foreign.Seed.VariantId, UnitId = foreign.Seed.UnitId, Quantity = 1 }];
        await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync(request));
        await using (var db = c.Context()) await db.ProductUnitConversions.Where(x => x.Id == c.Seed.ConversionId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, 9999999999980000m));
        await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync());
        await using var verify = c.Context(); Assert.Empty(await verify.StorefrontOrders.ToListAsync()); Assert.Empty(await verify.InventoryReservations.ToListAsync());
    }
}
