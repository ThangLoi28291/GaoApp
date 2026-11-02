using GaoApp.Application.Services.Products;
using GaoApp.Application.Services.Promotions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Promotions;
using GaoApp.Tests.Promotions;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

// Independent expected results come from the actual existing POS engine and
// approved delivery snapshot policy. No web pricing formula is copied here.
internal sealed record Web04PosResult(Order Order, IReadOnlyList<DeliveryApprovedPriceLine> Approved);
internal static class Web04PosOracle
{
    internal static async Task<Web04PosResult> PriceAsync(AppDbContext db, IReadOnlyList<StorefrontCheckoutSelection> selections,
        IReadOnlySet<int>? knownIneligiblePromotions = null)
    {
        var store = db.CurrentStoreId!.Value;
        var variants = await db.ProductVariants.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.UnitConversions).ThenInclude(x => x.Unit)
            .Where(x => selections.Select(s => s.VariantId).Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var lines = new List<OrderLine>();
        for (var index = 0; index < selections.Count; index++)
        {
            var selected = selections[index]; var variant = variants[selected.VariantId];
            Assert.Equal(selected.ProductId, variant.ProductId); Assert.Equal(store, variant.StoreId);
            var conversion = variant.UnitConversions.Where(x => x.UnitId == selected.UnitId && x.IsActive && !x.IsDeleted &&
                    (selected.UnitId != variant.Product.BaseUnitId || x.Factor == 1))
                .OrderByDescending(x => x.IsBaseUnit).ThenBy(x => x.Id).FirstOrDefault();
            var unit = conversion?.Unit ?? variant.Product.BaseUnit;
            var multiplier = conversion?.Factor ?? 1;
            lines.Add(new() { Id = index + 1, StoreId = store, ProductId = selected.ProductId, VariantId = selected.VariantId, Variant = variant,
                ItemName = variant.Product.Name, Sku = variant.Sku, UnitName = unit.Name,
                ProductUnitConversionId = conversion?.Id, SellingUnitId = unit.Id, SellingUnitName = unit.Name,
                BaseUnitId = variant.Product.BaseUnitId, BaseUnitName = variant.Product.BaseUnit.Name,
                UnitPrice = ProductSalePricePolicy.Pos(variant, conversion, wholesale: false), Quantity = selected.Quantity,
                Multiplier = multiplier, BaseQuantity = selected.Quantity * multiplier });
        }
        // Pricing-only transient graph. No POS shift/cart/order is persisted.
        var order = new Order { StoreId = store, Lines = lines };
        var repository = new PromotionRepository(db);
        var product = await repository.GetActiveProductDiscountPromotionsAsync(store);
        var combos = await repository.GetActiveComboPromotionsAsync(store);
        if (knownIneligiblePromotions is not null)
        {
            product.RemoveAll(p => knownIneligiblePromotions.Contains(p.Id)); combos.RemoveAll(p => knownIneligiblePromotions.Contains(p.Id));
        }
        await new PromotionEngine(repository).ApplyOrderPromotionsAsync(order, product, combos);
        MixedQuantityPromotionTests.Recalc(order); // Actual POSService.Recalc entry point.
        var next = selections.Count + 1;
        foreach (var gift in order.Lines.Where(x => !x.IsDeleted && x.IsPromotionGift))
        { Assert.True(gift.GiftSourceLineId > 0, "Engine gift source identity must survive before web mapping."); gift.Id = next++; }
        var live = order.Lines.Where(x => !x.IsDeleted).ToArray();
        var approved = DeliveryPricingPolicy.ApprovePos(live.Select(x => new DeliveryPosPriceLine(x.Id, x.Quantity, x.UnitPrice,
            x.LineDiscount, x.PromotionDiscount, x.LineTotal, x.IsPromotionGift, x.PromotionType == PromotionType.ComboFixedPrice)).ToArray(),
            order.Subtotal, order.OrderDiscount, order.ComboDiscountTotal, order.VoucherDiscountTotal, order.GrandTotal);
        return new(order, approved);
    }
}
