using GaoApp.Application.DTOs.Products;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

public sealed partial class ProductRepository
{
    private async Task PopulateSaleUnitsAsync(int storeId, List<ProductListItemDto> products, CancellationToken ct)
    {
        if (products.Count == 0) return;
        var productIds = products.Select(x => x.Id).ToArray();
        // Two bounded batch reads for the current page, never one query per product or unit.
        var variants = await _db.ProductVariants.AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted && productIds.Contains(x.ProductId) &&
                x.Product.StoreId == storeId && !x.Product.IsDeleted)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ProductId, x.Sku, x.ProductVariantName, x.IsActive, x.Price, x.WholesalePrice,
                x.Product.Name, x.Product.BasePrice, x.Product.BaseUnitId, BaseUnitName = x.Product.BaseUnit.Name }).ToListAsync(ct);
        var variantIds = variants.Select(x => x.Id).ToArray();
        var units = await _db.ProductUnitConversions.AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted && variantIds.Contains(x.ProductVariantId) &&
                x.Factor > 0 && x.Unit.StoreId == storeId && !x.Unit.IsDeleted)
            .OrderBy(x => x.Factor).ThenBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.ProductVariantId, x.UnitId, UnitName = x.Unit.Name, x.Factor,
                x.IsActive, x.Price, x.WholesalePrice }).ToListAsync(ct);
        var unitsByVariant = units.ToLookup(x => x.ProductVariantId);
        var byProduct = products.ToDictionary(x => x.Id);
        foreach (var variant in variants)
        {
            var rows = byProduct[variant.ProductId].SaleUnits;
            var conversions = unitsByVariant[variant.Id].ToList();
            var name = string.IsNullOrWhiteSpace(variant.ProductVariantName) ? variant.Name : variant.ProductVariantName;
            // Same fallback as POS ResolveSalePriceByTier: a conversion's missing wholesale uses retail,
            // while variant wholesale is used only when there is no conversion.
            var baseRetail = Positive(variant.Price) ?? Positive(variant.BasePrice);
            if (!conversions.Any(x => x.UnitId == variant.BaseUnitId && x.Factor == 1))
                rows.Add(new(variant.Id, variant.Sku, name, variant.BaseUnitName, 1, true, variant.IsActive,
                    baseRetail, Positive(variant.WholesalePrice)));
            foreach (var unit in conversions)
                rows.Add(new(variant.Id, variant.Sku, name, unit.UnitName, unit.Factor,
                    unit.UnitId == variant.BaseUnitId && unit.Factor == 1, variant.IsActive && unit.IsActive,
                    Positive(unit.Price) ?? baseRetail, Positive(unit.WholesalePrice)));
        }
    }

    private static decimal? Positive(decimal? price) => price is > 0 ? price : null;
}
