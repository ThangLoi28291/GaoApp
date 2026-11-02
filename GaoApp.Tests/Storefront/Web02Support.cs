using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[CollectionDefinition("StorefrontWeb02", DisableParallelization = true)]
public sealed class Web02Collection : ICollectionFixture<Web01Fixture> { }

internal static class Web02Support
{
    internal static StorefrontProductService Products(this Web01Case c, AppDbContext db, params string[] codes)
        => new(db, new Web01Actor(c.UserId), new Web01Permissions(c.StoreId, c.UserId,
            codes.Length == 0 ? [StorefrontPermissions.ViewProducts, StorefrontPermissions.ManageProducts] : codes));

    internal static StorefrontProductRequest Publication(this Web01Case c, string version = "", bool publish = true,
        bool baseUnit = true, int? pack = null, decimal? baseKg = 0.125m, decimal? packKg = 1.4m) => new()
    {
        Version = version, IsPublished = publish, DisplayName = "Bình giữ nhiệt WEB02", Description = "Mô tả tiếng Việt\nDòng thứ hai.",
        DisplayGroup = "Gia dụng", SortOrder = 17,
        Variants = [new() { VariantId = c.VariantId, IsPublished = true, BaseUnitIsPublished = baseUnit,
            BaseUnitWeightKg = baseKg, Units = pack.HasValue ? [new() { ProductUnitConversionId = pack.Value, IsPublished = true, WeightKg = packKg }] : [] }]
    };

    internal static async Task<(int Conversion, int Unit)> PackAsync(this Web01Case c, decimal factor = 12, decimal? price = 180_000)
    {
        await using var db = c.Context();
        var unit = new Unit { StoreId = c.StoreId, Code = "PACK-" + Guid.NewGuid().ToString("N")[..10], Name = "Thùng 12", IsActive = true };
        db.Units.Add(unit); await db.SaveChangesAsync();
        var conversion = new ProductUnitConversion { StoreId = c.StoreId, ProductVariantId = c.VariantId, UnitId = unit.Id,
            Factor = factor, Price = price, WholesalePrice = 1, IsActive = true };
        db.ProductUnitConversions.Add(conversion); await db.SaveChangesAsync();
        return (conversion.Id, unit.Id);
    }

    // Only a GUID fixture can reach this helper. WEB09 still owns the real opening gate.
    internal static async Task EnableSyntheticCatalogAsync(this Web01Case c, bool byWeight = false)
    {
        await c.SaveDefaultsAsync();
        await using var db = c.Context();
        await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, true).SetProperty(s => s.ShippingByWeight, byWeight));
    }

    internal static async Task<StorefrontProductRequest> SavedAsync(this Web01Case c)
    {
        await using var db = c.Context(); return (await c.Products(db).GetAsync(c.ProductId)).Product;
    }

    internal static async Task<int> AssetAsync(this Web01Case c, string path = "uploads/products/web02.jpg", string type = "image/jpeg",
        bool temp = false, string? token = null, DateTime? expires = null, bool deleted = false)
    {
        await using var db = c.Context();
        var asset = new MediaAsset { StoreId = c.StoreId, StoragePath = path, OriginalFileName = "WEB02 image.jpg", ContentType = type,
            IsTemp = temp, TempToken = token, ExpireAtUtc = expires, IsDeleted = deleted, SizeBytes = 1234 };
        db.MediaAssets.Add(asset); await db.SaveChangesAsync();
        // AppDbContext deliberately initializes new rows as live. Model a later
        // deletion rather than asking Added state to retain an IsDeleted flag.
        if (deleted) await db.MediaAssets.Where(x => x.Id == asset.Id).ExecuteUpdateAsync(x => x.SetProperty(a => a.IsDeleted, true));
        Assert.Equal(deleted, await db.MediaAssets.IgnoreQueryFilters().Where(x => x.Id == asset.Id).Select(x => x.IsDeleted).SingleAsync());
        return asset.Id;
    }

    internal static async Task<string> SideEffectsAsync(this Web01Case c)
    {
        await using var db = c.Context();
        return JsonSerializer.Serialize(new
        {
            menus = await db.AdminMenuItems.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Title, x.Url, x.SortOrder, x.IsActive }).ToListAsync(),
            products = await db.Products.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.BasePrice, x.IsActive, x.IsSellable }).ToListAsync(),
            stock = await db.InventoryTransactions.CountAsync(), pos = await db.Orders.CountAsync(), money = await db.OrderPayments.CountAsync(),
            shifts = await db.POSShifts.CountAsync(), terminals = await db.POSTerminals.CountAsync(),
            orders = await db.StorefrontOrders.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Phase, x.GoodsAmount, x.ShippingCharge, x.TotalDue, x.RowVersion }).ToListAsync(),
            lines = await db.Set<StorefrontOrderLine>().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ItemName, x.UnitName, x.Quantity, x.BaseQuantity, x.UnitPrice, x.NetAmount, x.WeightKgPerSellingUnit, x.RowVersion }).ToListAsync(),
            quotes = await db.StorefrontQuotes.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.SnapshotJson, x.Fingerprint, x.GoodsAmount, x.ShippingCharge, x.TotalDue, x.RowVersion }).ToListAsync()
        });
    }
}
