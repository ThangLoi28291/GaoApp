using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

public sealed partial class PurchaseReceiptPricingAllocationCalculator
{
    public PurchaseReceiptPricingAllocationPreview Calculate(PurchaseReceiptPricingAllocationRequest request,
        IReadOnlyList<PurchaseReceiptPricingPhysicalLine> physicalLines,
        IReadOnlyList<PurchaseReceiptPricingGiftValue>? giftValues = null)
    {
        var result = new PurchaseReceiptPricingAllocationPreview { ActualBillTotal = request.ActualBillTotal };
        try
        {
            if (request.BillLines is not null) CalculateBill(request, physicalLines, giftValues ?? [], result);
            else CalculateCore(request, physicalLines, giftValues ?? [], result);
        }
        catch (OverflowException) { result.Errors.Add("Giá trị tiền hoặc số lượng vượt giới hạn cho phép."); }
        return result;
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private const decimal MaximumMoney = 99_999_999_999_999.99m;

    private static void CalculateCore(PurchaseReceiptPricingAllocationRequest request,
        IReadOnlyList<PurchaseReceiptPricingPhysicalLine> physicalLines,
        IReadOnlyList<PurchaseReceiptPricingGiftValue> giftValues, PurchaseReceiptPricingAllocationPreview result, int maximumLines = 500,
        bool exactPurchasedBaseQuantities = false)
    {
        if (request.Lines is null || request.Rules is null || request.GiftValuations is null ||
            physicalLines.Count < 1 || physicalLines.Count > maximumLines || request.Lines.Count != physicalLines.Count || request.Rules.Count > 100)
        { result.Errors.Add("Danh sách dòng hoặc quy tắc không hợp lệ."); return; }
        if (request.ActualBillTotal <= 0 || request.ActualBillTotal > MaximumMoney || request.ActualBillTotal != Money(request.ActualBillTotal))
            result.Errors.Add("Tiền hàng thực tế phải lớn hơn 0, tối đa hai số lẻ và trong giới hạn cho phép.");
        if (request.GlobalDiscountPercent is < 0 or >= 100)
            result.Errors.Add("Giảm giá toàn bill phải từ 0% đến dưới 100%.");
        if (physicalLines.Select(x => x.StockDocumentLineId).Distinct().Count() != physicalLines.Count ||
            request.Lines.Select(x => x.StockDocumentLineId).Distinct().Count() != request.Lines.Count ||
            !physicalLines.Select(x => x.StockDocumentLineId).ToHashSet().SetEquals(request.Lines.Select(x => x.StockDocumentLineId)))
        { result.Errors.Add("Danh sách dòng phải khớp đầy đủ các dòng thực nhận."); return; }
        var physical = physicalLines.ToDictionary(x => x.StockDocumentLineId);
        var bill = request.Lines.ToDictionary(x => x.StockDocumentLineId);
        var discounts = new Dictionary<int, decimal>();
        var discountedLines = new HashSet<int>();
        var giftQuantities = physical.Keys.ToDictionary(x => x, _ => 0m);
        var ruleGiftBase = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (request.Rules.Any(x => string.IsNullOrWhiteSpace(x.RuleKey) || x.RuleKey.Length > 64) ||
            request.Rules.Select(x => x.RuleKey).Distinct(StringComparer.Ordinal).Count() != request.Rules.Count)
        { result.Errors.Add("Mã quy tắc bị trùng hoặc không hợp lệ."); return; }

        foreach (var line in physicalLines)
        {
            var input = bill[line.StockDocumentLineId];
            var unit = line.Units.SingleOrDefault(x => x.UnitId == input.BillUnitId);
            if (unit is null || unit.Factor <= 0 || line.BaseQuantity <= 0 || line.Quantity <= 0 || line.Factor <= 0 ||
                input.BillQuantity < 0 || input.BillUnitPriceBeforeVat < 0 ||
                input.BillUnitPriceBeforeVat > MaximumMoney || (input.BillQuantity > 0 && input.BillUnitPriceBeforeVat <= 0))
            { result.Errors.Add($"Dòng {line.LineNo}: đơn vị, số lượng hoặc giá bill không hợp lệ."); continue; }
            result.Lines.Add(new()
            {
                StockDocumentLineId = line.StockDocumentLineId, BillFactor = unit.Factor,
                PurchasedBaseQuantity = exactPurchasedBaseQuantities ? checked(input.BillQuantity * unit.Factor)
                    : decimal.Round(checked(input.BillQuantity * unit.Factor), 3, MidpointRounding.AwayFromZero)
            });
        }
        if (result.Errors.Count > 0) return;
        foreach (var rule in request.Rules.OrderBy(x => x.RuleKey, StringComparer.Ordinal))
        {
            if (rule.SourceLineIds is null || rule.SourceLineIds.Count == 0 ||
                rule.SourceLineIds.Distinct().Count() != rule.SourceLineIds.Count ||
                rule.SourceLineIds.Any(x => !bill.ContainsKey(x) || bill[x].BillQuantity <= 0))
            { result.Errors.Add($"Quy tắc {rule.RuleKey}: chọn các dòng mua hợp lệ, không trùng."); continue; }
            if (rule.Type != PurchaseReceiptPricingRuleType.FixedAmountDiscount && rule.DiscountAmount != 0)
            { result.Errors.Add($"Quy tắc {rule.RuleKey}: số tiền giảm chỉ dùng cho chương trình giảm số tiền."); continue; }
            if (rule.Type is PurchaseReceiptPricingRuleType.PercentageDiscount or PurchaseReceiptPricingRuleType.FixedAmountDiscount)
            {
                foreach (var id in rule.SourceLineIds)
                    if (!discountedLines.Add(id)) result.Errors.Add($"Dòng {physical[id].LineNo} đã có một quy tắc giảm giá.");
                if (rule.Type == PurchaseReceiptPricingRuleType.PercentageDiscount)
                {
                    if (rule.DiscountPercent is <= 0 or >= 100)
                        result.Errors.Add($"Quy tắc {rule.RuleKey}: giảm giá phải lớn hơn 0% và dưới 100%.");
                    foreach (var id in rule.SourceLineIds) discounts[id] = rule.DiscountPercent;
                }
                else
                {
                    var participatingAmount = rule.SourceLineIds.Sum(id => checked(bill[id].BillQuantity * bill[id].BillUnitPriceBeforeVat));
                    if (rule.DiscountPercent != 0 || rule.DiscountAmount <= 0 || rule.DiscountAmount > MaximumMoney ||
                        rule.DiscountAmount != Money(rule.DiscountAmount) || rule.DiscountAmount >= participatingAmount)
                        result.Errors.Add($"Quy tắc {rule.RuleKey}: tiền giảm phải lớn hơn 0, tối đa hai số lẻ và nhỏ hơn tiền hàng tham gia.");
                }
            }
            else if (rule.Type == PurchaseReceiptPricingRuleType.Gift)
            {
                if (!rule.GiftLineId.HasValue || !physical.TryGetValue(rule.GiftLineId.Value, out var target) ||
                    !rule.GiftUnitId.HasValue || rule.GiftQuantity <= 0)
                { result.Errors.Add($"Quy tắc {rule.RuleKey}: chọn dòng, đơn vị và số lượng quà hợp lệ."); continue; }
                var unit = target.Units.SingleOrDefault(x => x.UnitId == rule.GiftUnitId);
                if (unit is null || unit.Factor <= 0)
                { result.Errors.Add($"Quy tắc {rule.RuleKey}: đơn vị quà không còn hợp lệ."); continue; }
                var allSameSku = rule.SourceLineIds.All(x => physical[x].ProductVariantId == target.ProductVariantId);
                if (!Enum.IsDefined(rule.GiftMode) || (rule.GiftMode == PurchaseReceiptPricingGiftMode.SameSku) != allSameSku)
                { result.Errors.Add($"Quy tắc {rule.RuleKey}: cách định giá quà phải phù hợp SKU của các dòng nguồn."); continue; }
                var baseQuantity = decimal.Round(checked(rule.GiftQuantity * unit.Factor), 3, MidpointRounding.AwayFromZero);
                if (baseQuantity <= 0) { result.Errors.Add("Số lượng quà sau quy đổi phải lớn hơn 0."); continue; }
                ruleGiftBase.Add(rule.RuleKey, baseQuantity);
                giftQuantities[target.StockDocumentLineId] = checked(giftQuantities[target.StockDocumentLineId] + baseQuantity);
            }
            else result.Errors.Add("Loại quy tắc không hợp lệ.");
        }
        if (result.Errors.Count > 0) return;
        var lines = result.Lines.ToDictionary(x => x.StockDocumentLineId);
        foreach (var line in physicalLines)
        {
            var output = lines[line.StockDocumentLineId];
            output.GiftBaseQuantity = giftQuantities[line.StockDocumentLineId];
            if (output.PurchasedBaseQuantity + output.GiftBaseQuantity != line.BaseQuantity)
                result.Errors.Add($"Dòng {line.LineNo}: số lượng mua + quà sau quy đổi chưa bằng số lượng thực nhận.");
        }
        if (result.Errors.Count > 0) return;

        var rawBaseline = bill.Values.ToDictionary(x => x.StockDocumentLineId,
            x => checked(x.BillQuantity * x.BillUnitPriceBeforeVat * (1m - discounts.GetValueOrDefault(x.StockDocumentLineId) / 100m)));
        var beforeFixedDiscount = rawBaseline.Values.Sum();
        var fixedRules = request.Rules.Where(x => x.Type == PurchaseReceiptPricingRuleType.FixedAmountDiscount).OrderBy(x => x.RuleKey, StringComparer.Ordinal).ToArray();
        foreach (var rule in fixedRules)
        {
            var weights = rule.SourceLineIds.ToDictionary(id => id, id => rawBaseline[id]);
            var amounts = Allocate(rule.DiscountAmount, weights, physical, result, "discount", rule.RuleKey);
            foreach (var deduction in amounts) rawBaseline[deduction.Key] -= deduction.Value;
            if (amounts.Keys.Any(id => rawBaseline[id] <= 0))
            { result.Errors.Add($"Quy tắc {rule.RuleKey}: tiền giảm sau phân bổ làm tiền hàng của một phần dòng tham gia không còn lớn hơn 0. Kiểm tra lại số lượng hoặc tiền giảm."); return; }
            result.Rules.Add(new(rule.RuleKey, rule.DiscountAmount, amounts));
        }
        foreach (var id in rawBaseline.Keys.ToArray()) rawBaseline[id] = checked(rawBaseline[id] * (1m - request.GlobalDiscountPercent / 100m));
        var baselineTotal = fixedRules.Length == 0 ? Money(rawBaseline.Values.Sum())
            : Money(checked((beforeFixedDiscount - fixedRules.Sum(x => x.DiscountAmount)) * (1m - request.GlobalDiscountPercent / 100m)));
        if (baselineTotal <= 0 || baselineTotal > MaximumMoney) { result.Errors.Add("Tổng tiền hàng bill không hợp lệ."); return; }
        var baselines = Allocate(baselineTotal, rawBaseline, physical, result, "baseline", null);
        foreach (var line in result.Lines) line.BaselineAmount = baselines[line.StockDocumentLineId];
        foreach (var rule in request.Rules.Where(x => x.Type == PurchaseReceiptPricingRuleType.PercentageDiscount).OrderBy(x => x.RuleKey, StringComparer.Ordinal))
        {
            var weights = rule.SourceLineIds.ToDictionary(x => x,
                x => checked(bill[x].BillQuantity * bill[x].BillUnitPriceBeforeVat * rule.DiscountPercent / 100m));
            var amount = Money(weights.Values.Sum());
            var amounts = Allocate(amount, weights, physical, result, "discount", rule.RuleKey);
            result.Rules.Add(new(rule.RuleKey, amount, amounts));
        }
        var giftRules = request.Rules.Where(x => x.Type == PurchaseReceiptPricingRuleType.Gift).ToArray();
        var baseGiftValues = new Dictionary<int, decimal>();
        foreach (var group in giftRules.GroupBy(x => physical[x.GiftLineId!.Value].ProductVariantId))
        {
            if (group.Select(x => x.GiftMode).Distinct().Count() != 1)
            { result.Errors.Add("Một SKU quà chỉ có một cách định giá trong bill."); continue; }
            if (group.First().GiftMode == PurchaseReceiptPricingGiftMode.SameSku)
            {
                var sourceIds = group.SelectMany(x => x.SourceLineIds).Distinct().ToArray();
                var purchasedQuantity = sourceIds.Sum(x => lines[x].PurchasedBaseQuantity);
                var giftQuantity = group.Sum(x => ruleGiftBase[x.RuleKey]);
                var value = sourceIds.Sum(x => baselines[x]) / (purchasedQuantity + giftQuantity);
                baseGiftValues.Add(group.Key, value);
                var target = physical[group.First().GiftLineId!.Value];
                result.GiftValues.Add(new(group.Key, target.UnitId, target.Factor, value * target.Factor, PurchaseReceiptGiftValuationSource.SameSkuBlend));
            }
            else
            {
                var values = giftValues.Where(x => x.ProductVariantId == group.Key).ToArray();
                if (values.Length != 1 || values[0].Factor <= 0 || values[0].UnitValueBeforeVat <= 0 || values[0].UnitValueBeforeVat > MaximumMoney ||
                    values[0].Source is not (PurchaseReceiptGiftValuationSource.Manual or PurchaseReceiptGiftValuationSource.LatestConfirmedPurchase))
                { result.Errors.Add($"SKU quà {group.Key}: cần một giá lịch sử hợp lệ hoặc giá thủ công lớn hơn 0."); continue; }
                var value = values[0];
                var target = physical[group.First().GiftLineId!.Value];
                if (!target.Units.Any(x => x.UnitId == value.UnitId && x.Factor == value.Factor))
                { result.Errors.Add("Đơn vị định giá quà không còn hợp lệ."); continue; }
                baseGiftValues.Add(group.Key, value.UnitValueBeforeVat / value.Factor);
                result.GiftValues.Add(value);
            }
        }
        if (result.Errors.Count > 0) return;
        foreach (var rule in giftRules.OrderBy(x => x.RuleKey, StringComparer.Ordinal))
        {
            var target = physical[rule.GiftLineId!.Value];
            var amount = Money(checked(baseGiftValues[target.ProductVariantId] * ruleGiftBase[rule.RuleKey]));
            var weights = rule.SourceLineIds.ToDictionary(x => x, x => baselines[x]);
            if (weights.Values.Sum() <= 0) { result.Errors.Add("Dòng nguồn quà không có giá trị mua hợp lệ."); continue; }
            var burdens = Allocate(amount, weights, physical, result, "gift", rule.RuleKey);
            foreach (var burden in burdens) lines[burden.Key].GiftBurden += burden.Value;
            lines[target.StockDocumentLineId].GiftAmount += amount;
            result.Rules.Add(new(rule.RuleKey, amount, burdens));
        }
        foreach (var line in result.Lines)
        {
            line.PurchasedAmount = line.BaselineAmount - line.GiftBurden;
            if (line.PurchasedBaseQuantity > 0 && line.PurchasedAmount <= 0)
                result.Errors.Add($"Dòng {physical[line.StockDocumentLineId].LineNo}: gánh giá trị quà làm tiền hàng mua không còn lớn hơn 0.");
            line.FinalAmountBeforeVat = line.PurchasedAmount + line.GiftAmount;
            if (line.FinalAmountBeforeVat <= 0 || line.FinalAmountBeforeVat > MaximumMoney)
                result.Errors.Add($"Dòng {physical[line.StockDocumentLineId].LineNo}: giá trị nhập cuối cùng phải lớn hơn 0 và trong giới hạn.");
            line.EffectiveUnitPriceBeforeVat = Money(line.FinalAmountBeforeVat / physical[line.StockDocumentLineId].Quantity);
        }
        result.Lines = result.Lines.OrderBy(x => physical[x.StockDocumentLineId].LineNo).ThenBy(x => x.StockDocumentLineId).ToList();
        result.Rules = result.Rules.OrderBy(x => x.RuleKey, StringComparer.Ordinal).ToList();
        result.SystemTotal = result.Lines.Sum(x => x.FinalAmountBeforeVat);
        result.Difference = result.SystemTotal - request.ActualBillTotal;
        if (result.Difference != 0) result.Errors.Add($"Chênh lệch tiền hàng: {result.Difference:0.00}. Cần đối chiếu bill trước khi Apply.");
    }

    private static Dictionary<int, decimal> Allocate(decimal amount, IReadOnlyDictionary<int, decimal> weights,
        IReadOnlyDictionary<int, PurchaseReceiptPricingPhysicalLine> physical, PurchaseReceiptPricingAllocationPreview result,
        string stage, string? ruleKey)
    {
        var totalWeight = weights.Values.Sum();
        var output = weights.Keys.ToDictionary(x => x, _ => 0m);
        if (amount == 0) return output;
        if (totalWeight <= 0) throw new OverflowException();
        foreach (var item in weights) output[item.Key] = decimal.Floor(checked(amount * item.Value / totalWeight) * 100m) / 100m;
        var residual = amount - output.Values.Sum();
        if (residual != 0)
        {
            var recipient = weights.OrderByDescending(x => x.Value).ThenBy(x => physical[x.Key].LineNo).ThenBy(x => x.Key).First().Key;
            output[recipient] += residual;
            result.Residuals.Add(new(stage, ruleKey, recipient, residual));
        }
        return output;
    }
}
