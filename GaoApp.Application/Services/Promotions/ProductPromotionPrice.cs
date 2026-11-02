using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Promotions;

/// <summary>Shared selling price for POS and promotion shelf labels.</summary>
public static class ProductPromotionPrice
{
    public static decimal SalePrice(decimal retail, PromotionDiscountType type, decimal value)
    {
        if (retail <= 0 || value <= 0) return Math.Max(0, retail);
        var price = type switch
        {
            PromotionDiscountType.Percentage => retail * (1 - Math.Min(value, 100) / 100m),
            PromotionDiscountType.FixedAmount => retail - value,
            _ => retail
        };
        price = Math.Clamp(price, 0, retail);
        // Round the unit selling price, then multiply by quantity. Two identical
        // units must cost twice the shelf price, rather than rounding the basket.
        return type == PromotionDiscountType.Percentage
            ? Math.Min(retail, Math.Round(price / 100m, 0, MidpointRounding.AwayFromZero) * 100m)
            : price;
    }
}
