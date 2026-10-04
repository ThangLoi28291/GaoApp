using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

public sealed partial class PurchaseReceiptPricingAllocationCalculator
{
    private static decimal Quantity(decimal value) => decimal.Round(value, 3, MidpointRounding.AwayFromZero);

    private static void CalculateBill(PurchaseReceiptPricingAllocationRequest request,
        IReadOnlyList<PurchaseReceiptPricingPhysicalLine> physicalLines,
        IReadOnlyList<PurchaseReceiptPricingGiftValue> giftValues, PurchaseReceiptPricingAllocationPreview result)
    {
        var rows = request.BillLines!;
        if (physicalLines.Count is < 1 or > 500 || rows.Count is < 1 or > 500 || request.Rules is null || request.Rules.Count > 100 ||
            rows.Any(x => x is null) || request.Rules.Any(x => x is null) ||
            physicalLines.Select(x => x.StockDocumentLineId).Distinct().Count() != physicalLines.Count ||
            physicalLines.Any(x => x.Quantity <= 0 || x.BaseQuantity <= 0 || x.Factor <= 0))
        { result.Errors.Add("Nhập các dòng bill hợp lệ trước khi tính giá."); return; }
        var products = physicalLines.GroupBy(x => x.ProductVariantId).ToDictionary(x => x.Key, x => x.OrderBy(p => p.LineNo).ThenBy(p => p.StockDocumentLineId).ToArray());
        if (rows.Any(x => string.IsNullOrWhiteSpace(x.BillLineKey) || x.BillLineKey.Length > 64 || x.LineNo <= 0) ||
            rows.Select(x => x.BillLineKey).Distinct(StringComparer.Ordinal).Count() != rows.Count ||
            rows.Select(x => x.LineNo).Distinct().Count() != rows.Count)
        { result.Errors.Add("Số dòng hoặc mã dòng bill bị trùng, không hợp lệ."); return; }
        var bill = rows.ToDictionary(x => x.BillLineKey, StringComparer.Ordinal);
        var bought = products.Keys.ToDictionary(x => x, _ => 0m);
        var gifted = products.Keys.ToDictionary(x => x, _ => 0m);
        foreach (var row in rows)
        {
            var unit = products.GetValueOrDefault(row.ProductVariantId)?.First().Units.SingleOrDefault(u => u.UnitId == row.BillUnitId);
            if (unit is null || unit.Factor <= 0 || row.BillQuantity <= 0 || row.BillUnitPriceBeforeVat > MaximumMoney ||
                (row.IsGift ? row.BillUnitPriceBeforeVat != 0 : row.BillUnitPriceBeforeVat <= 0))
            { result.Errors.Add($"Dòng bill {row.LineNo}: sản phẩm phải có trên phiếu nhập; đơn vị, số lượng và giá phải hợp lệ."); continue; }
            var baseQuantity = Quantity(checked(row.BillQuantity * unit.Factor));
            if (baseQuantity <= 0) { result.Errors.Add($"Dòng bill {row.LineNo}: số lượng sau quy đổi phải lớn hơn 0."); continue; }
            if (row.IsGift) gifted[row.ProductVariantId] += baseQuantity; else bought[row.ProductVariantId] += baseQuantity;
        }
        if (result.Errors.Count > 0) return;
        var discountRows = new HashSet<string>(StringComparer.Ordinal);
        var allocatedGifts = rows.Where(x => x.IsGift).ToDictionary(x => x.BillLineKey, _ => 0m, StringComparer.Ordinal);
        if (request.Rules.Any(x => string.IsNullOrWhiteSpace(x.RuleKey) || x.RuleKey.Length > 64 || (x.Name?.Length ?? 0) > 200 || (x.ProgramKey?.Length ?? 0) > 64) ||
            request.Rules.Select(x => x.RuleKey).Distinct(StringComparer.Ordinal).Count() != request.Rules.Count)
        { result.Errors.Add("Mã hoặc tên chương trình bị trùng, không hợp lệ."); return; }
        foreach (var rule in request.Rules)
        {
            if (rule.BillSources is null || rule.BillSources.Count == 0 || rule.BillSources.Any(x => x is null) ||
                rule.BillSources.Select(x => x.BillLineKey).Distinct(StringComparer.Ordinal).Count() != rule.BillSources.Count ||
                rule.BillSources.Any(x => string.IsNullOrWhiteSpace(x.BillLineKey) || !bill.TryGetValue(x.BillLineKey, out var row) || row.IsGift || x.Quantity <= 0 || x.Quantity > row.BillQuantity))
            { result.Errors.Add($"Chương trình {rule.RuleKey}: chọn dòng mua và SL tham gia lớn hơn 0, không vượt số lượng dòng bill."); continue; }
            if (rule.Type is PurchaseReceiptPricingRuleType.PercentageDiscount or PurchaseReceiptPricingRuleType.FixedAmountDiscount)
            {
                foreach (var source in rule.BillSources)
                    if (!discountRows.Add(source.BillLineKey)) result.Errors.Add($"Dòng bill {bill[source.BillLineKey].LineNo} đã có một chương trình giảm giá.");
                if (!string.IsNullOrEmpty(rule.GiftBillLineKey)) result.Errors.Add("Chương trình giảm giá không được chứa dòng quà.");
                continue;
            }
            if (rule.Type != PurchaseReceiptPricingRuleType.Gift || !rule.GiftLineId.HasValue ||
                !physicalLines.Any(x => x.StockDocumentLineId == rule.GiftLineId) || !rule.GiftUnitId.HasValue || rule.GiftQuantity <= 0)
            { result.Errors.Add($"Chương trình {rule.RuleKey}: chọn hàng tặng, đơn vị và số lượng hợp lệ."); continue; }
            var target = physicalLines.Single(x => x.StockDocumentLineId == rule.GiftLineId);
            var unit = target.Units.SingleOrDefault(x => x.UnitId == rule.GiftUnitId);
            if (unit is null || unit.Factor <= 0) { result.Errors.Add("Đơn vị quà không còn hợp lệ."); continue; }
            var giftBase = Quantity(checked(rule.GiftQuantity * unit.Factor));
            if (giftBase <= 0) { result.Errors.Add("Số lượng quà sau quy đổi phải lớn hơn 0."); continue; }
            if (!string.IsNullOrEmpty(rule.GiftBillLineKey))
            {
                if (!bill.TryGetValue(rule.GiftBillLineKey, out var gift) || !gift.IsGift || gift.ProductVariantId != target.ProductVariantId)
                { result.Errors.Add("Dòng quà trên bill phải đúng sản phẩm được tặng."); continue; }
                allocatedGifts[gift.BillLineKey] += giftBase;
            }
            else gifted[target.ProductVariantId] += giftBase;
        }
        foreach (var gift in rows.Where(x => x.IsGift))
        {
            var unit = products[gift.ProductVariantId].First().Units.Single(x => x.UnitId == gift.BillUnitId);
            if (allocatedGifts[gift.BillLineKey] != Quantity(gift.BillQuantity * unit.Factor))
                result.Errors.Add($"Dòng quà bill {gift.LineNo}: số lượng gắn chương trình chưa khớp dòng quà; không được cộng quà hai lần.");
        }
        foreach (var group in products)
        {
            var received = group.Value.Sum(x => x.BaseQuantity);
            result.Quantities.Add(new(group.Key, received, bought[group.Key], gifted[group.Key]));
            var difference = received - bought[group.Key] - gifted[group.Key];
            var name = group.Value[0].ProductName;
            if (difference != 0) result.Errors.Add(difference > 0
                ? $"{name}: thực nhận nhiều hơn bill {difference:0.###} đơn vị gốc."
                : $"{name}: thực nhận thiếu {-difference:0.###} đơn vị gốc so với bill.");
        }
        if (result.Errors.Count > 0) return;

        // Split only the calculation inputs at participating-quantity boundaries.
        // The established decimal calculator remains the financial authority;
        // neither these virtual segments nor bill rows change physical receiving.
        var virtualPhysical = new List<PurchaseReceiptPricingPhysicalLine>();
        var virtualBill = new List<PurchaseReceiptPricingLineInput>();
        var metadata = new Dictionary<int, (int Variant, string? Key, decimal End)>();
        var giftTargets = new Dictionary<string, int>(StringComparer.Ordinal);
        int nextId = 1;
        int Add(PurchaseReceiptBillLineInput? row, PurchaseReceiptPricingPhysicalLine physical, int unitId, decimal qty, bool gift, decimal end)
        {
            var unit = physical.Units.Single(x => x.UnitId == unitId);
            var id = nextId++;
            virtualPhysical.Add(new() { StockDocumentLineId = id, LineNo = row?.LineNo ?? rows.Max(x => x.LineNo) + id,
                ProductVariantId = physical.ProductVariantId, ProductId = physical.ProductId, ProductName = physical.ProductName,
                UnitId = unitId, Factor = unit.Factor, Quantity = qty,
                BaseQuantity = gift ? Quantity(qty * unit.Factor) : checked(qty * unit.Factor), Units = physical.Units });
            virtualBill.Add(new() { StockDocumentLineId = id, BillUnitId = unitId, BillQuantity = gift ? 0 : qty,
                BillUnitPriceBeforeVat = gift ? 0 : row!.BillUnitPriceBeforeVat });
            metadata.Add(id, (physical.ProductVariantId, row?.BillLineKey, end));
            return id;
        }
        foreach (var row in rows.OrderBy(x => x.LineNo))
        {
            var physical = products[row.ProductVariantId][0];
            if (row.IsGift) { giftTargets.Add(row.BillLineKey, Add(row, physical, row.BillUnitId, row.BillQuantity, true, 0)); continue; }
            var boundaries = request.Rules.SelectMany(x => x.BillSources).Where(x => x.BillLineKey == row.BillLineKey)
                .Select(x => x.Quantity).Append(row.BillQuantity).Distinct().OrderBy(x => x).ToArray();
            var start = 0m;
            foreach (var end in boundaries)
            {
                Add(row, physical, row.BillUnitId, end - start, false, end);
                start = end;
            }
            if (virtualPhysical.Count > 10_000) { result.Errors.Add("Kế hoạch có quá nhiều phần số lượng tham gia."); return; }
        }
        var purchasedSegments = metadata.Where(x => x.Value.End > 0).ToLookup(x => x.Value.Key!, StringComparer.Ordinal);
        var virtualRules = new List<PurchaseReceiptPricingRuleInput>();
        foreach (var rule in request.Rules.OrderBy(x => x.RuleKey, StringComparer.Ordinal))
        {
            var sourceIds = rule.BillSources.SelectMany(s => purchasedSegments[s.BillLineKey].Where(x => x.Value.End <= s.Quantity).Select(x => x.Key)).ToList();
            int? target = null;
            if (rule.Type == PurchaseReceiptPricingRuleType.Gift)
            {
                if (!string.IsNullOrEmpty(rule.GiftBillLineKey)) target = giftTargets[rule.GiftBillLineKey];
                else target = Add(null, physicalLines.Single(x => x.StockDocumentLineId == rule.GiftLineId), rule.GiftUnitId!.Value, rule.GiftQuantity, true, 0);
            }
            virtualRules.Add(new() { RuleKey = rule.RuleKey, Type = rule.Type, SourceLineIds = sourceIds,
                DiscountPercent = rule.DiscountPercent, DiscountAmount = rule.DiscountAmount, GiftLineId = target, GiftUnitId = rule.GiftUnitId,
                GiftQuantity = rule.GiftQuantity, GiftMode = rule.GiftMode });
        }
        if (virtualPhysical.Count > 10_000) { result.Errors.Add("Kế hoạch có quá nhiều phần số lượng tham gia."); return; }
        var calculated = new PurchaseReceiptPricingAllocationPreview { ActualBillTotal = request.ActualBillTotal };
        CalculateCore(new() { ActualBillTotal = request.ActualBillTotal == 0 ? 1m : request.ActualBillTotal, GlobalDiscountPercent = request.GlobalDiscountPercent,
            Lines = virtualBill, Rules = virtualRules, GiftValuations = request.GiftValuations }, virtualPhysical, giftValues, calculated,
            10_000, exactPurchasedBaseQuantities: true);
        result.Errors.AddRange(calculated.Errors.Where(x => request.ActualBillTotal != 0 || !x.StartsWith("Chênh lệch tiền hàng:", StringComparison.Ordinal)));
        if (request.ActualBillTotal == 0) result.Errors.Add("Nhập tiền hàng thực trả trên bill trước khi lưu hoặc áp dụng giá.");
        if (calculated.SystemTotal <= 0) return;
        var virtualResults = calculated.Lines.ToDictionary(x => x.StockDocumentLineId);
        var physicalMap = physicalLines.ToDictionary(x => x.StockDocumentLineId);
        foreach (var group in products)
        {
            var source = calculated.Lines.Where(x => metadata[x.StockDocumentLineId].Variant == group.Key).ToArray();
            var weights = group.Value.ToDictionary(x => x.StockDocumentLineId, x => x.BaseQuantity);
            Dictionary<int, decimal> Split(decimal amount, string stage) => Allocate(amount, weights, physicalMap, result, stage, null);
            var baseline = Split(source.Sum(x => x.BaselineAmount), "baseline");
            var burden = Split(source.Sum(x => x.GiftBurden), "bill-gift-burden");
            var gift = Split(source.Sum(x => x.GiftAmount), "bill-gift-amount");
            var purchasedQuantity = SplitQuantity(bought[group.Key], weights, physicalMap);
            foreach (var physical in group.Value)
            {
                var id = physical.StockDocumentLineId;
                var finalAmount = baseline[id] - burden[id] + gift[id];
                result.Lines.Add(new() { StockDocumentLineId = id, BillFactor = physical.Factor,
                    PurchasedBaseQuantity = purchasedQuantity[id], GiftBaseQuantity = physical.BaseQuantity - purchasedQuantity[id],
                    BaselineAmount = baseline[id], GiftBurden = burden[id], PurchasedAmount = baseline[id] - burden[id],
                    GiftAmount = gift[id], FinalAmountBeforeVat = finalAmount, EffectiveUnitPriceBeforeVat = Money(finalAmount / physical.Quantity) });
                if (finalAmount <= 0 || baseline[id] - burden[id] < 0) result.Errors.Add($"Dòng thực nhận {physical.LineNo}: giá trị nhập cuối phải lớn hơn 0.");
            }
        }
        foreach (var rule in calculated.Rules)
        {
            var mapped = new Dictionary<int, decimal>();
            foreach (var group in rule.SourceAmounts.GroupBy(x => metadata[x.Key].Key!))
            {
                var ids = group.Select(x => x.Key).ToHashSet();
                result.BillSources.Add(new(rule.RuleKey, group.Key, ids.Sum(id => virtualResults[id].BaselineAmount), group.Sum(x => x.Value),
                    calculated.Residuals.Where(x => x.RuleKey == rule.RuleKey && ids.Contains(x.StockDocumentLineId)).Sum(x => x.Amount)));
                var variant = metadata[group.First().Key].Variant;
                var allocated = Allocate(group.Sum(x => x.Value), products[variant].ToDictionary(x => x.StockDocumentLineId, x => x.BaseQuantity),
                    physicalMap, result, "bill-rule", rule.RuleKey);
                foreach (var value in allocated) mapped[value.Key] = mapped.GetValueOrDefault(value.Key) + value.Value;
            }
            result.Rules.Add(new(rule.RuleKey, rule.Amount, mapped));
        }
        result.GiftValues = calculated.GiftValues;
        result.Lines = result.Lines.OrderBy(x => physicalMap[x.StockDocumentLineId].LineNo).ThenBy(x => x.StockDocumentLineId).ToList();
        result.SystemTotal = result.Lines.Sum(x => x.FinalAmountBeforeVat);
        result.Difference = result.SystemTotal - request.ActualBillTotal;
    }

    private static Dictionary<int, decimal> SplitQuantity(decimal amount, IReadOnlyDictionary<int, decimal> weights,
        IReadOnlyDictionary<int, PurchaseReceiptPricingPhysicalLine> physical)
    {
        var sum = weights.Values.Sum();
        var result = weights.ToDictionary(x => x.Key, x => decimal.Floor(amount * x.Value / sum * 1000m) / 1000m);
        var remaining = amount - result.Values.Sum();
        foreach (var id in weights.OrderByDescending(x => x.Value).ThenBy(x => physical[x.Key].LineNo).ThenBy(x => x.Key).Select(x => x.Key))
        {
            var extra = Math.Min(.001m, remaining); result[id] += extra; remaining -= extra;
            if (remaining == 0) break;
        }
        return result;
    }
}
