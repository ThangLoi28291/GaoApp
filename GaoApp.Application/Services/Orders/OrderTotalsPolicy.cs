using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Orders;

// Exact POS totals implementation shared with detached website pricing. No persistence or POS context is required.
public static class OrderTotalsPolicy
{
    public static void Recalculate(Order order)
    {
        var lines = order.Lines
            .Where(x => !x.IsDeleted)
            .ToList();

        // =========================
        // 1. TÍNH LẠI TỪNG DÒNG
        // =========================
        foreach (var l in lines)
        {
            var grossLineAmount = Math.Round(
                l.Quantity * l.UnitPrice,
                0,
                MidpointRounding.AwayFromZero);

            if (grossLineAmount < 0)
                grossLineAmount = 0;

            if (l.LineDiscount < 0)
                l.LineDiscount = 0;

            if (l.PromotionDiscount < 0)
                l.PromotionDiscount = 0;

            if (l.LineDiscount > grossLineAmount)
                l.LineDiscount = grossLineAmount;

            var remainAfterLineDiscount = Math.Max(
                grossLineAmount - l.LineDiscount,
                0);

            if (l.PromotionDiscount > remainAfterLineDiscount)
                l.PromotionDiscount = remainAfterLineDiscount;

            l.LineTotal = Math.Round(
                grossLineAmount - l.LineDiscount - l.PromotionDiscount,
                0,
                MidpointRounding.AwayFromZero);

            if (l.LineTotal < 0)
                l.LineTotal = 0;
        }

        // =========================
        // 2. TỔNG TIỀN HÀNG
        // =========================
        var subtotal = Math.Round(
            // Mixed-group discounts reconcile whole dong on each sale line.
            // Use the same gross basis so the bill and refunds share its total.
            lines.Sum(x => x.PromotionType == PromotionType.ComboFixedPrice
                ? Math.Round(x.Quantity * x.UnitPrice, 0, MidpointRounding.AwayFromZero)
                : x.Quantity * x.UnitPrice),
            0,
            MidpointRounding.AwayFromZero);

        if (subtotal < 0)
            subtotal = 0;

        var lineDiscountTotal = Math.Round(
            Math.Max(lines.Sum(x => x.LineDiscount), 0),
            0,
            MidpointRounding.AwayFromZero);

        var promotionDiscountTotal = Math.Round(
            Math.Max(lines.Sum(x => x.PromotionDiscount), 0),
            0,
            MidpointRounding.AwayFromZero);

        var comboDiscountTotal = Math.Round(
            Math.Max(order.ComboDiscountTotal, 0),
            0,
            MidpointRounding.AwayFromZero);

        order.Subtotal = subtotal;
        order.PromotionDiscountTotal = promotionDiscountTotal;
        order.ComboDiscountTotal = comboDiscountTotal;

        // =========================
        // 3. KHÓA COMBO KHÔNG VƯỢT TIỀN CÒN LẠI
        // =========================
        var maxComboDiscount = Math.Max(
            subtotal - lineDiscountTotal - promotionDiscountTotal,
            0);

        if (order.ComboDiscountTotal > maxComboDiscount)
            order.ComboDiscountTotal = maxComboDiscount;

        comboDiscountTotal = order.ComboDiscountTotal;

        // Nếu combo bị đưa về 0 thì xóa snapshot combo.
        if (comboDiscountTotal <= 0)
        {
            order.ComboPromotionId = null;
            order.ComboPromotionName = null;
            order.ComboPromotionNote = null;
        }

        // =========================
        // 4. GIẢM GIÁ ĐƠN HÀNG
        // =========================
        var maxOrderDiscount = Math.Max(
            subtotal
            - lineDiscountTotal
            - promotionDiscountTotal
            - comboDiscountTotal,
            0);

        if (order.OrderDiscount < 0)
            order.OrderDiscount = 0;

        order.OrderDiscount = Math.Round(
            order.OrderDiscount,
            0,
            MidpointRounding.AwayFromZero);

        if (order.OrderDiscount > maxOrderDiscount)
            order.OrderDiscount = maxOrderDiscount;

        // =========================
        // 5. VOUCHER
        // =========================
        var voucherDiscountTotal = order.RewardVouchers?
            .Where(x => !x.IsDeleted)
            .Sum(x => x.VoucherValue) ?? 0m;

        voucherDiscountTotal = Math.Round(
            Math.Max(voucherDiscountTotal, 0),
            0,
            MidpointRounding.AwayFromZero);

        var maxVoucherDiscount = Math.Max(
            subtotal
            - lineDiscountTotal
            - promotionDiscountTotal
            - comboDiscountTotal
            - order.OrderDiscount,
            0);

        if (voucherDiscountTotal > maxVoucherDiscount)
            voucherDiscountTotal = maxVoucherDiscount;

        order.VoucherDiscountTotal = voucherDiscountTotal;

        // =========================
        // 6. TỔNG GIẢM / TỔNG THANH TOÁN
        // =========================
        order.DiscountTotal = Math.Round(
            lineDiscountTotal
            + promotionDiscountTotal
            + comboDiscountTotal
            + order.OrderDiscount
            + order.VoucherDiscountTotal,
            0,
            MidpointRounding.AwayFromZero);

        order.GrandTotal = Math.Round(
            Math.Max(order.Subtotal - order.DiscountTotal, 0),
            0,
            MidpointRounding.AwayFromZero);

        // =========================
        // 7. THANH TOÁN
        // =========================
        order.PaidTotal = Math.Round(
            order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount) + order.DepositAmount,
            0,
            MidpointRounding.AwayFromZero);

        if (order.PaidTotal < 0)
            order.PaidTotal = 0;

        order.BalanceDue = Math.Max(order.GrandTotal - order.PaidTotal, 0);
        order.ChangeDue = Math.Max(order.PaidTotal - order.GrandTotal, 0);

        if (order.PaidTotal <= 0)
            order.PaymentStatus = PaymentStatus.Unpaid;
        else if (order.PaidTotal < order.GrandTotal)
            order.PaymentStatus = PaymentStatus.PartiallyPaid;
        else
            order.PaymentStatus = PaymentStatus.Paid;
    }
}
