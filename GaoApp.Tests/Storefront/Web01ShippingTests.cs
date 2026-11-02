using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb01"), Trait("Category", "StorefrontWeb01")]
public sealed class Web01ShippingTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Local_and_national_zones_use_distinct_scopes_and_service_fee_settings()
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context(); var service = c.FullService(db);
        await service.SaveZoneAsync(await c.ZoneAsync());
        await service.SaveZoneAsync(await c.ZoneAsync("NATIONAL", "", "", StorefrontDeliveryMethod.Nationwide));
        var page = await service.GetAsync(); Assert.Equal(2, page.Zones.Count);
        Assert.Contains(page.Zones, x => x.DeliveryMethod == StorefrontDeliveryMethod.Local && x.ProvinceCode == "79");
        Assert.Contains(page.Zones, x => x.DeliveryMethod == StorefrontDeliveryMethod.Nationwide && x.ProvinceCode == "");
        Assert.All(page.Zones, x => { Assert.Equal(20_000, x.BaseFee); Assert.False(x.ByWeight); Assert.Equal(1, x.RateRevision); });
        Assert.Equal(StorefrontShippingFeeTreatment.StoreServiceFee, page.Settings.ShippingFeeTreatment);
    }

    [Fact]
    public async Task Settings_must_exist_before_saving_any_zone()
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        var request = await c.ZoneAsync();
        await Assert.ThrowsAsync<ValidationAppException>(() => c.FullService(db).SaveZoneAsync(request));
        Assert.Empty(await db.StorefrontShippingZones.ToListAsync()); Assert.Empty(await db.StorefrontShippingRates.ToListAsync());
    }

    [Theory]
    [InlineData("localWithoutProvince")]
    [InlineData("wardWithoutProvince")]
    [InlineData("invalidMethod")]
    [InlineData("invalidCode")]
    [InlineData("invalidProvince")]
    [InlineData("invertedDays")]
    [InlineData("negativeDays")]
    [InlineData("longDays")]
    [InlineData("negativeFee")]
    [InlineData("fractionFee")]
    [InlineData("negativeStepFee")]
    [InlineData("zeroKg")]
    [InlineData("tinyKg")]
    [InlineData("negativeStepKg")]
    [InlineData("zeroFreeThreshold")]
    [InlineData("fractionFreeThreshold")]
    [InlineData("invalidId")]
    public async Task Invalid_zone_input_does_not_write_zone_or_rate(string invalid)
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context(); var request = await c.ZoneAsync();
        switch (invalid)
        {
            case "localWithoutProvince": request.ProvinceCode = ""; break;
            case "wardWithoutProvince": request.ProvinceCode = ""; request.WardCode = "001"; request.DeliveryMethod = StorefrontDeliveryMethod.Nationwide; break;
            case "invalidMethod": request.DeliveryMethod = (StorefrontDeliveryMethod)99; break;
            case "invalidCode": request.Code = "<script>"; break;
            case "invalidProvince": request.ProvinceCode = "../79"; break;
            case "invertedDays": request.MinDeliveryDays = 4; request.MaxDeliveryDays = 3; break;
            case "negativeDays": request.MinDeliveryDays = -1; break;
            case "longDays": request.MaxDeliveryDays = 366; break;
            case "negativeFee": request.BaseFee = -1; break;
            case "fractionFee": request.BaseFee = 1.5m; break;
            case "negativeStepFee": request.AdditionalFee = -1; break;
            case "zeroKg": request.FirstWeightKg = 0; break;
            case "tinyKg": request.FirstWeightKg = 0.0000001m; break;
            case "negativeStepKg": request.AdditionalStepKg = -1; break;
            case "zeroFreeThreshold": request.FreeFromGoodsAmount = 0; break;
            case "fractionFreeThreshold": request.FreeFromGoodsAmount = 1.5m; break;
            case "invalidId": request.Id = -1; break;
        }
        await Assert.ThrowsAsync<ValidationAppException>(() => c.FullService(db).SaveZoneAsync(request));
        await using var verify = c.Context(); Assert.Empty(await verify.StorefrontShippingZones.ToListAsync()); Assert.Empty(await verify.StorefrontShippingRates.ToListAsync());
    }

    [Fact]
    public async Task Duplicate_scope_is_rejected_even_when_zone_code_differs()
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context();
        await c.FullService(db).SaveZoneAsync(await c.ZoneAsync());
        await using var duplicate = c.Context();
        var request = await c.ZoneAsync("OTHER");
        await Assert.ThrowsAsync<ConflictAppException>(() => c.FullService(duplicate).SaveZoneAsync(request));
        await using var verify = c.Context(); Assert.Single(await verify.StorefrontShippingZones.ToListAsync()); Assert.Single(await verify.StorefrontShippingRates.ToListAsync());
    }

    [Fact]
    public async Task Updating_rate_keeps_old_snapshot_and_increments_immutable_revision()
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context(); var service = c.FullService(db);
        await service.SaveZoneAsync(await c.ZoneAsync()); var zone = Assert.Single((await service.GetAsync()).Zones);
        var change = await c.ZoneAsync(); change.Id = zone.Id; change.Version = zone.Version; change.BaseFee = 30_000;
        await service.SaveZoneAsync(change); await using var verify = c.Context();
        var rates = await verify.StorefrontShippingRates.OrderBy(x => x.Revision).ToArrayAsync(); Assert.Equal(2, rates.Length);
        Assert.Equal(20_000, rates[0].BaseFee); Assert.False(rates[0].IsCurrent);
        Assert.Equal(30_000, rates[1].BaseFee); Assert.True(rates[1].IsCurrent); Assert.Equal(2, rates[1].Revision);
        Assert.Equal(2, (await verify.StorefrontShippingZones.SingleAsync()).Revision);
        await using var stale = c.Context();
        await Assert.ThrowsAsync<ConflictAppException>(() => c.FullService(stale).SaveZoneAsync(change));
    }

    [Fact]
    public async Task Changing_global_weight_mode_snapshots_all_rates_and_invalidates_zone_version()
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context(); var service = c.FullService(db);
        await service.SaveZoneAsync(await c.ZoneAsync()); var page = await service.GetAsync(); var zone = Assert.Single(page.Zones);
        page.Settings.ShippingByWeight = true; await service.SaveAsync(page.Settings);
        await using var verify = c.Context(); var rates = await verify.StorefrontShippingRates.OrderBy(x => x.Revision).ToArrayAsync();
        Assert.Equal(2, rates.Length); Assert.False(rates[0].ByWeight); Assert.False(rates[0].IsCurrent);
        Assert.True(rates[1].ByWeight); Assert.True(rates[1].IsCurrent); Assert.Equal(2, rates[1].Revision);
        Assert.Equal(2, (await verify.StorefrontShippingZones.SingleAsync()).Revision);
        var oldZone = await c.ZoneAsync(); oldZone.Id = zone.Id; oldZone.Version = zone.Version;
        await Assert.ThrowsAsync<ConflictAppException>(() => c.FullService(verify).SaveZoneAsync(oldZone));
    }

    [Fact]
    public async Task Foreign_zone_id_cannot_be_edited_or_disclosed()
    {
        var c = await fixture.CaseAsync(); var foreign = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); await foreign.SaveDefaultsAsync();
        await using var other = foreign.Context(); await foreign.FullService(other).SaveZoneAsync(await foreign.ZoneAsync());
        var foreignZone = Assert.Single((await foreign.FullService(other).GetAsync()).Zones);
        await using var db = c.Context(); var request = await c.ZoneAsync(); request.Id = foreignZone.Id; request.Version = foreignZone.Version;
        await Assert.ThrowsAsync<NotFoundAppException>(() => c.FullService(db).SaveZoneAsync(request));
        Assert.Empty((await c.FullService(db).GetAsync()).Zones);
    }

    [Fact]
    public async Task New_zone_form_cannot_silently_adopt_a_weight_mode_changed_after_it_was_opened()
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); var pending = await c.ZoneAsync();
        await using var db = c.Context(); var service = c.FullService(db); var page = await service.GetAsync();
        page.Settings.ShippingByWeight = true; await service.SaveAsync(page.Settings);
        await using var stale = c.Context();
        await Assert.ThrowsAsync<ConflictAppException>(() => c.FullService(stale).SaveZoneAsync(pending));
        Assert.Empty(await stale.StorefrontShippingZones.ToListAsync()); Assert.Empty(await stale.StorefrontShippingRates.ToListAsync());
        await c.FullService(stale).SaveZoneAsync(await c.ZoneAsync());
        Assert.True((await stale.StorefrontShippingRates.SingleAsync()).ByWeight);
    }
}
