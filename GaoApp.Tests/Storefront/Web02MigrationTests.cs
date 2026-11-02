using System.Text.Json;
using GaoApp.Domain.Storefront;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb02"), Trait("Category", "StorefrontWeb02")]
public sealed class Web02MigrationTests(Web01Fixture fixture)
{
    private const string Previous = "20261030170000_AddStorefrontFoundation";
    private const string Current = "20261030230000_AddStorefrontBaseUnitPublication";

    [Fact]
    public async Task Fresh_model_and_SQL_have_explicit_false_base_default_and_no_implicit_publication_or_menu()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); await using var db = sql.CreateHostContext();
        Assert.Contains(Current, await db.Database.GetAppliedMigrationsAsync()); Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model; var property = model.FindEntityType(typeof(StorefrontVariant))!.FindProperty(nameof(StorefrontVariant.BaseUnitIsPublished))!;
        Assert.False(property.IsNullable); Assert.Equal(false, property.GetDefaultValue());
        Assert.Empty(await db.StorefrontProducts.ToListAsync()); Assert.Empty(await db.StorefrontVariants.ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync());
        Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.StorefrontVariants WHERE BaseUnitIsPublished<>0;"));
        var c = await fixture.CaseAsync(); await c.PublishAsync(false); await using var tenant = c.Context(); Assert.False((await tenant.StorefrontVariants.SingleAsync()).BaseUnitIsPublished);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Upgrade_defaults_base_selection_false_and_preserves_existing_hidden_or_published_graph_and_service_fee(bool existingPublished)
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(Previous); var seed = await sql.SeedInventoryCatalogAsync();
        int productId; int publicationId;
        await using (var db = sql.CreateTenantContext(seed.StoreId))
        {
            productId = await db.ProductVariants.Where(x => x.Id == seed.ProductVariantId).Select(x => x.ProductId).SingleAsync();
            var publication = new StorefrontProduct { StoreId = seed.StoreId, ProductId = productId, IsPublished = existingPublished,
                DisplayName = "Existing WEB01 publication", Description = "Do not alter", DisplayGroup = "Existing group", SortOrder = 87 };
            db.Add(publication); db.StorefrontSettings.Add(new() { StoreId = seed.StoreId, DisplayName = "Existing synthetic settings", ShippingFeeTreatment = StorefrontShippingFeeTreatment.StoreServiceFee });
            await db.SaveChangesAsync(); publicationId = publication.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT dbo.StorefrontVariants (StoreId,StorefrontProductId,ProductId,VariantId,IsPublished,BaseUnitWeightKg,CreatedAtUtc,IsDeleted)
                VALUES ({seed.StoreId},{publicationId},{productId},{seed.ProductVariantId},{existingPublished},CAST(0.321 AS decimal(18,6)),SYSUTCDATETIME(),0);
                """);
            Assert.Equal(0.321m, await sql.ExecuteScalarAsync<decimal>("SELECT BaseUnitWeightKg FROM dbo.StorefrontVariants;"));
        }
        var before = await Snapshot(sql); await sql.MigrateAsync(); Assert.Equal(before, await Snapshot(sql));
        await using (var db = sql.CreateTenantContext(seed.StoreId))
        {
            var variant = await db.StorefrontVariants.SingleAsync(); Assert.False(variant.BaseUnitIsPublished); Assert.Equal(existingPublished, variant.IsPublished);
            Assert.Equal(0.321m, variant.BaseUnitWeightKg); Assert.Equal(StorefrontShippingFeeTreatment.StoreServiceFee, (await db.StorefrontSettings.SingleAsync()).ShippingFeeTreatment);
            Assert.Empty(await db.AdminMenuItems.ToListAsync());
        }
        await sql.MigrateAsync(); Assert.Equal(before, await Snapshot(sql));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Downgrade_preserves_explicit_base_selection_by_refusing_loss_and_empty_selection_can_round_trip(bool baseSelected)
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); var seed = await sql.SeedInventoryCatalogAsync();
        await using (var db = sql.CreateTenantContext(seed.StoreId))
        {
            var product = await db.ProductVariants.Where(x => x.Id == seed.ProductVariantId).Select(x => x.ProductId).SingleAsync();
            var publication = new StorefrontProduct { StoreId = seed.StoreId, ProductId = product }; db.Add(publication); await db.SaveChangesAsync();
            db.StorefrontVariants.Add(new() { StoreId = seed.StoreId, ProductId = product, StorefrontProductId = publication.Id, VariantId = seed.ProductVariantId,
                IsPublished = baseSelected, BaseUnitIsPublished = baseSelected, BaseUnitWeightKg = 0.2m }); await db.SaveChangesAsync();
        }
        var before = await Snapshot(sql);
        if (baseSelected)
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => sql.MigrateAsync(Previous)); Assert.Equal(51093, ex.Number);
            await using var db = sql.CreateHostContext(); Assert.Contains(Current, await db.Database.GetAppliedMigrationsAsync()); Assert.True((await db.StorefrontVariants.SingleAsync()).BaseUnitIsPublished);
        }
        else { await sql.MigrateAsync(Previous); await sql.MigrateAsync(); }
        Assert.Equal(before, await Snapshot(sql));
    }

    private static async Task<string> Snapshot(InventoryPostingLocalDb sql)
    {
        // Deliberately excludes the new column; all pre-existing variant data including
        // rowversion must survive ADD COLUMN without a data rewrite.
        var values = await sql.ExecuteScalarAsync<string>("""
            SELECT (SELECT StoreId,StorefrontProductId,ProductId,VariantId,IsPublished,BaseUnitWeightKg,CreatedAtUtc,IsDeleted,RowVersion
            FROM dbo.StorefrontVariants ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES);
            """);
        await using var db = sql.CreateHostContext();
        return JsonSerializer.Serialize(new { variants = values,
            publications = await db.StorefrontProducts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.StoreId, x.ProductId, x.IsPublished, x.DisplayName, x.Description, x.DisplayGroup, x.SortOrder, x.RowVersion }).ToListAsync(),
            products = await db.Products.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.BasePrice, x.IsActive, x.IsSellable, x.RowVersion }).ToListAsync(),
            settings = await db.StorefrontSettings.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.IsEnabled, x.ShippingFeeTreatment, x.RowVersion }).ToListAsync(),
            menu = await db.AdminMenuItems.CountAsync(), orders = await db.Orders.CountAsync(), stock = await db.InventoryTransactions.CountAsync() });
    }
}
