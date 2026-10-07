using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Promotions;

public sealed partial class PromotionEngine
{
    private static void ApplyMixedQuantityPromotions(
        Order order, IReadOnlyList<Promotion>? promotions)
    {
        // These discounts belong to individual sale lines, so returns use the
        // actual amount paid. They are not also added to ComboDiscountTotal.
        var claimedLines = new HashSet<OrderLine>();
        var candidates = (promotions ?? Array.Empty<Promotion>())
            .Where(p => p.StoreId == order.StoreId && !p.IsDeleted &&
                p.Type == PromotionType.ComboFixedPrice &&
                p.ComboPricingMode == ComboPricingMode.MixedQuantity &&
                p.ComboQuantity > 0m && p.ComboFixedPrice > 0m &&
                p.ComboBaseUnitId > 0 && IsCustomerTierMatched(p, order))
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.ComboFixedPrice!.Value / p.ComboQuantity!.Value)
            .ThenByDescending(p => p.Id);

        foreach (var promotion in candidates)
        {
            var members = promotion.ComboRules.Where(x => !x.IsDeleted).ToList();
            var lines = order.Lines.Where(line =>
                    !line.IsDeleted && !line.IsPromotionGift &&
                    line.StoreId == order.StoreId && line.Quantity > 0m &&
                    line.UnitPrice > 0m && line.Multiplier > 0m &&
                    line.BaseUnitId == promotion.ComboBaseUnitId &&
                    !claimedLines.Contains(line) &&
                    members.Any(member => member.ProductId == line.ProductId &&
                        member.VariantId == line.VariantId))
                .OrderBy(line => line.Id).ThenBy(line => line.VariantId)
                .ThenBy(line => line.ProductUnitConversionId)
                .ToList();

            // Recompute from the selling-unit snapshot; stale BaseQuantity must
            // not keep a cart eligible after its quantity was changed.
            var totalBaseQuantity = lines.Sum(line => line.Quantity * line.Multiplier);
            if (totalBaseQuantity < promotion.ComboQuantity!.Value)
                continue;

            var baseUnitPrice = promotion.ComboFixedPrice!.Value / promotion.ComboQuantity.Value;
            var grossAmount = lines.Sum(line => RoundVnd(line.Quantity * line.UnitPrice));
            var targetAmount = RoundVnd(totalBaseQuantity * baseUnitPrice);
            var capacities = lines.Select(line => Math.Max(0m,
                RoundVnd(line.Quantity * line.UnitPrice) - line.LineDiscount)).ToArray();
            var discount = Math.Min(Math.Max(0m, RoundVnd(grossAmount) - targetAmount),
                capacities.Sum());

            // Product discounts and group prices compete; do not stack the two
            // on the same quantities or replace a better existing discount.
            if (discount <= 0m || discount <= lines.Sum(line => line.PromotionDiscount))
                continue;

            var availableLines = order.Lines.Where(line => !line.IsDeleted && !line.IsPromotionGift &&
                line.Quantity > 0m && line.UnitPrice > 0m && !claimedLines.Contains(line)).ToList();
            var competingCombo = (promotions ?? Array.Empty<Promotion>())
                .Where(p => p.StoreId == order.StoreId && IsCustomerTierMatched(p, order))
                .Select(p => CalculateComboResult(p, availableLines))
                .Where(result => result != null && result.DiscountAmount > 0m)
                .OrderByDescending(result => result!.Promotion.Priority)
                .ThenByDescending(result => result!.DiscountAmount)
                .ThenByDescending(result => result!.Promotion.Id)
                .FirstOrDefault();
            if (competingCombo != null &&
                competingCombo.MatchedParts.SelectMany(part => part.MatchedLines).Any(lines.Contains) &&
                (competingCombo.Promotion.Priority > promotion.Priority ||
                    (competingCombo.Promotion.Priority == promotion.Priority && competingCombo.DiscountAmount >= discount)))
                continue;

            var allocations = lines.Select((line, index) => Math.Min(capacities[index],
                Math.Max(0m, RoundVnd(line.Quantity * line.UnitPrice) -
                    RoundVnd(line.Quantity * line.Multiplier * baseUnitPrice)))).ToArray();
            var remainder = discount - allocations.Sum();
            for (var index = 0; index < allocations.Length && remainder != 0m; index++)
            {
                var adjustment = remainder > 0m
                    ? Math.Min(remainder, capacities[index] - allocations[index])
                    : -Math.Min(-remainder, allocations[index]);
                allocations[index] += adjustment;
                remainder -= adjustment;
            }

            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                line.OriginalUnitPrice = line.UnitPrice;
                line.PromotionDiscount = allocations[index];
                line.PromotionId = promotion.Id;
                line.PromotionType = PromotionType.ComboFixedPrice;
                line.PromotionName = promotion.Name;
                claimedLines.Add(line);
            }
        }
    }
}
