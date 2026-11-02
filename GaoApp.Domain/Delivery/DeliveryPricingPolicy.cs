using System.Numerics;

namespace GaoApp.Domain.Delivery;

public static class DeliveryPricingPolicy
{
    public static IReadOnlyList<DeliveryApprovedPriceLine> ApprovePos(IReadOnlyList<DeliveryPosPriceLine> lines,
        decimal subtotal, decimal orderDiscount, decimal comboDiscount, decimal voucherDiscount, decimal grandTotal)
    {
        DeliveryValues.Require(lines.Count > 0 && lines.All(x => x.LineId > 0) &&
            lines.Select(x => x.LineId).Distinct().Count() == lines.Count, "LINES_INVALID", "Đơn cần dòng hàng có mã riêng.");
        foreach (var amount in new[] { subtotal, orderDiscount, comboDiscount, voucherDiscount, grandTotal })
            DeliveryValues.Money(amount, "Snapshot POS");
        DeliveryValues.Require(grandTotal > 0, "ZERO_PRICE_UNSUPPORTED", "Tổng đơn giao phải lớn hơn 0 như thanh toán POS.");
        var gross = new Dictionary<int, decimal>();
        var discounts = new Dictionary<int, decimal>();
        foreach (var line in lines)
        {
            DeliveryValues.Quantity(line.OrderedQuantity, "Lượng đặt");
            DeliveryValues.Require(line.OrderedQuantity > 0, "QUANTITY_INVALID", "Lượng đặt phải lớn hơn 0.");
            DeliveryValues.Money(line.UnitPrice, "Đơn giá", false);
            DeliveryValues.Money(line.LineDiscount, "Giảm dòng POS", false);
            DeliveryValues.Money(line.PromotionDiscount, "Khuyến mãi dòng POS", false);
            DeliveryValues.Money(line.LineTotal, "Thành tiền dòng POS");
            gross[line.LineId] = DeliveryValues.PricedAmount(line.OrderedQuantity, line.UnitPrice);
            discounts[line.LineId] = gross[line.LineId] - line.LineTotal;
            DeliveryValues.Require(line.LineDiscount + line.PromotionDiscount <= gross[line.LineId] &&
                DeliveryValues.RoundMoney(gross[line.LineId] - line.LineDiscount - line.PromotionDiscount) == line.LineTotal,
                "QUOTE_MISMATCH", "Giá dòng POS không khớp giảm giá đã áp dụng.");
        }
        var posSubtotal = DeliveryValues.RoundMoney(lines.Sum(x => x.RoundGrossInSubtotal
            ? gross[x.LineId] : x.OrderedQuantity * x.UnitPrice));
        DeliveryValues.Require(posSubtotal == subtotal, "QUOTE_MISMATCH", "Tạm tính POS không khớp các dòng nguồn.");
        // POS rounds the aggregate gross; reconcile its bounded whole-dong difference once,
        // deterministically, without changing the source prices or charging free gift lines.
        var beforeHeader = subtotal - DeliveryValues.RoundMoney(lines.Sum(x => x.LineDiscount)) -
            DeliveryValues.RoundMoney(lines.Sum(x => x.PromotionDiscount));
        var rounding = beforeHeader - lines.Sum(x => x.LineTotal);
        DeliveryValues.Require(beforeHeader >= 0 && Math.Abs(rounding) <= lines.Count + 1,
            "QUOTE_MISMATCH", "Chênh lệch làm tròn POS không hợp lệ.");
        var candidates = lines.Where(x => !x.IsGift && x.UnitPrice > 0 &&
            (rounding > 0 ? x.LineTotal > 0 || x.PromotionDiscount == 0 : x.LineTotal > 0))
            .OrderBy(x => x.LineId).ToArray();
        for (var remaining = Math.Abs(rounding); remaining > 0; remaining--)
        {
            var candidate = candidates.FirstOrDefault(x => rounding > 0 || gross[x.LineId] > discounts[x.LineId]);
            DeliveryValues.Require(candidate is not null, "QUOTE_MISMATCH", "Không thể phân bổ chênh lệch làm tròn POS.");
            gross[candidate!.LineId] += rounding > 0 ? 1 : -1;
        }
        var total = gross.Values.Sum() - discounts.Values.Sum();
        var headerDiscount = orderDiscount + comboDiscount + voucherDiscount;
        DeliveryValues.Require(total - headerDiscount == grandTotal && headerDiscount <= total,
            "QUOTE_MISMATCH", "Tổng giá/giảm giá POS không khớp; tải lại giỏ.");
        var denominator = new BigInteger(total);
        var shares = lines.Select(x =>
        {
            var numerator = new BigInteger(gross[x.LineId] - discounts[x.LineId]) * new BigInteger(headerDiscount);
            var whole = BigInteger.DivRem(numerator, denominator, out var remainder);
            return (x.LineId, Whole: (decimal)whole, Remainder: remainder);
        }).ToArray();
        var allocated = shares.ToDictionary(x => x.LineId, x => x.Whole);
        var residual = (int)(headerDiscount - allocated.Values.Sum());
        foreach (var share in shares.OrderByDescending(x => x.Remainder).ThenBy(x => x.LineId).Take(residual))
            allocated[share.LineId]++;
        return lines.Select(x => new DeliveryApprovedPriceLine(x.LineId, x.OrderedQuantity, gross[x.LineId],
            discounts[x.LineId], allocated[x.LineId], gross[x.LineId] - discounts[x.LineId] - allocated[x.LineId])).ToArray();
    }

    public static IReadOnlyList<DeliveryApprovedPriceLine> Approve(IReadOnlyList<DeliveryPriceLine> lines,
        decimal orderDiscount = 0m, bool hasConditionalPromotionOrVoucher = false,
        decimal additionalTaxOrFee = 0m, decimal advanceAmount = 0m)
    {
        DeliveryValues.Require(lines.Count > 0 && lines.All(x => x.LineId > 0) && lines.Select(x => x.LineId).Distinct().Count() == lines.Count,
            "LINES_INVALID", "Đơn cần dòng hàng có mã riêng.");
        DeliveryValues.Money(orderDiscount, "Giảm giá đơn");
        DeliveryValues.Money(additionalTaxOrFee, "Thuế/phí bổ sung");
        DeliveryValues.Money(advanceAmount, "Trả trước");
        DeliveryValues.Require(!hasConditionalPromotionOrVoucher && additionalTaxOrFee == 0m,
            "PRICE_FEATURE_UNSUPPORTED", "Đợt đầu chưa hỗ trợ combo/mua-tặng/voucher hoặc thuế/phí bổ sung trên đơn giao.");
        DeliveryValues.Require(advanceAmount == 0m, "ADVANCE_UNSUPPORTED", "Đợt đầu chưa hỗ trợ đơn giao có đặt cọc/trả trước.");

        var gross = new Dictionary<int, decimal>();
        var beforeOrderDiscount = new Dictionary<int, decimal>();
        foreach (var line in lines)
        {
            DeliveryValues.Quantity(line.OrderedQuantity, "Lượng đặt");
            DeliveryValues.Require(line.OrderedQuantity > 0, "QUANTITY_INVALID", "Lượng đặt phải > 0.");
            DeliveryValues.Money(line.UnitPrice, "Đơn giá", wholeDong: false);
            DeliveryValues.Money(line.ApprovedLineDiscount, "Giảm giá dòng");
            var amount = DeliveryValues.PricedAmount(line.OrderedQuantity, line.UnitPrice);
            DeliveryValues.Require(line.ApprovedLineDiscount <= amount, "DISCOUNT_EXCEEDS_PRICE", "Giảm giá vượt tiền dòng.");
            gross[line.LineId] = amount;
            beforeOrderDiscount[line.LineId] = amount - line.ApprovedLineDiscount;
        }
        var total = beforeOrderDiscount.Values.Sum();
        DeliveryValues.Money(total, "Tổng tiền");
        DeliveryValues.Require(orderDiscount <= total, "DISCOUNT_EXCEEDS_PRICE", "Giảm giá đơn vượt tiền hàng.");
        DeliveryValues.Require(total - orderDiscount > 0m, "ZERO_PRICE_UNSUPPORTED", "Đợt đầu chưa hỗ trợ đơn giao có tổng giá bằng 0.");

        // Largest remainder, stable line ID tie-break. Sum allocations == the approved whole-dong discount.
        // Divide first so valid large money values cannot overflow decimal by multiplication.
        var shares = beforeOrderDiscount.Select(x => new {
            Id = x.Key, Exact = total == 0 ? 0 : x.Value / total * orderDiscount
        }).ToList();
        var allocated = shares.ToDictionary(x => x.Id, x => decimal.Floor(x.Exact));
        var remainder = (int)(orderDiscount - allocated.Values.Sum());
        foreach (var share in shares.OrderByDescending(x => x.Exact - decimal.Floor(x.Exact)).ThenBy(x => x.Id).Take(remainder))
            allocated[share.Id] += 1m;
        return Array.AsReadOnly(lines.Select(x => new DeliveryApprovedPriceLine(x.LineId, x.OrderedQuantity,
            gross[x.LineId], x.ApprovedLineDiscount, allocated[x.LineId], beforeOrderDiscount[x.LineId] - allocated[x.LineId])).ToArray());
    }

    public static IReadOnlyList<DeliveryChargeLine> Charge(IReadOnlyList<DeliveryApprovedPriceLine> approved,
        IReadOnlyDictionary<int, decimal> deliveredQuantities)
    {
        DeliveryValues.Require(approved.Count > 0 && approved.All(x => x.LineId > 0) &&
            approved.Select(x => x.LineId).Distinct().Count() == approved.Count &&
            approved.Count == deliveredQuantities.Count && approved.All(x => deliveredQuantities.ContainsKey(x.LineId)),
            "PRICE_LINES_MISMATCH", "Giá và kết quả giao phải có cùng bộ dòng hàng.");
        var result = new List<DeliveryChargeLine>();
        foreach (var line in approved)
        {
            DeliveryValues.Quantity(line.OrderedQuantity, "Lượng đặt đã duyệt");
            DeliveryValues.Require(line.OrderedQuantity > 0, "QUANTITY_INVALID", "Lượng đặt phải > 0.");
            foreach (var value in new[] { line.Gross, line.LineDiscount, line.AllocatedOrderDiscount, line.Net })
                DeliveryValues.Money(value, "Snapshot tiền");
            DeliveryValues.Require(line.Net == line.Gross - line.LineDiscount - line.AllocatedOrderDiscount,
                "PRICE_SNAPSHOT_INVALID", "Snapshot giá đã duyệt không khớp.");
            var delivered = deliveredQuantities[line.LineId];
            DeliveryValues.Quantity(delivered, "Thực giao");
            DeliveryValues.Require(delivered <= line.OrderedQuantity, "DELIVERY_EXCEEDS_ORDER", "Thực giao vượt lượng đã duyệt.");
            var amount = DeliveryValues.RoundMoney(delivered / line.OrderedQuantity * line.Net);
            result.Add(new(line.LineId, delivered, amount));
        }
        return result.AsReadOnly();
    }
}
