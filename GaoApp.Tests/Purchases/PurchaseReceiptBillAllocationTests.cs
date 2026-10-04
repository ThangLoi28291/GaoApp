using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptBillAllocationTests
{
    private readonly PurchaseReceiptPricingAllocationCalculator calculator=new();
    private static PurchaseReceiptPricingPhysicalLine Physical(int id,decimal qty,int variant=1)=>PurchaseReceiptPricingAllocationCalculatorTests.Physical(id,qty,variant);
    private static PurchaseReceiptBillLineInput Row(string key,int no,decimal qty,decimal price,int variant=1,int unit=1,bool gift=false)=>new()
    {BillLineKey=key,LineNo=no,ProductVariantId=variant,BillUnitId=unit,BillQuantity=qty,BillUnitPriceBeforeVat=price,IsGift=gift};
    private static PurchaseReceiptPricingRuleInput Gift(string key,string source,decimal participating,int target,decimal qty,string? giftLine=null)=>new()
    {RuleKey=key,Type=PurchaseReceiptPricingRuleType.Gift,GiftMode=PurchaseReceiptPricingGiftMode.SameSku,GiftLineId=target,GiftUnitId=1,GiftQuantity=qty,GiftBillLineKey=giftLine,
        BillSources=[new(){BillLineKey=source,Quantity=participating}]};
    private static PurchaseReceiptPricingRuleInput Fixed(decimal amount, params (string Key, decimal Quantity)[] sources) => new()
    {
        RuleKey = "fixed", Type = PurchaseReceiptPricingRuleType.FixedAmountDiscount, DiscountAmount = amount,
        BillSources = sources.Select(x => new PurchaseReceiptBillRuleSourceInput { BillLineKey = x.Key, Quantity = x.Quantity }).ToList()
    };
    [Fact]
    public void Fixed_amount_uses_only_participating_quantities_and_preserves_exact_discount()
    {
        var request = new PurchaseReceiptPricingAllocationRequest
        {
            ActualBillTotal = 2920, BillLines = [Row("a", 1, 10, 100), Row("b", 2, 10, 200, variant: 2)],
            Rules = [Fixed(80, ("a", 2), ("b", 1))]
        };
        var result = calculator.Calculate(request, [Physical(1, 10), Physical(2, 10, 2)]);
        Assert.True(result.CanApply, string.Join(";", result.Errors));
        Assert.Equal(2920, result.SystemTotal); Assert.Equal(80, result.Rules.Single().Amount);
        Assert.Equal(960, result.Lines[0].FinalAmountBeforeVat); Assert.Equal(1960, result.Lines[1].FinalAmountBeforeVat);
        Assert.All(result.BillSources, x => Assert.Equal(40, x.Amount));
    }
    [Fact]
    public void Fixed_amount_cent_residual_is_deterministic_and_agrees_with_each_bill_baseline()
    {
        var request = new PurchaseReceiptPricingAllocationRequest
        {
            ActualBillTotal = 1.99m, BillLines = [Row("a", 1, 1, 1), Row("b", 2, 1, 1, variant: 2)],
            Rules = [Fixed(.01m, ("b", 1), ("a", 1))]
        };
        var result = calculator.Calculate(request, [Physical(1, 1), Physical(2, 1, 2)]);
        Assert.True(result.CanApply, string.Join(";", result.Errors));
        Assert.Equal(.99m, result.Lines[0].FinalAmountBeforeVat); Assert.Equal(1m, result.Lines[1].FinalAmountBeforeVat);
        Assert.Equal(.01m, result.BillSources.Single(x => x.BillLineKey == "a").Amount);
        Assert.All(result.BillSources, x => Assert.Equal(1m, x.BaselineAmount + x.Amount));
        request.BillLines.Reverse(); request.Rules[0].BillSources.Reverse();
        var reversed = calculator.Calculate(request, [Physical(2, 1, 2), Physical(1, 1)]);
        Assert.True(reversed.CanApply, string.Join(";", reversed.Errors));
        Assert.Equal(result.Lines.Select(x => x.FinalAmountBeforeVat), reversed.Lines.Select(x => x.FinalAmountBeforeVat));
    }
    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(.001)][InlineData(100)][InlineData(101)]
    public void Invalid_fixed_amount_is_rejected(decimal amount)
    {
        var result = calculator.Calculate(new() { ActualBillTotal = 900, BillLines = [Row("a", 1, 10, 100)],
            Rules = [Fixed(amount, ("a", 1))] }, [Physical(1, 10)]);
        Assert.False(result.CanApply); Assert.Contains(result.Errors, x => x.Contains("tiền giảm"));
    }
    [Fact]
    public void Fixed_and_percentage_discounts_cannot_overlap_on_the_same_bill_row()
    {
        var result = calculator.Calculate(new() { ActualBillTotal = 900, BillLines = [Row("a", 1, 10, 100)],
            Rules = [Fixed(10, ("a", 1)), new() { RuleKey = "percent", Type = PurchaseReceiptPricingRuleType.PercentageDiscount,
                DiscountPercent = 10, BillSources = [new() { BillLineKey = "a", Quantity = 2 }] }] }, [Physical(1, 10)]);
        Assert.False(result.CanApply); Assert.Contains(result.Errors, x => x.Contains("đã có một chương trình giảm giá"));
    }
    [Fact]
    public void Fixed_amount_then_global_percent_and_same_sku_gift_preserve_total()
    {
        var request = new PurchaseReceiptPricingAllocationRequest
        {
            ActualBillTotal = 453600, GlobalDiscountPercent = 10,
            BillLines = [Row("a", 1, 180, 3110), Row("g", 2, 20, 0, gift: true)],
            Rules = [Fixed(55800, ("a", 180)), Gift("gift", "a", 180, 1, 20, "g")]
        };
        var result = calculator.Calculate(request, [Physical(1, 200)]);
        Assert.True(result.CanApply, string.Join(";", result.Errors));
        Assert.Equal(453600, result.SystemTotal); Assert.Equal(2268, result.Lines.Single().EffectiveUnitPriceBeforeVat);
        Assert.Equal(55800, result.Rules.Single(x => x.RuleKey == "fixed").Amount);
        Assert.Equal(20, result.Quantities.Single().GiftBaseQuantity);
    }
    [Fact]
    public void Repeated_bill_and_receiving_lines_reconcile_by_sku_and_blend_same_sku_gift_once()
    {
        var request=new PurchaseReceiptPricingAllocationRequest{ActualBillTotal=559800,
            BillLines=[Row("a",1,100,3110),Row("b",2,80,3110),Row("gift",3,20,0,gift:true)],
            Rules=[Gift("g","a",60,1,20,"gift")]};
        var result=calculator.Calculate(request,[Physical(1,120),Physical(2,80)]);
        Assert.True(result.CanApply,string.Join(";",result.Errors));Assert.Equal(559800,result.SystemTotal);
        Assert.All(result.Lines,x=>Assert.Equal(2799,x.EffectiveUnitPriceBeforeVat));
        Assert.Equal(20,result.Quantities.Single().GiftBaseQuantity);
        Assert.Equal(60*3110,result.BillSources.Single().BaselineAmount);
        Assert.All(result.Lines,x=>Assert.Equal(x.PurchasedAmount+x.GiftAmount,x.FinalAmountBeforeVat));
    }
    [Fact]
    public void Partial_discount_only_reduces_selected_quantity_and_unit_conversion_reconciles_multiple_receiving_lines()
    {
        var request=new PurchaseReceiptPricingAllocationRequest{ActualBillTotal=4550,
            BillLines=[Row("carton",1,1,4800,unit:2)],Rules=[new(){RuleKey="d",Type=PurchaseReceiptPricingRuleType.PercentageDiscount,DiscountPercent=10,
                BillSources=[new(){BillLineKey="carton",Quantity=25m/48m}]}]};
        var result=calculator.Calculate(request,[Physical(1,20),Physical(2,28)]);
        Assert.True(result.CanApply,string.Join(";",result.Errors));Assert.Equal(250,result.Rules.Single().Amount);Assert.Equal(4550,result.SystemTotal);
        Assert.Equal(48,result.Quantities.Single().PurchasedBaseQuantity);
    }
    [Fact]
    public void Gift_description_and_partial_gift_bill_line_can_coexist_without_double_counting()
    {
        var request=new PurchaseReceiptPricingAllocationRequest{ActualBillTotal=1000,
            BillLines=[Row("a",1,10,100),Row("gift",2,2,0,gift:true)],
            Rules=[Gift("g1","a",4,1,1,"gift"),Gift("g2","a",6,1,1,"gift"),Gift("g3","a",10,1,1)]};
        var result=calculator.Calculate(request,[Physical(1,13)]);
        Assert.True(result.CanApply,string.Join(";",result.Errors));Assert.Equal(3,result.Quantities.Single().GiftBaseQuantity);
        var expected=result.Lines.Single().FinalAmountBeforeVat;request.Rules.Reverse();
        Assert.Equal(expected,calculator.Calculate(request,[Physical(1,13)]).Lines.Single().FinalAmountBeforeVat);
    }
    [Fact]
    public void Different_sku_gift_keeps_fixed_value_and_burden_uses_only_participating_amounts()
    {
        var rule=Gift("g","a",2,3,1);rule.GiftMode=PurchaseReceiptPricingGiftMode.DifferentSku;
        rule.BillSources.Add(new(){BillLineKey="b",Quantity=1});
        var request=new PurchaseReceiptPricingAllocationRequest{ActualBillTotal=2000,BillLines=[Row("a",1,10,100),Row("b",2,10,100,variant:2)],Rules=[rule]};
        var result=calculator.Calculate(request,[Physical(1,10),Physical(2,10,2),Physical(3,1,3)],
            [new(3,1,1,90,PurchaseReceiptGiftValuationSource.Manual)]);
        Assert.True(result.CanApply,string.Join(";",result.Errors));Assert.Equal(940,result.Lines[0].FinalAmountBeforeVat);
        Assert.Equal(970,result.Lines[1].FinalAmountBeforeVat);Assert.Equal(90,result.Lines[2].FinalAmountBeforeVat);
    }
    [Theory]
    [InlineData(9,"nhiều hơn bill 1")][InlineData(11,"thiếu 1")]
    public void Quantity_mismatch_identifies_which_side_is_short(decimal quantity,string text)
    {
        var result=calculator.Calculate(new(){ActualBillTotal=1000,BillLines=[Row("a",1,quantity,100)]},[Physical(1,10)]);
        Assert.False(result.CanApply);Assert.Contains(result.Errors,x=>x.Contains(text));
    }
    [Fact]
    public void Unknown_product_excess_participation_and_unassigned_gift_are_rejected()
    {
        Assert.False(calculator.Calculate(new(){ActualBillTotal=100,BillLines=[Row("a",1,1,100,variant:99)]},[Physical(1,1)]).CanApply);
        Assert.False(calculator.Calculate(new(){ActualBillTotal=100,BillLines=[Row("a",1,1,100)],Rules=[Gift("g","a",2,1,1)]},[Physical(1,2)]).CanApply);
        var unassigned=calculator.Calculate(new(){ActualBillTotal=100,BillLines=[Row("a",1,1,100),Row("g",2,1,0,gift:true)]},[Physical(1,2)]);
        Assert.Contains(unassigned.Errors,x=>x.Contains("chưa khớp dòng quà"));
    }

    [Fact]
    public void Small_participating_quantity_preserves_decimal_price_without_rounding_virtual_segments_to_zero()
    {
        var request = new PurchaseReceiptPricingAllocationRequest
        {
            ActualBillTotal = 999.98m,
            BillLines = [Row("a", 1, 1, 1000)],
            Rules = [new() { RuleKey = "d", Type = PurchaseReceiptPricingRuleType.PercentageDiscount, DiscountPercent = 20,
                BillSources = [new() { BillLineKey = "a", Quantity = .0001m }] }]
        };
        var result = calculator.Calculate(request, [Physical(1, 1)]);
        Assert.True(result.CanApply, string.Join(";", result.Errors));
        Assert.Equal(.02m, result.Rules.Single().Amount);
        Assert.Equal(999.98m, result.SystemTotal);
        Assert.Equal(1m, result.Quantities.Single().PurchasedBaseQuantity);
    }

    [Fact]
    public void Price_is_visible_before_actual_bill_total_is_entered_but_apply_remains_blocked()
    {
        var request = new PurchaseReceiptPricingAllocationRequest
        {
            BillLines = [Row("a", 1, 180, 3110), Row("g", 2, 20, 0, gift: true)],
            Rules = [Gift("gift", "a", 180, 1, 20, "g")]
        };
        var result = calculator.Calculate(request, [Physical(1, 200)]);
        Assert.False(result.CanApply);
        Assert.Equal(2799m, result.Lines.Single().EffectiveUnitPriceBeforeVat);
        Assert.Contains(result.Errors, x => x.Contains("Nhập tiền hàng thực trả"));
    }

    [Fact]
    public void Invalid_physical_quantity_and_null_bill_source_key_are_rejected_without_exception()
    {
        var request = new PurchaseReceiptPricingAllocationRequest
        {
            ActualBillTotal = 100, BillLines = [Row("a", 1, 1, 100)],
            Rules = [Gift("g", "a", 1, 1, 1)]
        };
        Assert.False(calculator.Calculate(request, [Physical(1, 0), Physical(2, 1)]).CanApply);
        request.Rules[0].BillSources[0].BillLineKey = null!;
        Assert.False(calculator.Calculate(request, [Physical(1, 2)]).CanApply);
    }

    [Fact]
    public void Oversized_participating_quantity_layout_is_rejected_before_financial_allocation()
    {
        var request = new PurchaseReceiptPricingAllocationRequest
        {
            ActualBillTotal = 200000,
            BillLines = Enumerable.Range(1, 200).Select(i => Row($"row-{i}", i, 10, 100)).ToList(),
            Rules = Enumerable.Range(1, 100).Select(i => new PurchaseReceiptPricingRuleInput
            {
                RuleKey = $"gift-{i}", Type = PurchaseReceiptPricingRuleType.Gift,
                GiftMode = PurchaseReceiptPricingGiftMode.SameSku, GiftLineId = 1, GiftUnitId = 1, GiftQuantity = .1m,
                BillSources = Enumerable.Range(1, 200).Select(n => new PurchaseReceiptBillRuleSourceInput
                    { BillLineKey = $"row-{n}", Quantity = i * .09m }).ToList()
            }).ToList()
        };
        var result = calculator.Calculate(request, [Physical(1, 2010)]);
        Assert.False(result.CanApply);
        Assert.Contains(result.Errors, x => x.Contains("quá nhiều phần số lượng"));
        Assert.Empty(result.Lines);
    }
}
