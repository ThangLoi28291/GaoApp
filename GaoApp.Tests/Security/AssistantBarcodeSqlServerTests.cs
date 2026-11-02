using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Assistant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class AssistantBarcodeSqlServerTests
{
    [Fact]
    public async Task Exact_barcode_search_runs_on_SQL_Server_and_isolates_duplicate_codes_between_stores()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        const string barcode = "0008935217400157";
        foreach (var store in app.Stores)
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            var unit = new ProductUnitConversion
            {
                StoreId = store.StoreId, ProductVariantId = variant.Id, ProductVariant = variant,
                UnitId = variant.Product.BaseUnitId, Factor = 1, IsActive = true, IsBaseUnit = true
            };
            db.ProductUnitConversions.Add(unit);
            await db.SaveChangesAsync();
            db.ProductVariantUnitBarcodes.Add(new ProductVariantUnitBarcode
            {
                StoreId = store.StoreId, ProductUnitConversionId = unit.Id, Barcode = barcode, IsActive = true
            });
            await db.SaveChangesAsync();
        }
        foreach (var store in app.Stores)
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var catalog = new AssistantCatalogService(db, TimeProvider.System);
            var who = new AssistantIdentity(1, "TEST", new(store.StoreId.ToString(), null, null), AssistantPermissions.All);
            var found = await catalog.SearchAsync(who, new CatalogSearch { Query = "barcode:" + barcode, Limit = 2 }, default);
            Assert.Equal(store.VariantId.ToString(), Assert.Single(found.Variants).Product.VariantId);
            Assert.Null(found.Cursor);
            Assert.Empty((await catalog.SearchAsync(who, new CatalogSearch { Query = "barcode:" + barcode[1..], Limit = 2 }, default)).Variants);
            var prices = await catalog.QuoteAsync(who, new QuoteLookup
            {
                Items = found.Variants[0].Units.Select(u => new QuoteItem(found.Variants[0].Product.ProductId,
                    found.Variants[0].Product.VariantId, u.Unit.UnitId, u.Unit.ConversionId)).ToArray(),
                Tiers = ["retail", "wholesale"]
            }, default);
            Assert.NotEmpty(prices);
            Assert.All(prices, price => Assert.Equal(store.VariantId.ToString(), price.Product.VariantId));
            Assert.Empty(db.ChangeTracker.Entries());
        }
    }
}
