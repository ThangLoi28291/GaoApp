using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb02"), Trait("Category", "StorefrontWeb02")]
public sealed class Web02SecurityConcurrencyTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("none", "list")][InlineData("none", "get")][InlineData("none", "media")]
    [InlineData("none", "save")][InlineData("none", "publication")][InlineData("none", "bulk")]
    [InlineData("view", "save")][InlineData("view", "publication")][InlineData("view", "bulk")]
    [InlineData("manage", "list")][InlineData("manage", "get")][InlineData("manage", "media")]
    public async Task Every_operation_checks_its_own_permission_and_manage_does_not_imply_view(string grant, string operation)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        string[] codes = grant switch { "view" => [StorefrontPermissions.ViewProducts], "manage" => [StorefrontPermissions.ManageProducts], _ => [] };
        var service = new StorefrontProductService(db, new Web01Actor(c.UserId), new Web01Permissions(c.StoreId, c.UserId, codes));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => operation switch
        {
            "list" => (Task)service.ListAsync(new()), "get" => service.GetAsync(c.ProductId), "media" => service.SearchMediaAsync(null),
            "save" => service.SaveAsync(c.ProductId, c.Publication()), "publication" => service.SetPublicationAsync(new() { ProductId = c.ProductId, IsPublished = true }),
            _ => service.SetPublicationBulkAsync(new() { Items = [new() { ProductId = c.ProductId, IsPublished = true }] })
        });
        Assert.Empty(await db.StorefrontProducts.ToListAsync());
    }

    [Theory]
    [InlineData(false, true)][InlineData(true, false)]
    public async Task Service_denies_unauthenticated_or_missing_actor_even_if_resolver_returns_permission(bool authenticated, bool withId)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        var service = new StorefrontProductService(db, new Web01Actor(withId ? c.UserId : null, authenticated),
            new Web01Permissions(c.StoreId, c.UserId, [StorefrontPermissions.ViewProducts, StorefrontPermissions.ManageProducts]));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.ListAsync(new()));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.SaveAsync(c.ProductId, c.Publication()));
    }

    [Theory]
    [InlineData("revoke")][InlineData("membershipInactive")][InlineData("membershipDeleted")][InlineData("userInactive")][InlineData("userDeleted")]
    public async Task Real_permission_repository_rechecks_current_grants_and_active_membership(string revoke)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        var membership = await db.UserInStores.SingleAsync(x => x.UserId == c.UserId); var role = membership.RoleId;
        foreach (var code in new[] { StorefrontPermissions.ViewProducts, StorefrontPermissions.ManageProducts })
            db.RolePermissions.Add(new() { RoleId = role, PermissionId = await db.Permissions.Where(x => x.Code == code).Select(x => x.Id).SingleAsync() });
        await db.SaveChangesAsync();
        var service = new StorefrontProductService(db, new Web01Actor(c.UserId), new CurrentStorePermissionService(new UserInStoreRepository(db)));
        Assert.Single((await service.ListAsync(new())).Items); await service.SaveAsync(c.ProductId, c.Publication());
        var request = await c.SavedAsync();
        await using (var mutate = c.Context())
        {
            switch (revoke)
            {
                case "revoke": await mutate.RolePermissions.Where(x => x.RoleId == role).ExecuteDeleteAsync(); break;
                case "membershipInactive": await mutate.UserInStores.Where(x => x.UserId == c.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
                case "membershipDeleted": await mutate.UserInStores.Where(x => x.UserId == c.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); break;
                case "userInactive": await mutate.Users.Where(x => x.Id == c.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
                case "userDeleted": await mutate.Users.Where(x => x.Id == c.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); break;
            }
        }
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetAsync(c.ProductId));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.SaveAsync(c.ProductId, request));
        Assert.Empty(await db.AdminMenuItems.ToListAsync());
    }

    [Fact]
    public async Task Host_or_cross_store_actor_and_foreign_product_ids_are_denied_without_mutation()
    {
        var c = await fixture.CaseAsync(); var other = await fixture.CaseAsync(); await using var db = c.Context(); await using var host = fixture.Database.CreateHostContext();
        await Assert.ThrowsAsync<ForbiddenAppException>(() => c.Products(host).GetAsync(c.ProductId));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => c.Products(host).SaveAsync(c.ProductId, c.Publication()));
        var wrongActor = new StorefrontProductService(db, new Web01Actor(c.UserId), new Web01Permissions(other.StoreId, c.UserId, [StorefrontPermissions.ViewProducts, StorefrontPermissions.ManageProducts]));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => wrongActor.GetAsync(c.ProductId));
        await Assert.ThrowsAsync<NotFoundAppException>(() => c.Products(db).GetAsync(other.ProductId));
        await Assert.ThrowsAsync<NotFoundAppException>(() => c.Products(db).SaveAsync(other.ProductId, c.Publication()));
        Assert.Null(typeof(StorefrontProductRequest).GetProperty("StoreId")); Assert.Null(typeof(StorefrontPublicationRequest).GetProperty("StoreId"));
        await using var foreign = other.Context(); Assert.Empty(await foreign.StorefrontProducts.ToListAsync());
    }

    [Theory]
    [InlineData("productInactive")][InlineData("productUnsellable")][InlineData("variantInactive")][InlineData("baseUnitInactive")][InlineData("conversionInactive")][InlineData("sellingUnitInactive")]
    public async Task Cannot_manually_publish_ineligible_source_data(string kind)
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(); await using var db = c.Context();
        switch (kind)
        {
            case "productInactive": await db.Products.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
            case "productUnsellable": await db.Products.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsSellable, false)); break;
            case "variantInactive": await db.ProductVariants.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
            case "baseUnitInactive": await db.Units.Where(x => x.Id == c.UnitId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
            case "conversionInactive": await db.ProductUnitConversions.Where(x => x.Id == pack.Conversion).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
            case "sellingUnitInactive": await db.Units.Where(x => x.Id == pack.Unit).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
        }
        await Assert.ThrowsAsync<ValidationAppException>(() => c.Products(db).SaveAsync(c.ProductId, c.Publication(pack: pack.Conversion)));
        Assert.Empty(await db.StorefrontProducts.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("")][InlineData("invalid")][InlineData("AQIDBAUGBwg=")][InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Invalid_or_stale_expected_version_cannot_overwrite_current_publication(string token)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context(); await c.Products(db).SaveAsync(c.ProductId, c.Publication());
        var request = c.Publication(token); request.DisplayName = "MUST NOT OVERWRITE";
        await Assert.ThrowsAsync<ConflictAppException>(() => c.Products(db).SaveAsync(c.ProductId, request));
        Assert.Equal("Bình giữ nhiệt WEB02", (await c.SavedAsync()).DisplayName);
    }

    [Fact]
    public async Task Child_only_change_advances_aggregate_version_and_stale_editor_cannot_undo_it()
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(); await using var db = c.Context();
        await c.Products(db).SaveAsync(c.ProductId, c.Publication(pack: pack.Conversion));
        var stale = await c.SavedAsync(); var edit = await c.SavedAsync(); edit.Variants[0].Units[0].WeightKg = 3.456789m;
        await c.Products(db).SaveAsync(c.ProductId, edit); var current = await c.SavedAsync(); Assert.NotEqual(stale.Version, current.Version);
        await Assert.ThrowsAsync<ConflictAppException>(() => c.Products(db).SaveAsync(c.ProductId, stale));
        Assert.Equal(3.456789m, Assert.Single(Assert.Single((await c.SavedAsync()).Variants).Units).WeightKg);
    }

    [Theory]
    [InlineData("")][InlineData("invalid")][InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Toggle_does_not_allow_missing_or_malformed_version_even_when_target_already_matches(string version)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context(); await c.Products(db).SaveAsync(c.ProductId, c.Publication());
        var before = await c.SavedAsync();
        Assert.Equal(StorefrontPublicationOutcome.Conflict, (await c.Products(db).SetPublicationAsync(new() { ProductId = c.ProductId, Version = version, IsPublished = true })).Outcome);
        Assert.Equal(before.Version, (await c.SavedAsync()).Version);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Concurrent_first_create_or_expected_version_updates_have_one_winner_and_no_duplicate_graph(bool update)
    {
        var c = await fixture.CaseAsync(); string version = "";
        if (update) { await using var db = c.Context(); await c.Products(db).SaveAsync(c.ProductId, c.Publication()); version = (await c.SavedAsync()).Version; }
        async Task<bool> Save(string text)
        {
            await using var db = c.Context(); var request = c.Publication(version); request.Description = text;
            try { await c.Products(db).SaveAsync(c.ProductId, request); return true; } catch (ConflictAppException) { return false; }
        }
        var result = await Task.WhenAll(Save("First contender"), Save("Second contender")); Assert.Single(result, x => x);
        await using var verify = c.Context(); Assert.Single(await verify.StorefrontProducts.ToListAsync()); Assert.Single(await verify.StorefrontVariants.ToListAsync());
        Assert.Contains((await c.SavedAsync()).Description, new[] { "First contender", "Second contender" });
    }
}
