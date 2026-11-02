using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Products;

// Unit prices come from stored unit/variant prices; no multiplication by pack size.
public static class ProductSalePricePolicy
{
    public static decimal? Retail(ProductVariant variant, ProductUnitConversion? unit, bool acceptZero = true)
    {
        static bool Valid(decimal? value, bool zero) => value is { } p && (zero ? p >= 0 : p > 0);
        if (Valid(unit?.Price, acceptZero)) return unit!.Price;
        if (Valid(variant.Price, acceptZero)) return variant.Price;
        return variant.Product.BasePrice >= 0 ? variant.Product.BasePrice : null;
    }
    public static decimal? Wholesale(ProductVariant variant, ProductUnitConversion? unit, bool acceptZero = true)
    {
        var price = unit is null ? variant.WholesalePrice : unit.WholesalePrice;
        return price is { } p && (acceptZero ? p >= 0 : p > 0) ? p : null;
    }
    // Existing POS uses positive overrides. Keep that behavior while sharing the source policy.
    public static decimal Pos(ProductVariant variant, ProductUnitConversion? unit, bool wholesale) =>
        (wholesale ? Wholesale(variant, unit, false) : null) ?? Retail(variant, unit, false) ?? 0;
}
