using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Application.Services.Security;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb01"), Trait("Category", "StorefrontWeb01")]
public sealed class Web01SettingsTests(Web01Fixture fixture)
{
    [Fact]
    public async Task First_read_is_closed_service_fee_defaults_without_inserts_or_pos_effects()
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        var before = new { Menus = await db.AdminMenuItems.CountAsync(), Pos = await db.Orders.CountAsync(), Stock = await db.InventoryTransactions.CountAsync(), Money = await db.OrderPayments.CountAsync() };
        var result = await c.FullService(db).GetAsync();
        Assert.False(result.Settings.IsEnabled); Assert.True(result.Settings.CodEnabled); Assert.False(result.Settings.QrEnabled);
        Assert.True(result.Settings.AllowNegativeInventory); Assert.False(result.Settings.ShippingByWeight);
        Assert.Equal(StorefrontShippingFeeTreatment.StoreServiceFee, result.Settings.ShippingFeeTreatment);
        Assert.Equal("", result.Settings.Version); Assert.Equal(0, result.Revision);
        Assert.Empty(await db.StorefrontSettings.ToListAsync()); Assert.Empty(await db.StorefrontProducts.ToListAsync());
        Assert.Equal(before.Menus, await db.AdminMenuItems.CountAsync()); Assert.Equal(before.Pos, await db.Orders.CountAsync());
        Assert.Equal(before.Stock, await db.InventoryTransactions.CountAsync()); Assert.Equal(before.Money, await db.OrderPayments.CountAsync());
        Assert.Single(result.Warehouses); Assert.Single(result.BankAccounts); Assert.Single(result.LegalEntities);
        Assert.Empty(result.Zones);
    }

    [Fact]
    public async Task Missing_tenant_is_rejected_before_reading_or_writing_configuration()
    {
        var c = await fixture.CaseAsync(); await using var host = fixture.Database.CreateHostContext();
        var service = c.FullService(host);
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.SaveAsync(c.Request()));
    }

    [Fact]
    public async Task Real_permission_repository_honours_view_manage_and_immediate_revocation_without_menus()
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        var service = new StorefrontSettingsService(db, new Web01Actor(c.UserId), new CurrentStorePermissionService(new UserInStoreRepository(db)));
        var menuCount = await db.AdminMenuItems.CountAsync();
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetAsync());
        var roleId = await db.UserInStores.Where(x => x.UserId == c.UserId).Select(x => x.RoleId).SingleAsync();
        var view = await db.Permissions.SingleAsync(x => x.Code == StorefrontPermissions.ViewSettings);
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = view.Id }); await db.SaveChangesAsync();
        Assert.False((await service.GetAsync()).CanManageSettings);
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.SaveAsync(c.Request()));
        var manage = await db.Permissions.SingleAsync(x => x.Code == StorefrontPermissions.ManageSettings);
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = manage.Id }); await db.SaveChangesAsync();
        await service.SaveAsync(c.Request());
        var version = (await service.GetAsync()).Settings.Version;
        await db.RolePermissions.Where(x => x.RoleId == roleId).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.SaveAsync(c.Request(version)));
        Assert.Equal(menuCount, await db.AdminMenuItems.CountAsync());
    }

    [Fact]
    public async Task Save_and_reload_capture_real_source_and_revision_without_creating_pos_context()
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        await c.FullService(db).SaveAsync(c.Request());
        var settings = Assert.Single(await db.StorefrontSettings.ToListAsync());
        Assert.Equal(c.WarehouseId, settings.SourceWarehouseId); Assert.Equal(c.LegalEntityId, settings.SourceLegalEntityId);
        Assert.Equal(1, settings.Revision); Assert.False(settings.IsEnabled);
        Assert.Equal(StorefrontShippingFeeTreatment.StoreServiceFee, settings.ShippingFeeTreatment);
        Assert.Empty(await db.POSShifts.ToListAsync()); Assert.Empty(await db.POSTerminals.ToListAsync());
        Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync());
        var page = await c.FullService(db).GetAsync(); Assert.Equal(8, Convert.FromBase64String(page.Settings.Version).Length);
        page.Settings.DisplayName = "  Updated shop  "; await c.FullService(db).SaveAsync(page.Settings);
        await using var verify = c.Context(); var changed = await verify.StorefrontSettings.SingleAsync();
        Assert.Equal("Updated shop", changed.DisplayName); Assert.Equal(2, changed.Revision);
    }

    [Theory]
    [InlineData("none", "get")]
    [InlineData("none", "save")]
    [InlineData("none", "zone")]
    [InlineData("view", "save")]
    [InlineData("view", "zone")]
    [InlineData("settings", "zone")]
    [InlineData("shipping", "save")]
    public async Task Service_checks_permission_for_each_operation(string grant, string operation)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        string[] codes = grant switch { "view" => [StorefrontPermissions.ViewSettings], "settings" => [StorefrontPermissions.ManageSettings],
            "shipping" => [StorefrontPermissions.ManageShipping], _ => [] };
        var service = c.Service(db, codes); var zone = await c.ZoneAsync();
        await Assert.ThrowsAsync<ForbiddenAppException>(() => operation switch
        {
            "get" => (Task)service.GetAsync(), "save" => service.SaveAsync(c.Request()), _ => service.SaveZoneAsync(zone)
        });
        Assert.Empty(await db.StorefrontSettings.ToListAsync()); Assert.Empty(await db.StorefrontShippingZones.ToListAsync());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Unauthenticated_or_missing_user_cannot_use_trusted_permission_service(bool authenticated, bool withId)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        var service = new StorefrontSettingsService(db, new Web01Actor(withId ? c.UserId : null, authenticated),
            new Web01Permissions(c.StoreId, c.UserId, [StorefrontPermissions.ViewSettings, StorefrontPermissions.ManageSettings]));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.SaveAsync(c.Request()));
    }

    [Fact]
    public async Task Foreign_store_permission_and_inactive_store_are_denied()
    {
        var c = await fixture.CaseAsync(); var other = await fixture.CaseAsync(); await using var db = c.Context();
        var foreign = new StorefrontSettingsService(db, new Web01Actor(c.UserId), new Web01Permissions(other.StoreId, c.UserId, [StorefrontPermissions.ViewSettings]));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => foreign.GetAsync());
        var store = await db.Stores.SingleAsync(x => x.Id == c.StoreId); store.IsActive = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenAppException>(() => c.FullService(db).GetAsync());
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("feeUnconfigured")]
    [InlineData("feeCarrier")]
    [InlineData("feeUnknown")]
    [InlineData("noMethod")]
    [InlineData("blankName")]
    [InlineData("longName")]
    [InlineData("badPhone")]
    [InlineData("zeroHold")]
    [InlineData("longHold")]
    [InlineData("negativeWarehouse")]
    [InlineData("bankWithoutOwner")]
    [InlineData("ownerWithoutBank")]
    [InlineData("duplicateOwners")]
    [InlineData("negativeOwner")]
    [InlineData("ownersWithoutBank")]
    [InlineData("nullOwners")]
    public async Task Invalid_settings_fail_without_partial_configuration(string input)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context(); var request = c.Request();
        switch (input)
        {
            case "enable": request.IsEnabled = true; break;
            case "feeUnconfigured": request.ShippingFeeTreatment = StorefrontShippingFeeTreatment.Unconfigured; break;
            case "feeCarrier": request.ShippingFeeTreatment = StorefrontShippingFeeTreatment.CarrierPassThrough; break;
            case "feeUnknown": request.ShippingFeeTreatment = (StorefrontShippingFeeTreatment)99; break;
            case "noMethod": request.CodEnabled = request.QrEnabled = false; break;
            case "blankName": request.DisplayName = "  "; break;
            case "longName": request.DisplayName = new string('x', 201); break;
            case "badPhone": request.ContactPhone = "<script>"; break;
            case "zeroHold": request.QrHoldMinutes = 0; break;
            case "longHold": request.QrHoldMinutes = 10081; break;
            case "negativeWarehouse": request.SourceWarehouseId = -1; break;
            case "bankWithoutOwner": request.BankAccountId = c.BankAccountId; break;
            case "ownerWithoutBank": request.ReceivingLegalEntityId = c.LegalEntityId; break;
            case "duplicateOwners": request.CollectionSourceLegalEntityIds = [c.LegalEntityId, c.LegalEntityId]; break;
            case "negativeOwner": request.CollectionSourceLegalEntityIds = [-1]; break;
            case "ownersWithoutBank": request.CollectionSourceLegalEntityIds = [c.LegalEntityId]; break;
            case "nullOwners": request.CollectionSourceLegalEntityIds = null!; break;
        }
        await Assert.ThrowsAsync<ValidationAppException>(() => c.FullService(db).SaveAsync(request));
        Assert.Empty(await db.StorefrontSettings.ToListAsync());
        Assert.Empty(await db.Set<StorefrontBankCollectionAuthority>().ToListAsync());
    }

    [Theory]
    [InlineData("https://example.com/logo.png")]
    [InlineData("//example.com/logo.png")]
    [InlineData("/uploads/../logo.png")]
    [InlineData("/uploads/%2e%2e/logo.png")]
    [InlineData("/uploads/dir\\logo.png")]
    [InlineData("/uploads/logo.svg")]
    [InlineData("/uploads/logo.html")]
    public async Task Logo_is_limited_to_local_raster_upload_paths(string path)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context(); var request = c.Request(); request.LogoPath = path;
        await Assert.ThrowsAsync<ValidationAppException>(() => c.FullService(db).SaveAsync(request));
    }

    [Fact]
    public async Task Foreign_or_inactive_source_bank_and_authority_are_rejected()
    {
        var c = await fixture.CaseAsync(); var other = await fixture.CaseAsync();
        foreach (var kind in new[] { "warehouse", "bank", "sourceOwner", "receivingOwner" })
        {
            await using var db = c.Context(); var request = c.Request(); request.BankAccountId = c.BankAccountId; request.ReceivingLegalEntityId = c.LegalEntityId;
            if (kind == "warehouse") request.SourceWarehouseId = other.WarehouseId;
            if (kind == "bank") request.BankAccountId = other.BankAccountId;
            if (kind == "sourceOwner") request.CollectionSourceLegalEntityIds = [other.LegalEntityId];
            if (kind == "receivingOwner") request.ReceivingLegalEntityId = other.LegalEntityId;
            await Assert.ThrowsAsync<ValidationAppException>(() => c.FullService(db).SaveAsync(request));
        }
        await using var inactive = c.Context(); var warehouse = await inactive.Warehouses.SingleAsync(x => x.Id == c.WarehouseId); warehouse.IsActive = false;
        await inactive.SaveChangesAsync(); await Assert.ThrowsAsync<ValidationAppException>(() => c.FullService(inactive).SaveAsync(c.Request()));
    }

    [Theory]
    [InlineData("noAcb")]
    [InlineData("merchantLong")]
    [InlineData("beneficiaryLong")]
    [InlineData("terminalMissing")]
    [InlineData("userInvalid")]
    [InlineData("holdMissing")]
    [InlineData("authorityMissing")]
    public async Task Qr_requires_registered_binding_transport_bounds_and_all_collection_authorities(string invalid)
    {
        var c = await fixture.CaseAsync(); if (invalid != "noAcb") await c.ConfigureAcbAsync();
        await using var db = c.Context();
        if (invalid is "merchantLong" or "beneficiaryLong")
        {
            var acb = await db.Set<GaoApp.Domain.Entities.StoreAcbSettings>().SingleAsync();
            if (invalid == "merchantLong") acb.MerchantId = new string('m', 31); else acb.BeneficiaryName = new string('n', 31);
            await db.SaveChangesAsync();
        }
        var request = c.Request(); request.QrEnabled = true; request.QrHoldMinutes = 10; request.BankAccountId = c.BankAccountId;
        request.ReceivingLegalEntityId = c.LegalEntityId; request.BankTerminalCode = "BANK-WEB"; request.BankUserCode = "REGISTERED-WEB";
        request.CollectionSourceLegalEntityIds = [c.LegalEntityId];
        if (invalid == "terminalMissing") request.BankTerminalCode = "";
        if (invalid == "userInvalid") request.BankUserCode = "<notregistered>";
        if (invalid == "holdMissing") request.QrHoldMinutes = null;
        if (invalid == "authorityMissing") request.CollectionSourceLegalEntityIds = [];
        await Assert.ThrowsAsync<ValidationAppException>(() => c.FullService(db).SaveAsync(request));
        Assert.Empty(await db.StorefrontSettings.ToListAsync());
    }

    [Fact]
    public async Task Valid_qr_settings_store_bank_identifiers_and_authority_without_pos_terminal_or_shift()
    {
        var c = await fixture.CaseAsync(); await c.ConfigureAcbAsync(); await using var db = c.Context();
        var request = c.Request(); request.QrEnabled = true; request.QrHoldMinutes = 17; request.BankAccountId = c.BankAccountId;
        request.ReceivingLegalEntityId = c.LegalEntityId; request.BankTerminalCode = "BANK-WEB"; request.BankUserCode = "REGISTERED-WEB";
        request.CollectionSourceLegalEntityIds = [c.LegalEntityId]; await c.FullService(db).SaveAsync(request);
        var page = await c.FullService(db).GetAsync(); Assert.True(page.Settings.QrEnabled); Assert.Equal(17, page.Settings.QrHoldMinutes);
        Assert.Equal("BANK-WEB", page.Settings.BankTerminalCode); Assert.Equal("REGISTERED-WEB", page.Settings.BankUserCode);
        Assert.Equal(new[] { c.LegalEntityId }, page.Settings.CollectionSourceLegalEntityIds);
        Assert.Single(await db.Set<StorefrontBankCollectionAuthority>().Where(x => x.IsActive).ToListAsync());
        Assert.Empty(await db.POSTerminals.ToListAsync()); Assert.Empty(await db.POSShifts.ToListAsync());
    }

    [Theory]
    [InlineData(true, null, 0.2)]
    [InlineData(true, 0.2, null)]
    public async Task By_weight_rejects_missing_kg_on_published_variants_or_units(bool published, double? baseKg, double? unitKg)
    {
        var c = await fixture.CaseAsync(); await c.PublishAsync(published, baseKg.HasValue ? (decimal)baseKg : null, unitKg.HasValue ? (decimal)unitKg : null);
        await using var db = c.Context(); var request = c.Request(); request.ShippingByWeight = true;
        await Assert.ThrowsAsync<ValidationAppException>(() => c.FullService(db).SaveAsync(request));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Hidden_missing_weights_or_fixed_mode_do_not_block_settings(bool published, bool byWeight)
    {
        var c = await fixture.CaseAsync(); await c.PublishAsync(published);
        await using var db = c.Context(); var request = c.Request(); request.ShippingByWeight = byWeight;
        await c.FullService(db).SaveAsync(request); Assert.Equal(byWeight, (await db.StorefrontSettings.SingleAsync()).ShippingByWeight);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AQIDBAUGBwg=")]
    public async Task Malformed_or_stale_version_cannot_overwrite_settings(string stale)
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context();
        var request = c.Request(stale); request.DisplayName = "Must not overwrite";
        await Assert.ThrowsAsync<ConflictAppException>(() => c.FullService(db).SaveAsync(request));
        await using var verify = c.Context(); Assert.Equal("WEB01 synthetic shop", (await verify.StorefrontSettings.SingleAsync()).DisplayName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_create_or_same_version_update_has_one_winner(bool update)
    {
        var c = await fixture.CaseAsync(); string version = "";
        if (update) { await c.SaveDefaultsAsync(); await using var load = c.Context(); version = (await c.FullService(load).GetAsync()).Settings.Version; }
        async Task<bool> Save(string name)
        {
            await using var db = c.Context(); var request = c.Request(version); request.DisplayName = name;
            try { await c.FullService(db).SaveAsync(request); return true; } catch (ConflictAppException) { return false; }
        }
        var outcomes = await Task.WhenAll(Save("Concurrent A"), Save("Concurrent B")); Assert.Single(outcomes, x => x);
        await using var verify = c.Context(); var row = Assert.Single(await verify.StorefrontSettings.ToListAsync());
        Assert.Equal(update ? 2 : 1, row.Revision); Assert.Contains(row.DisplayName, new[] { "Concurrent A", "Concurrent B" });
    }
}
