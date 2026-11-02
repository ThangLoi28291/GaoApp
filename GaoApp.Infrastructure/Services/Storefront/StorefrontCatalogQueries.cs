using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

// The sole read predicate used for list, direct detail and checkout resolution.
internal static class StorefrontCatalogQueries
{
    internal sealed class VariantRow
    {
        public Product Product { get; init; } = null!;
        public ProductVariant Variant { get; init; } = null!;
        public Unit BaseUnit { get; init; } = null!;
        public StorefrontProduct Publication { get; init; } = null!;
        public StorefrontVariant VariantPublication { get; init; } = null!;
        public bool ByWeight { get; init; }
    }

    internal static IQueryable<VariantRow> EligibleVariants(AppDbContext db, int store) =>
        from settings in db.StorefrontSettings.AsNoTracking()
        join shop in db.Stores.AsNoTracking() on settings.StoreId equals shop.Id
        join publication in db.StorefrontProducts.AsNoTracking() on settings.StoreId equals publication.StoreId
        join product in db.Products.AsNoTracking() on new { publication.StoreId, Id = publication.ProductId } equals new { product.StoreId, product.Id }
        join vp in db.StorefrontVariants.AsNoTracking() on new { publication.StoreId, Id = publication.Id, publication.ProductId } equals new { vp.StoreId, Id = vp.StorefrontProductId, vp.ProductId }
        join variant in db.ProductVariants.AsNoTracking() on new { vp.StoreId, Id = vp.VariantId, vp.ProductId } equals new { variant.StoreId, variant.Id, variant.ProductId }
        join unit in db.Units.AsNoTracking() on new { product.StoreId, Id = product.BaseUnitId } equals new { unit.StoreId, unit.Id }
        where settings.StoreId == store && settings.IsEnabled && shop.IsActive && publication.IsPublished &&
            product.IsActive && product.IsSellable && variant.IsActive && vp.IsPublished && unit.IsActive &&
            ((vp.BaseUnitIsPublished && (!settings.ShippingByWeight || vp.BaseUnitWeightKg > 0)) ||
             (from sale in db.StorefrontSellingUnits
              join conversion in db.ProductUnitConversions on new { sale.StoreId, Id = sale.ProductUnitConversionId, ProductVariantId = sale.VariantId, sale.UnitId }
                  equals new { conversion.StoreId, conversion.Id, conversion.ProductVariantId, conversion.UnitId }
              join sellingUnit in db.Units on new { sale.StoreId, Id = sale.UnitId } equals new { sellingUnit.StoreId, sellingUnit.Id }
              where sale.StoreId == store && sale.StorefrontVariantId == vp.Id && sale.VariantId == variant.Id && sale.IsPublished &&
                  conversion.IsActive && conversion.Factor > 0 && sellingUnit.IsActive && conversion.UnitId != product.BaseUnitId &&
                  (!settings.ShippingByWeight || sale.WeightKg > 0)
              select sale.Id).Any())
        select new VariantRow { Product = product, Variant = variant, BaseUnit = unit, Publication = publication,
            VariantPublication = vp, ByWeight = settings.ShippingByWeight };

    internal static ProductUnitConversion? BaseConversion(Product product, IEnumerable<ProductUnitConversion> conversions) =>
        conversions.Where(x => x.UnitId == product.BaseUnitId && x.Factor == 1 && x.IsActive && !x.IsDeleted && x.StoreId == product.StoreId)
            .OrderByDescending(x => x.IsBaseUnit).ThenBy(x => x.Id).FirstOrDefault();

    internal static string? MediaPath(MediaAsset asset, StorefrontMediaKind kind, bool allowCleanupDeadline = false)
    {
        if (asset.IsDeleted || asset.IsTemp || (!allowCleanupDeadline && asset.ExpireAtUtc is not null) || !string.IsNullOrEmpty(asset.TempToken)) return null;
        var path = asset.StoragePath?.TrimStart('/');
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("uploads/", StringComparison.Ordinal) ||
            path.Contains("..", StringComparison.Ordinal) || path.Contains('\\') || path.Contains('%') ||
            path.Contains('?') || path.Contains('#') || path.Contains(':') || path.Any(char.IsControl) ||
            path.Split('/').Any(x => x.Equals("_temp", StringComparison.OrdinalIgnoreCase))) return null;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var valid = kind == StorefrontMediaKind.Image
            ? new[] { ".jpg", ".jpeg", ".png", ".webp", ".avif", ".gif" }.Contains(extension) && asset.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
            : kind == StorefrontMediaKind.Model3D && new[] { ".glb", ".gltf" }.Contains(extension) &&
              asset.ContentType is "model/gltf-binary" or "model/gltf+json" or "application/octet-stream";
        return valid ? "/" + path : null;
    }
}
