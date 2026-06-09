using GaoApp.Application.Interfaces.Repositories.Promotions;
using GaoApp.Application.Interfaces.Services.Promotions;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Promotions;

public sealed class PromotionEngine : IPromotionEngine
{
    private readonly IPromotionRepository _promotionRepository;

    public PromotionEngine(IPromotionRepository promotionRepository)
    {
        _promotionRepository = promotionRepository;
    }

    // =========================================================
    // 1. KHUYẾN MÃI SẢN PHẨM
    // Type = ProductDiscount
    // Dùng bảng PromotionItems
    // Ghi vào OrderLine.PromotionDiscount
    // =========================================================
    public async Task ApplyLinePromotionAsync(
     Order order,
     OrderLine line,
     CancellationToken ct = default)
    {
        if (order == null || line == null)
            return;

        // Dòng hàng tặng không được áp giảm giá sản phẩm.
        if (line.IsPromotionGift)
        {
            ClearProductPromotionSnapshot(line);
            return;
        }

        ClearProductPromotionSnapshot(line);

        var effectiveStoreId = line.StoreId > 0
       ? line.StoreId
       : order.StoreId;

        if (effectiveStoreId <= 0)
            return;

        if (order.StoreId > 0 && effectiveStoreId != order.StoreId)
            return;

        if (line.StoreId <= 0)
            line.StoreId = effectiveStoreId;

        if (line.ProductId <= 0 || line.VariantId <= 0)
            return;

        if (line.Quantity <= 0 || line.UnitPrice <= 0)
            return;

        var originalUnitPrice = line.UnitPrice;

        var promotions = await _promotionRepository
      .GetActiveProductDiscountPromotionsAsync(effectiveStoreId, ct);

        if (!promotions.Any())
            return;

        var matched = promotions
            // Chỉ xử lý Type 1 ở hàm này.
            // Type 3 BuyXGetY sẽ xử lý riêng bằng gift line.
            .Where(p => p.Type == PromotionType.ProductDiscount)
            .Where(p => IsCustomerTierMatched(p, order))
            .SelectMany(p => p.Items
                .Where(i => IsProductPromotionItemMatched(i, line))
                .Select(i => new
                {
                    Promotion = p,
                    Item = i
                }))
            .OrderByDescending(x => x.Promotion.Priority)
            .ThenByDescending(x => x.Promotion.Id)
            .FirstOrDefault();

        if (matched == null)
            return;

        var discountAmount = CalculateProductDiscountAmount(
            matched.Promotion,
            originalUnitPrice,
            line.Quantity);

        if (discountAmount <= 0)
            return;

        var maxDiscount = originalUnitPrice * line.Quantity;

        if (discountAmount > maxDiscount)
            discountAmount = maxDiscount;

        line.OriginalUnitPrice = originalUnitPrice;
        line.PromotionDiscount = RoundVnd(discountAmount);
        line.PromotionId = matched.Promotion.Id;
        line.PromotionType = PromotionType.ProductDiscount;
        line.PromotionName = matched.Promotion.Name;

        line.PromotionBuyQuantity = 0m;
        line.PromotionGiftQuantity = 0m;

        // Không đổi UnitPrice.
        // Recalc sẽ trừ PromotionDiscount khỏi LineTotal.
        line.UnitPrice = originalUnitPrice;
    }
    private static decimal CalculateBuyXGetYGiftQuantity(
    Promotion promotion,
    decimal quantity)
    {
        if (quantity <= 0)
            return 0m;

        var buyQty = promotion.BuyQuantity ?? 0m;
        var getQty = promotion.GetQuantity ?? 0m;

        if (buyQty <= 0 || getQty <= 0)
            return 0m;

        var groupQty = promotion.RequireGiftQuantityInCart
            ? buyQty + getQty
            : buyQty;

        if (groupQty <= 0)
            return 0m;

        var comboCount = Math.Floor(quantity / groupQty);

        if (comboCount <= 0)
            return 0m;

        return comboCount * getQty;
    }
    public Task ClearLinePromotionAsync(OrderLine line)
    {
        ClearProductPromotionSnapshot(line);
        return Task.CompletedTask;
    }

    // =========================================================
    // 2. KHUYẾN MÃI COMBO
    // Type = ComboFixedPrice
    // Dùng bảng PromotionComboRule
    // Ghi vào Order.ComboDiscountTotal
    // =========================================================
    public async Task ApplyOrderComboPromotionAsync(
        Order order,
        CancellationToken ct = default)
    {
        if (order == null)
            return;

        ClearComboSnapshot(order);

        if (order.StoreId <= 0)
            return;

        var lines = order.Lines
            .Where(x =>
                !x.IsDeleted &&
                x.Quantity > 0 &&
                x.UnitPrice > 0 &&
                x.ProductId > 0 &&
                x.VariantId > 0)
            .ToList();

        if (!lines.Any())
            return;

        var promotions = await _promotionRepository
            .GetActiveComboPromotionsAsync(order.StoreId, ct);

        if (!promotions.Any())
            return;

        var bestCombo = promotions
            .Where(p => IsCustomerTierMatched(p, order))
            .Select(p => CalculateComboResult(p, lines))
            .Where(x => x != null && x.DiscountAmount > 0)
            .OrderByDescending(x => x!.Promotion.Priority)
            .ThenByDescending(x => x!.DiscountAmount)
            .ThenByDescending(x => x!.Promotion.Id)
            .FirstOrDefault();

        if (bestCombo == null)
            return;
        var note =
    string.IsNullOrWhiteSpace(bestCombo.Promotion.ComboNote)
        ? $"{bestCombo.Promotion.Name} giảm {bestCombo.DiscountAmount:#,##0}"
        : bestCombo.Promotion.ComboNote;

        order.ComboDiscountTotal = bestCombo.DiscountAmount;
        order.ComboPromotionId = bestCombo.Promotion.Id;
        order.ComboPromotionName = bestCombo.Promotion.Name;
        order.ComboPromotionNote = note;

        var involvedLines = bestCombo.MatchedParts
            .SelectMany(x => x.MatchedLines)
            .DistinctBy(x => x.Id)
            .ToList();

        var discountPerLine = involvedLines.Count > 0
            ? RoundVnd(bestCombo.DiscountAmount / involvedLines.Count)
            : 0m;

        foreach (var line in involvedLines)
        {
            line.ComboPromotionId = bestCombo.Promotion.Id;
            line.ComboPromotionName = bestCombo.Promotion.Name;
            line.ComboPromotionNote = note;
            line.ComboAllocatedDiscount = discountPerLine;
        }

        order.ComboDiscountTotal = bestCombo.DiscountAmount;
        order.ComboPromotionId = bestCombo.Promotion.Id;
        order.ComboPromotionName = bestCombo.Promotion.Name;
        order.ComboPromotionNote =
            string.IsNullOrWhiteSpace(bestCombo.Promotion.ComboNote)
                ? $"{bestCombo.Promotion.Name} giảm {bestCombo.DiscountAmount:#,##0}"
                : bestCombo.Promotion.ComboNote;
    }

    private static ComboResult? CalculateComboResult(
        Promotion promotion,
        List<OrderLine> lines)
    {
        if (promotion.Type != PromotionType.ComboFixedPrice)
            return null;

        if (!promotion.ComboFixedPrice.HasValue || promotion.ComboFixedPrice.Value <= 0)
            return null;

        var rules = promotion.ComboRules
            .Where(x => !x.IsDeleted)
            .ToList();

        if (!rules.Any())
            return null;

        var matchedParts = new List<ComboMatchedPart>();
        var possibleComboCount = decimal.MaxValue;

        foreach (var rule in rules)
        {
            var requiredQty = rule.RequiredQuantity > 0
                ? rule.RequiredQuantity
                : 1m;

            var matchedLines = lines
                .Where(line => IsComboRuleMatched(rule, line))
                .ToList();

            var availableQty = matchedLines.Sum(x => x.Quantity);

            if (availableQty < requiredQty)
                return null;

            var countByThisRule = Math.Floor(availableQty / requiredQty);
            possibleComboCount = Math.Min(possibleComboCount, countByThisRule);

            var avgUnitPrice = CalculateAverageUnitPriceAfterProductPromotion(matchedLines);

            matchedParts.Add(new ComboMatchedPart
            {
                Rule = rule,
                MatchedLines = matchedLines,
                RequiredQuantity = requiredQty,
                UnitPrice = avgUnitPrice
            });
        }

        if (possibleComboCount <= 0 || possibleComboCount == decimal.MaxValue)
            return null;

        var oneComboOriginalAmount = RoundVnd(
            matchedParts.Sum(x => x.RequiredQuantity * x.UnitPrice));

        var oneComboFixedPrice = RoundVnd(promotion.ComboFixedPrice.Value);

        var oneComboDiscount = oneComboOriginalAmount - oneComboFixedPrice;

        if (oneComboDiscount <= 0)
            return null;

        var totalDiscount = RoundVnd(oneComboDiscount * possibleComboCount);

        return new ComboResult
        {
            Promotion = promotion,
            ComboCount = possibleComboCount,
            OriginalAmount = RoundVnd(oneComboOriginalAmount * possibleComboCount),
            ComboFixedPrice = RoundVnd(oneComboFixedPrice * possibleComboCount),
            DiscountAmount = totalDiscount,
            MatchedParts = matchedParts
        };
    }

    private static bool IsProductPromotionItemMatched(
        PromotionItem item,
        OrderLine line)
    {
        if (item.ProductId > 0 && item.ProductId != line.ProductId)
            return false;

        if (item.VariantId.HasValue &&
            item.VariantId.Value > 0 &&
            item.VariantId.Value != line.VariantId)
            return false;

        if (item.ProductUnitConversionId.HasValue &&
            item.ProductUnitConversionId.Value > 0 &&
            item.ProductUnitConversionId.Value != line.ProductUnitConversionId)
            return false;

        if (item.MinQuantity > 0 && line.Quantity < item.MinQuantity)
            return false;

        return true;
    }

    private static bool IsComboRuleMatched(
        PromotionComboRule rule,
        OrderLine line)
    {
        if (rule.ProductId > 0 && rule.ProductId != line.ProductId)
            return false;

        if (rule.VariantId.HasValue &&
            rule.VariantId.Value > 0 &&
            rule.VariantId.Value != line.VariantId)
            return false;

        if (rule.ProductUnitConversionId.HasValue &&
            rule.ProductUnitConversionId.Value > 0 &&
            rule.ProductUnitConversionId.Value != line.ProductUnitConversionId)
            return false;

        return true;
    }

    private static decimal CalculateProductDiscountAmount(
        Promotion promotion,
        decimal unitPrice,
        decimal quantity)
    {
        var lineAmount = unitPrice * quantity;

        if (lineAmount <= 0)
            return 0m;

        if (promotion.DiscountValue <= 0)
            return 0m;

        return promotion.DiscountType switch
        {
            PromotionDiscountType.Percentage =>
                lineAmount * promotion.DiscountValue / 100m,

            PromotionDiscountType.FixedAmount =>
                promotion.DiscountValue * quantity,

            _ => 0m
        };
    }

    private static decimal CalculateAverageUnitPriceAfterProductPromotion(
        List<OrderLine> lines)
    {
        var totalQty = lines.Sum(x => x.Quantity);

        if (totalQty <= 0)
            return 0m;

        var grossAmount = lines.Sum(x => x.Quantity * x.UnitPrice);
        var productPromotionDiscount = lines.Sum(x => x.PromotionDiscount);

        var netAmount = Math.Max(grossAmount - productPromotionDiscount, 0);

        return RoundVnd(netAmount / totalQty);
    }

    private static bool IsCustomerTierMatched(Promotion promotion, Order order)
    {
        var promoTier = (promotion.CustomerPriceTier ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        // Null/rỗng = áp dụng tất cả
        if (string.IsNullOrWhiteSpace(promoTier))
            return true;

        var orderTier = (order.Customer?.PriceTier ?? CustomerPriceTiers.Retail)
            .Trim()
            .ToUpperInvariant();

        return promoTier == orderTier;
    }

    private static void ClearProductPromotionSnapshot(OrderLine line)
    {
        if (line.IsPromotionGift)
        {
            line.OriginalUnitPrice = 0m;
            line.PromotionDiscount = 0m;
            line.PromotionId = null;
            line.PromotionName = null;
            line.PromotionType = null;
            line.PromotionBuyQuantity = 0m;
            line.PromotionGiftQuantity = 0m;
            return;
        }

        line.OriginalUnitPrice = 0m;
        line.PromotionDiscount = 0m;
        line.PromotionId = null;
        line.PromotionName = null;
        line.PromotionType = null;
        line.PromotionBuyQuantity = 0m;
        line.PromotionGiftQuantity = 0m;
    }

    private static void ClearComboSnapshot(Order order)
    {
        order.ComboDiscountTotal = 0m;
        order.ComboPromotionId = null;
        order.ComboPromotionName = null;
        order.ComboPromotionNote = null;

        foreach (var line in order.Lines.Where(x => !x.IsDeleted))
        {
            line.ComboPromotionId = null;
            line.ComboPromotionName = null;
            line.ComboPromotionNote = null;
            line.ComboAllocatedDiscount = 0m;
        }
    }

    private static decimal RoundVnd(decimal value)
    {
        return Math.Round(value, 0, MidpointRounding.AwayFromZero);
    }
 
    /// <summary>
    /// Áp toàn bộ khuyến mãi cho 1 đơn POS.
    /// Tối ưu tốc độ:
    /// - Không query DB trong từng line.
    /// - productPromotions và comboPromotions đã được POSService lấy sẵn 1 lần.
    /// </summary>
    public Task ApplyOrderPromotionsAsync(
        Order order,
        IReadOnlyList<Promotion> productPromotions,
        IReadOnlyList<Promotion> comboPromotions,
        CancellationToken ct = default)
    {
        if (order == null)
            return Task.CompletedTask;

        // =========================================================
        // 1. TÁCH KHUYẾN MÃI THEO TYPE
        // =========================================================
        var productDiscountPromotions = (productPromotions ?? Array.Empty<Promotion>())
            .Where(x => x.Type == PromotionType.ProductDiscount)
            .ToList();

        var buyXGetYPromotions = (productPromotions ?? Array.Empty<Promotion>())
            .Where(x => x.Type == PromotionType.BuyXGetY)
            .ToList();

        // =========================================================
        // 2. XỬ LÝ BUY X GET Y TRƯỚC
        // Vì BuyXGetY có thể làm thay đổi Quantity dòng mua:
        // Ví dụ nhập 11 => dòng mua còn 10, dòng tặng 1.
        // =========================================================
        ApplyBuyXGetYGiftLinesFromLoadedPromotions(
            order,
            buyXGetYPromotions);

        // =========================================================
        // 3. ÁP TYPE 1 PRODUCT DISCOUNT
        // Chỉ áp lên dòng mua thật, không áp lên dòng hàng tặng.
        // =========================================================
        var normalLines = order.Lines
            .Where(x => !x.IsDeleted && !x.IsPromotionGift)
            .ToList();

        foreach (var line in normalLines)
        {
            ApplyLinePromotionFromLoadedPromotions(
                order,
                line,
                productDiscountPromotions);
        }

        // =========================================================
        // 4. ÁP TYPE 2 COMBO
        // Combo cũng chỉ tính dòng mua thật, không tính hàng tặng.
        // =========================================================
        ApplyComboPromotionFromLoadedPromotions(
            order,
            comboPromotions);

        return Task.CompletedTask;
    }
    // =========================================================
    // 3. BUY X GET Y - SINH ORDERLINE HÀNG TẶNG
    // =========================================================

    private static void ApplyBuyXGetYGiftLinesFromLoadedPromotions(
       Order order,
       IReadOnlyList<Promotion> promotions)
    {
        if (order == null)
            return;

        var activePromotions = (promotions ?? Array.Empty<Promotion>())
            .Where(x => x.Type == PromotionType.BuyXGetY)
            .ToList();

        if (!activePromotions.Any())
        {
            DeleteAllGiftLines(order);
            return;
        }

        var normalLines = order.Lines
            .Where(x =>
                !x.IsDeleted &&
                !x.IsPromotionGift &&
                x.ProductId > 0 &&
                x.VariantId > 0 &&
                x.Quantity > 0 &&
                x.UnitPrice > 0)
            .OrderBy(x => x.Id)
            .ToList();

        var touchedGiftKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in normalLines)
        {
            ClearBuyXGetYSnapshot(line);

            var matched = activePromotions
                .Where(p => IsCustomerTierMatched(p, order))
                .SelectMany(p => p.Items
                    .Where(i => IsProductPromotionItemMatched(i, line))
                    .Select(i => new
                    {
                        Promotion = p,
                        Item = i
                    }))
                .OrderByDescending(x => x.Promotion.Priority)
                .ThenByDescending(x => x.Promotion.Id)
                .FirstOrDefault();

            if (matched == null)
                continue;

            var promotion = matched.Promotion;
            var giftQty = CalculateBuyXGetYGiftQuantityForGiftLine(
                promotion,
                line.Quantity);

            if (giftQty <= 0)
                continue;

            line.PromotionType = PromotionType.BuyXGetY;
            line.PromotionBuyQuantity = promotion.BuyQuantity ?? 0m;
            line.PromotionGiftQuantity = giftQty;
            line.PromotionName = BuildBuyXGetYName(promotion);

            var key = BuildGiftKey(line, promotion);
            touchedGiftKeys.Add(key);

            UpsertGiftLine(
                order,
                sourceLine: line,
                promotion: promotion,
                giftQuantity: giftQty);
        }

        DeleteUntouchedGiftLines(order, touchedGiftKeys);
    }
    private static decimal CalculateBuyXGetYGiftQuantityForGiftLine(
    Promotion promotion,
    decimal buyQuantity)
    {
        if (buyQuantity <= 0)
            return 0m;

        var buyQty = promotion.BuyQuantity ?? 0m;
        var getQty = promotion.GetQuantity ?? 0m;

        if (buyQty <= 0 || getQty <= 0)
            return 0m;

        // POS chuẩn:
        // SL trên dòng mua là số lượng khách mua.
        // Hàng tặng là dòng riêng giá 0.
        //
        // Mua 10 tặng 1:
        // 9  => 0
        // 10 => 1
        // 11 => 1
        // 20 => 2
        return Math.Floor(buyQuantity / buyQty) * getQty;
    }

    private static string BuildGiftKey(OrderLine sourceLine, Promotion promotion)
    {
        return $"{sourceLine.Id}:{promotion.Id}";
    }

    private static void ClearBuyXGetYSnapshot(OrderLine line)
    {
        if (line.IsPromotionGift)
            return;

        if (line.PromotionType == PromotionType.BuyXGetY)
        {
            line.PromotionType = null;
            line.PromotionBuyQuantity = 0m;
            line.PromotionGiftQuantity = 0m;
            line.PromotionName = null;
        }
    }

    private static void RemoveOldGiftLines(Order order)
    {
        foreach (var giftLine in order.Lines.Where(x => !x.IsDeleted && x.IsPromotionGift))
        {
            giftLine.IsDeleted = true;
            giftLine.DeletedAtUtc = DateTime.UtcNow;
        }
    }

    private static (decimal PaidQuantity, decimal GiftQuantity) CalculateBuyXGetYPaidAndGiftQuantity(
        Promotion promotion,
        decimal currentPaidQuantity,
        decimal oldGiftQuantity)
    {
        if (currentPaidQuantity <= 0)
            return (0m, 0m);

        var buyQty = promotion.BuyQuantity ?? 0m;
        var getQty = promotion.GetQuantity ?? 0m;

        if (buyQty <= 0 || getQty <= 0)
            return (currentPaidQuantity, 0m);

        // =====================================================
        // RequireGiftQuantityInCart = true:
        // User nhập tổng số hàng khách cầm.
        //
        // Ví dụ mua 10 tặng 1:
        // - Nhập 11 => trả tiền 10, tặng 1
        // - Nhập 22 => trả tiền 20, tặng 2
        //
        // currentPaidQuantity + oldGiftQuantity là tổng thực tế
        // trước khi tính lại.
        // =====================================================
        if (promotion.RequireGiftQuantityInCart)
        {
            var totalPhysicalQuantity = currentPaidQuantity + oldGiftQuantity;
            var groupQty = buyQty + getQty;

            if (groupQty <= 0 || totalPhysicalQuantity < groupQty)
                return (totalPhysicalQuantity, 0m);

            var groupCount = Math.Floor(totalPhysicalQuantity / groupQty);
            var giftQuantity = groupCount * getQty;
            var paidQuantity = totalPhysicalQuantity - giftQuantity;

            if (paidQuantity < 0)
                paidQuantity = 0;

            return (paidQuantity, giftQuantity);
        }

        // =====================================================
        // RequireGiftQuantityInCart = false:
        // User nhập số lượng mua.
        // Hệ thống tặng thêm ngoài số lượng mua.
        //
        // Ví dụ nhập 10 => dòng mua 10, dòng tặng 1.
        // =====================================================
        var giftQty = Math.Floor(currentPaidQuantity / buyQty) * getQty;

        return (currentPaidQuantity, giftQty);
    }

    private static void UpsertGiftLine(
    Order order,
    OrderLine sourceLine,
    Promotion promotion,
    decimal giftQuantity)
    {
        if (giftQuantity <= 0)
            return;

        var multiplier = sourceLine.Multiplier <= 0 ? 1m : sourceLine.Multiplier;
        var giftNote = BuildBuyXGetYNote(promotion);

        var existingGift = order.Lines.FirstOrDefault(x =>
            !x.IsDeleted &&
            x.IsPromotionGift &&
            x.GiftSourceLineId == sourceLine.Id &&
            x.GiftPromotionId == promotion.Id);

        if (existingGift != null)
        {
            existingGift.Quantity = giftQuantity;
            existingGift.BaseQuantity = giftQuantity * multiplier;
            existingGift.Multiplier = multiplier;

            existingGift.ProductId = sourceLine.ProductId;
            existingGift.VariantId = sourceLine.VariantId;
            existingGift.Variant = sourceLine.Variant;

            existingGift.ItemName = sourceLine.ItemName;
            existingGift.UnitName = sourceLine.UnitName;
            existingGift.Sku = sourceLine.Sku;
            existingGift.Barcode = sourceLine.Barcode;

            existingGift.ProductUnitConversionId = sourceLine.ProductUnitConversionId;
            existingGift.SellingUnitId = sourceLine.SellingUnitId;
            existingGift.SellingUnitName = sourceLine.SellingUnitName;
            existingGift.BaseUnitId = sourceLine.BaseUnitId;
            existingGift.BaseUnitName = sourceLine.BaseUnitName;

            existingGift.UnitPrice = 0m;
            existingGift.LineDiscount = 0m;
            existingGift.PromotionDiscount = 0m;
            existingGift.LineTotal = 0m;
            existingGift.OriginalUnitPrice = 0m;

            existingGift.GiftPromotionName = promotion.Name;
            existingGift.GiftPromotionNote = giftNote;

            existingGift.PromotionId = null;
            existingGift.PromotionName = null;
            existingGift.PromotionType = null;
            existingGift.PromotionBuyQuantity = 0m;
            existingGift.PromotionGiftQuantity = 0m;

            existingGift.ComboPromotionId = null;
            existingGift.ComboPromotionName = null;
            existingGift.ComboPromotionNote = null;
            existingGift.ComboAllocatedDiscount = 0m;

            return;
        }

        order.Lines.Add(new OrderLine
        {
            StoreId = sourceLine.StoreId > 0 ? sourceLine.StoreId : order.StoreId,
            OrderId = order.Id,

            ProductId = sourceLine.ProductId,
            VariantId = sourceLine.VariantId,
            Variant = sourceLine.Variant,

            ItemName = sourceLine.ItemName,
            UnitName = sourceLine.UnitName,
            Sku = sourceLine.Sku,
            Barcode = sourceLine.Barcode,

            Quantity = giftQuantity,
            BaseQuantity = giftQuantity * multiplier,
            Multiplier = multiplier,

            ProductUnitConversionId = sourceLine.ProductUnitConversionId,
            SellingUnitId = sourceLine.SellingUnitId,
            SellingUnitName = sourceLine.SellingUnitName,
            BaseUnitId = sourceLine.BaseUnitId,
            BaseUnitName = sourceLine.BaseUnitName,

            ScannedBarcode = sourceLine.ScannedBarcode,
            BarcodeSource = sourceLine.BarcodeSource,

            UnitPrice = 0m,
            LineDiscount = 0m,
            PromotionDiscount = 0m,
            LineTotal = 0m,
            OriginalUnitPrice = 0m,

            IsPromotionGift = true,
            GiftPromotionId = promotion.Id,
            GiftSourceLineId = sourceLine.Id > 0 ? sourceLine.Id : null,
            GiftPromotionName = promotion.Name,
            GiftPromotionNote = giftNote,

            PromotionId = null,
            PromotionName = null,
            PromotionType = null,
            PromotionBuyQuantity = 0m,
            PromotionGiftQuantity = 0m,

            ComboPromotionId = null,
            ComboPromotionName = null,
            ComboPromotionNote = null,
            ComboAllocatedDiscount = 0m
        });
    }
    private static void DeleteAllGiftLines(Order order)
    {
        foreach (var giftLine in order.Lines.Where(x => !x.IsDeleted && x.IsPromotionGift))
        {
            giftLine.IsDeleted = true;
            giftLine.DeletedAtUtc = DateTime.UtcNow;
        }
    }

    private static void DeleteUntouchedGiftLines(
        Order order,
        HashSet<string> touchedGiftKeys)
    {
        var giftLines = order.Lines
            .Where(x => !x.IsDeleted && x.IsPromotionGift)
            .ToList();

        foreach (var giftLine in giftLines)
        {
            if (!giftLine.GiftSourceLineId.HasValue ||
                !giftLine.GiftPromotionId.HasValue)
            {
                giftLine.IsDeleted = true;
                giftLine.DeletedAtUtc = DateTime.UtcNow;
                continue;
            }

            var key = $"{giftLine.GiftSourceLineId.Value}:{giftLine.GiftPromotionId.Value}";

            if (!touchedGiftKeys.Contains(key))
            {
                giftLine.IsDeleted = true;
                giftLine.DeletedAtUtc = DateTime.UtcNow;
            }
        }
    }

    private static string BuildBuyXGetYName(Promotion promotion)
    {
        var buyQty = promotion.BuyQuantity ?? 0m;
        var getQty = promotion.GetQuantity ?? 0m;

        return $"{promotion.Name} - Mua {buyQty:n0} tặng {getQty:n0}";
    }

    private static string BuildBuyXGetYNote(Promotion promotion)
    {
        var buyQty = promotion.BuyQuantity ?? 0m;
        var getQty = promotion.GetQuantity ?? 0m;

        return $"Hàng tặng từ CTKM: Mua {buyQty:n0} tặng {getQty:n0}";
    }
    private void ApplyLinePromotionFromLoadedPromotions(
    Order order,
    OrderLine line,
    IReadOnlyList<Promotion> promotions)
    {
        ClearProductPromotionSnapshot(line);
        if (line.IsPromotionGift)
            return;

        var effectiveStoreId = line.StoreId > 0
     ? line.StoreId
     : order.StoreId;

        if (effectiveStoreId <= 0)
            return;

        if (order.StoreId > 0 && effectiveStoreId != order.StoreId)
            return;

        if (line.StoreId <= 0)
            line.StoreId = effectiveStoreId;

        if (line.ProductId <= 0 || line.VariantId <= 0)
            return;

        if (line.Quantity <= 0 || line.UnitPrice <= 0)
            return;

        if (promotions == null || promotions.Count == 0)
            return;

        var originalUnitPrice = line.UnitPrice;

        var matched = promotions
            .Where(p => IsCustomerTierMatched(p, order))
            .SelectMany(p => p.Items
                .Where(i => IsProductPromotionItemMatched(i, line))
                .Select(i => new
                {
                    Promotion = p,
                    Item = i
                }))
            .OrderByDescending(x => x.Promotion.Priority)
            .ThenByDescending(x => x.Promotion.Id)
            .FirstOrDefault();

        if (matched == null)
            return;

        var discountAmount = matched.Promotion.Type switch
        {
            PromotionType.ProductDiscount => CalculateProductDiscountAmount(
                matched.Promotion,
                originalUnitPrice,
                line.Quantity),

            _ => 0m
        };

        if (discountAmount <= 0)
            return;

        var maxDiscount = originalUnitPrice * line.Quantity;

        if (discountAmount > maxDiscount)
            discountAmount = maxDiscount;

        line.OriginalUnitPrice = originalUnitPrice;
        line.PromotionDiscount = RoundVnd(discountAmount);
        line.PromotionId = matched.Promotion.Id;
        line.PromotionType = matched.Promotion.Type;

        if (matched.Promotion.Type == PromotionType.BuyXGetY)
        {
            var giftQty = CalculateBuyXGetYGiftQuantity(
                matched.Promotion,
                line.Quantity);

            line.PromotionBuyQuantity = matched.Promotion.BuyQuantity ?? 0m;
            line.PromotionGiftQuantity = giftQty;

            line.PromotionName =
                $"{matched.Promotion.Name} - Mua {matched.Promotion.BuyQuantity:n0} tặng {matched.Promotion.GetQuantity:n0}";
        }
        else
        {
            line.PromotionBuyQuantity = 0m;
            line.PromotionGiftQuantity = 0m;
            line.PromotionName = matched.Promotion.Name;
        }

        line.UnitPrice = originalUnitPrice;
    }
    private void ApplyComboPromotionFromLoadedPromotions(
    Order order,
    IReadOnlyList<Promotion> promotions)
    {
        ClearComboSnapshot(order);

        if (order.StoreId <= 0)
            return;

        var lines = order.Lines
     .Where(x =>
         !x.IsDeleted &&
         !x.IsPromotionGift &&
         x.Quantity > 0 &&
         x.UnitPrice > 0 &&
         x.ProductId > 0 &&
         x.VariantId > 0)
     .ToList();

        if (!lines.Any())
            return;

        if (promotions == null || promotions.Count == 0)
            return;

        var bestCombo = promotions
            .Where(p => IsCustomerTierMatched(p, order))
            .Select(p => CalculateComboResult(p, lines))
            .Where(x => x != null && x.DiscountAmount > 0)
            .OrderByDescending(x => x!.Promotion.Priority)
            .ThenByDescending(x => x!.DiscountAmount)
            .ThenByDescending(x => x!.Promotion.Id)
            .FirstOrDefault();

        if (bestCombo == null)
            return;

        var note =
            string.IsNullOrWhiteSpace(bestCombo.Promotion.ComboNote)
                ? $"{bestCombo.Promotion.Name} giảm {bestCombo.DiscountAmount:#,##0}"
                : bestCombo.Promotion.ComboNote;

        order.ComboDiscountTotal = bestCombo.DiscountAmount;
        order.ComboPromotionId = bestCombo.Promotion.Id;
        order.ComboPromotionName = bestCombo.Promotion.Name;
        order.ComboPromotionNote = note;

        var involvedLines = bestCombo.MatchedParts
            .SelectMany(x => x.MatchedLines)
            .DistinctBy(x => x.Id)
            .ToList();

        var discountPerLine = involvedLines.Count > 0
            ? RoundVnd(bestCombo.DiscountAmount / involvedLines.Count)
            : 0m;

        foreach (var line in involvedLines)
        {
            line.ComboPromotionId = bestCombo.Promotion.Id;
            line.ComboPromotionName = bestCombo.Promotion.Name;
            line.ComboPromotionNote = note;
            line.ComboAllocatedDiscount = discountPerLine;
        }
    }

    private sealed class ComboResult
    {
        public Promotion Promotion { get; set; } = default!;

        public decimal ComboCount { get; set; }

        public decimal OriginalAmount { get; set; }

        public decimal ComboFixedPrice { get; set; }

        public decimal DiscountAmount { get; set; }

        public List<ComboMatchedPart> MatchedParts { get; set; } = new();
    }

    private sealed class ComboMatchedPart
    {
        public PromotionComboRule Rule { get; set; } = default!;

        public List<OrderLine> MatchedLines { get; set; } = new();

        public decimal RequiredQuantity { get; set; }

        public decimal UnitPrice { get; set; }
    }
}