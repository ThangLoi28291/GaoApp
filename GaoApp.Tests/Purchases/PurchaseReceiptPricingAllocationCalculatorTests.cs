using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptPricingAllocationCalculatorTests
{
    private readonly PurchaseReceiptPricingAllocationCalculator _calculator = new();

    internal static PurchaseReceiptPricingPhysicalLine Physical(int id, decimal quantity, int variant = 1) => new()
    {
        StockDocumentLineId = id, LineNo = id, ProductVariantId = variant, ProductId = variant,
        UnitId = 1, Factor = 1, Quantity = quantity, BaseQuantity = quantity,
        Units = [new(1, "Hộp", 1, 1, "", ""), new(2, "Thùng", 2, 48, "", "")]
    };
    internal static PurchaseReceiptPricingLineInput Bill(int id, decimal qty, decimal price, int unit = 1) => new()
        { StockDocumentLineId = id, BillUnitId = unit, BillQuantity = qty, BillUnitPriceBeforeVat = price };
    internal static PurchaseReceiptPricingRuleInput Gift(string key, int target, decimal qty, params int[] sources) => new()
    {
        RuleKey = key, Type = PurchaseReceiptPricingRuleType.Gift, GiftMode = PurchaseReceiptPricingGiftMode.DifferentSku,
        GiftLineId = target, GiftUnitId = 1, GiftQuantity = qty, SourceLineIds = [.. sources]
    };

    [Fact]
    public void Fractional_bill_carton_reconciles_exactly_to_three_physical_boxes()
    {
        var result = _calculator.Calculate(new() { ActualBillTotal = 30, Lines = [Bill(1, .0625m, 480, 2)] }, [Physical(1, 3)]);
        Assert.True(result.CanApply, string.Join(";", result.Errors));
        Assert.Equal(3m, result.Lines.Single().PurchasedBaseQuantity);
        Assert.Equal(30m, result.SystemTotal);
    }

    [Fact]
    public void Bill_cartons_reconcile_with_physical_boxes_without_changing_the_receipt()
    {
        var physical = Physical(1, 96);
        var result = _calculator.Calculate(new() { ActualBillTotal = 960, Lines = [Bill(1, 2, 480, 2)] }, [physical]);
        Assert.True(result.CanApply, string.Join(";", result.Errors));
        Assert.Equal(960m, result.Lines.Single().FinalAmountBeforeVat);
        Assert.Equal(10m, result.Lines.Single().EffectiveUnitPriceBeforeVat);
        Assert.Equal(96m, physical.Quantity);
    }

    [Fact]
    public void Same_sku_gift_blends_purchase_amount_over_all_received_quantity_without_history()
    {
        var rule = Gift("same", 1, 1, 1); rule.GiftMode = PurchaseReceiptPricingGiftMode.SameSku;
        var result = _calculator.Calculate(new() { ActualBillTotal = 1000, Lines = [Bill(1, 10, 100)], Rules = [rule] }, [Physical(1, 11)]);
        Assert.True(result.CanApply, string.Join(";", result.Errors));
        var line = result.Lines.Single();
        Assert.Equal(1000m, line.FinalAmountBeforeVat);
        Assert.Equal(90.91m, line.EffectiveUnitPriceBeforeVat);
        Assert.Equal(10m, line.PurchasedBaseQuantity); Assert.Equal(1m, line.GiftBaseQuantity);
    }

    [Fact]
    public void Overlapping_rules_use_one_immutable_baseline_and_fixed_gift_value_in_any_order()
    {
        var rules = new[] { Gift("r1", 3, 3, 1, 2), Gift("r2", 3, 5, 1) };
        var request = new PurchaseReceiptPricingAllocationRequest
        {
            ActualBillTotal = 3200, Lines = [Bill(1, 10, 100), Bill(2, 20, 100), Bill(3, 2, 100)], Rules = [.. rules]
        };
        var physical = new[] { Physical(1, 10, 1), Physical(2, 20, 2), Physical(3, 10, 8) };
        var valuations = new[] { new PurchaseReceiptPricingGiftValue(8, 1, 1, 10, PurchaseReceiptGiftValuationSource.Manual) };
        var first = _calculator.Calculate(request, physical, valuations);
        request.Rules.Reverse();
        var reverse = _calculator.Calculate(request, physical, valuations);
        Assert.True(first.CanApply, string.Join(";", first.Errors));
        Assert.Equal(3200m, first.SystemTotal);
        Assert.Equal(940m, first.Lines.Single(x => x.StockDocumentLineId == 1).PurchasedAmount);
        Assert.Equal(1980m, first.Lines.Single(x => x.StockDocumentLineId == 2).PurchasedAmount);
        Assert.Equal(280m, first.Lines.Single(x => x.StockDocumentLineId == 3).FinalAmountBeforeVat);
        Assert.Equal(first.Lines.Select(x => x.FinalAmountBeforeVat), reverse.Lines.Select(x => x.FinalAmountBeforeVat));
    }

    [Fact]
    public void Line_discount_then_global_discount_precedes_gift_burdens()
    {
        var request = new PurchaseReceiptPricingAllocationRequest
        {
            ActualBillTotal = 810, GlobalDiscountPercent = 10,
            Lines = [Bill(1, 10, 100), Bill(2, 0, 0)],
            Rules = [new() { RuleKey = "discount", Type = PurchaseReceiptPricingRuleType.PercentageDiscount, DiscountPercent = 10, SourceLineIds = [1] }, Gift("gift", 2, 1, 1)]
        };
        var result = _calculator.Calculate(request, [Physical(1, 10), Physical(2, 1, 2)],
            [new(2, 1, 1, 50, PurchaseReceiptGiftValuationSource.Manual)]);
        Assert.True(result.CanApply, string.Join(";", result.Errors));
        Assert.Equal(810m, result.Lines.Single(x => x.StockDocumentLineId == 1).BaselineAmount);
        Assert.Equal(760m, result.Lines.Single(x => x.StockDocumentLineId == 1).PurchasedAmount);
        Assert.Equal(50m, result.Lines.Single(x => x.StockDocumentLineId == 2).GiftAmount);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1000)]
    public void Zero_negative_or_exhausting_gift_values_block_apply(decimal giftValue)
    {
        var result = _calculator.Calculate(new() { ActualBillTotal = 1000, Lines = [Bill(1, 10, 100), Bill(2, 0, 0)], Rules = [Gift("gift", 2, 1, 1)] },
            [Physical(1, 10), Physical(2, 1, 2)], [new(2, 1, 1, giftValue, PurchaseReceiptGiftValuationSource.Manual)]);
        Assert.False(result.CanApply);
    }

    [Fact]
    public void Group_discount_evidence_conserves_rounded_group_amount_with_deterministic_residual()
    {
        var result = _calculator.Calculate(new()
        {
            ActualBillTotal = 2.84m,
            Lines = [Bill(1, 1, 1.05m), Bill(2, 1, 1.05m), Bill(3, 1, 1.05m)],
            Rules = [new() { RuleKey = "group", Type = PurchaseReceiptPricingRuleType.PercentageDiscount,
                DiscountPercent = 10, SourceLineIds = [3, 2, 1] }]
        }, [Physical(1, 1), Physical(2, 1), Physical(3, 1)]);
        Assert.True(result.CanApply, string.Join(";", result.Errors));
        var discount = result.Rules.Single();
        Assert.Equal(.32m, discount.Amount);
        Assert.Equal(discount.Amount, discount.SourceAmounts.Values.Sum());
        Assert.Equal(.12m, discount.SourceAmounts[1]);
        Assert.Equal(.10m, discount.SourceAmounts[2]);
        Assert.Equal(.10m, discount.SourceAmounts[3]);
        Assert.Contains(result.Residuals, x => x.Stage == "discount" && x.RuleKey == "group" &&
            x.StockDocumentLineId == 1 && x.Amount == .02m);
        Assert.Equal(2.84m, result.SystemTotal);
    }

    [Fact]
    public void Mismatched_bill_total_is_visible_and_never_silently_assigned()
    {
        var result = _calculator.Calculate(new() { ActualBillTotal = 999, Lines = [Bill(1, 10, 100)] }, [Physical(1, 10)]);
        Assert.False(result.CanApply); Assert.Equal(1000m, result.SystemTotal); Assert.Equal(1m, result.Difference);
        Assert.Equal(1000m, result.Lines.Single().FinalAmountBeforeVat);
    }

    [Fact]
    public void Two_line_discount_rules_cannot_stack_on_one_purchase_line()
    {
        var result = _calculator.Calculate(new() { ActualBillTotal = 810, Lines = [Bill(1, 10, 100)], Rules = [
            new() { RuleKey = "a", Type = PurchaseReceiptPricingRuleType.PercentageDiscount, DiscountPercent = 10, SourceLineIds = [1] },
            new() { RuleKey = "b", Type = PurchaseReceiptPricingRuleType.PercentageDiscount, DiscountPercent = 10, SourceLineIds = [1] }] }, [Physical(1, 10)]);
        Assert.False(result.CanApply);
    }

    [Fact]
    public void Residual_cent_uses_largest_weight_then_line_number_and_exact_amount_is_authority()
    {
        var a = Physical(20, 3, 1); a.LineNo = 1;
        var b = Physical(10, 3, 2); b.LineNo = 2;
        var request = new PurchaseReceiptPricingAllocationRequest { ActualBillTotal = 2,
            Lines = [Bill(20, 3, 1m / 3m), Bill(10, 3, 1m / 3m), Bill(30, 0, 0)], Rules = [Gift("cent", 30, 1, 20, 10)] };
        var result = _calculator.Calculate(request, [a, b, Physical(30, 1, 3)], [new(3, 1, 1, .01m, PurchaseReceiptGiftValuationSource.Manual)]);
        Assert.True(result.CanApply, string.Join(";", result.Errors));
        Assert.Equal(.01m, result.Lines.Single(x => x.StockDocumentLineId == 20).GiftBurden);
        Assert.Equal(0m, result.Lines.Single(x => x.StockDocumentLineId == 10).GiftBurden);
        Assert.Equal(2m, result.SystemTotal);
        Assert.Contains(result.Residuals, x => x.StockDocumentLineId == 20 && x.Amount == .01m);
        Assert.NotEqual(result.Lines[1].FinalAmountBeforeVat, decimal.Round(result.Lines[1].EffectiveUnitPriceBeforeVat * 3, 2));
    }
}
