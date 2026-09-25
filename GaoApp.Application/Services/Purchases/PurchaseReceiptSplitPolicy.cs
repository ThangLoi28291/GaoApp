namespace GaoApp.Application.Services.Purchases;

public sealed record PurchaseReceiptSplitSourceLine(
    int LineId,
    decimal BaseQuantity,
    decimal Factor,
    decimal LineAmountAfterVat,
    decimal FreightAllocation);

public sealed record PurchaseReceiptSplitAllocationInput(
    int SourceLineId,
    decimal BaseQuantity);

public sealed record PurchaseReceiptSplitTargetInput(
    int TargetIndex,
    int SupplierId,
    bool? IsMerchandisePaid,
    string? MerchandisePayeeName,
    bool PaymentStateConfirmed,
    IReadOnlyList<PurchaseReceiptSplitAllocationInput> Allocations);

public sealed class PurchaseReceiptSplitPolicyInput
{
    public int SourceSupplierId { get; init; }
    public bool SourceIsMerchandisePaid { get; init; }
    public string? SourceMerchandisePayeeName { get; init; }
    public bool HasFreight { get; init; }
    public bool CapitalizeFreightInInventoryCost { get; init; }
    public decimal FreightTotal { get; init; }
    public IReadOnlyList<PurchaseReceiptSplitSourceLine> Lines { get; init; } = [];
    public IReadOnlyList<PurchaseReceiptSplitTargetInput> Targets { get; init; } = [];
}

public sealed record PurchaseReceiptSplitPlannedLine(
    int SourceLineId,
    decimal BaseQuantity,
    decimal Quantity,
    decimal FreightAllocation);

public sealed record PurchaseReceiptSplitPlannedTarget(
    int TargetIndex,
    int SupplierId,
    bool IsMerchandisePaid,
    string? MerchandisePayeeName,
    decimal FreightTotal,
    IReadOnlyList<PurchaseReceiptSplitPlannedLine> Lines);

public sealed record PurchaseReceiptSplitPlan(
    IReadOnlyList<PurchaseReceiptSplitPlannedTarget> Targets,
    bool UsedD8Fallback);

public static class PurchaseReceiptSplitPolicy
{
    public static PurchaseReceiptSplitPlan Build(PurchaseReceiptSplitPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Lines.Count == 0)
            throw new InvalidOperationException("Phiếu nhập chưa có dòng hợp lệ để tách.");
        if (input.Targets.Count < 2)
            throw new InvalidOperationException("Tách phiếu nhập cần ít nhất 2 phiếu kết quả.");

        var sourceLines = input.Lines.OrderBy(x => x.LineId).ToArray();
        if (sourceLines.Any(x => x.LineId <= 0 || x.BaseQuantity <= 0m || x.Factor <= 0m) ||
            sourceLines.Select(x => x.LineId).Distinct().Count() != sourceLines.Length)
            throw new InvalidOperationException("Dữ liệu dòng nguồn không hợp lệ.");

        var targets = input.Targets.OrderBy(x => x.TargetIndex).ToArray();
        if (targets.Select(x => x.TargetIndex).Distinct().Count() != targets.Length ||
            !targets.Select(x => x.TargetIndex).SequenceEqual(Enumerable.Range(1, targets.Length)))
            throw new InvalidOperationException("Thứ tự phiếu kết quả không hợp lệ.");

        var sourceById = sourceLines.ToDictionary(x => x.LineId);
        var plannedLines = new Dictionary<int, List<PurchaseReceiptSplitPlannedLine>>();
        var resolvedPayments = new Dictionary<int, (bool Paid, string? Payee)>();
        foreach (var target in targets)
        {
            if (target.SupplierId <= 0)
                throw new InvalidOperationException($"Phiếu {target.TargetIndex} chưa chọn nhà cung cấp.");
            if (target.Allocations.GroupBy(x => x.SourceLineId).Any(x => x.Count() > 1))
                throw new InvalidOperationException("Một dòng nguồn bị lặp trong cùng phiếu kết quả.");

            var lines = new List<PurchaseReceiptSplitPlannedLine>();
            foreach (var allocation in target.Allocations.OrderBy(x => x.SourceLineId))
            {
                if (!sourceById.TryGetValue(allocation.SourceLineId, out var source))
                    throw new InvalidOperationException("Phân bổ chứa dòng không thuộc phiếu nguồn.");
                var baseQuantity = PurchasePricingPolicy.RoundQuantity(allocation.BaseQuantity);
                if (baseQuantity != allocation.BaseQuantity || baseQuantity < 0m)
                    throw new InvalidOperationException("Số lượng phân bổ vượt độ chính xác cho phép.");
                if (baseQuantity == 0m) continue;

                var quantity = PurchasePricingPolicy.RoundQuantity(baseQuantity / source.Factor);
                if (quantity <= 0m ||
                    PurchasePricingPolicy.RoundQuantity(quantity * source.Factor) != baseQuantity)
                    throw new InvalidOperationException(
                        "Số lượng phân bổ không thể biểu diễn chính xác theo đơn vị nhập.");
                lines.Add(new PurchaseReceiptSplitPlannedLine(
                    source.LineId, baseQuantity, quantity, 0m));
            }

            if (lines.Count == 0)
                throw new InvalidOperationException(
                    $"Phiếu {target.TargetIndex} phải có ít nhất một dòng phân bổ lớn hơn 0.");
            plannedLines[target.TargetIndex] = lines;
            resolvedPayments[target.TargetIndex] = ResolvePayment(input, target);
        }

        foreach (var source in sourceLines)
        {
            var allocated = PurchasePricingPolicy.RoundQuantity(
                plannedLines.Values.SelectMany(x => x)
                    .Where(x => x.SourceLineId == source.LineId)
                    .Sum(x => x.BaseQuantity));
            if (allocated != source.BaseQuantity)
                throw new InvalidOperationException(
                    $"Phân bổ dòng {source.LineId} không bảo toàn BaseQuantity.");
        }

        var freightByTarget = targets.ToDictionary(x => x.TargetIndex, _ => 0m);
        var freightByTargetLine = new Dictionary<(int Target, int Line), decimal>();
        var usedD8Fallback = false;
        if (input.HasFreight)
        {
            if (input.FreightTotal <= 0m)
                throw new InvalidOperationException("Tổng phí vận chuyển phải lớn hơn 0.");

            var canonicalLineFreight = ResolveCanonicalLineFreight(input, sourceLines, out usedD8Fallback);
            if (usedD8Fallback)
            {
                var targetWeights = targets.Select(target => (
                    target.TargetIndex,
                    plannedLines[target.TargetIndex].Sum(x => x.BaseQuantity))).ToArray();
                foreach (var pair in PurchasePricingPolicy.AllocateByWeight(
                             input.FreightTotal, targetWeights))
                    freightByTarget[pair.Key] = pair.Value;
            }
            else
            {
                foreach (var source in sourceLines)
                {
                    var allocationWeights = targets
                        .Select(target => (
                            target.TargetIndex,
                            plannedLines[target.TargetIndex]
                                .Where(x => x.SourceLineId == source.LineId)
                                .Sum(x => x.BaseQuantity)))
                        .Where(x => x.Item2 > 0m)
                        .ToArray();
                    foreach (var pair in PurchasePricingPolicy.AllocateByWeight(
                                 canonicalLineFreight[source.LineId], allocationWeights))
                    {
                        freightByTarget[pair.Key] += pair.Value;
                        if (input.CapitalizeFreightInInventoryCost)
                            freightByTargetLine[(pair.Key, source.LineId)] = pair.Value;
                    }
                }
            }
        }

        var result = targets.Select(target =>
        {
            var payment = resolvedPayments[target.TargetIndex];
            var lines = plannedLines[target.TargetIndex]
                .Select(line => line with
                {
                    FreightAllocation = input.CapitalizeFreightInInventoryCost
                        ? freightByTargetLine.GetValueOrDefault((target.TargetIndex, line.SourceLineId))
                        : 0m
                })
                .ToArray();
            return new PurchaseReceiptSplitPlannedTarget(
                target.TargetIndex,
                target.SupplierId,
                payment.Paid,
                payment.Payee,
                PurchasePricingPolicy.RoundMoney(freightByTarget[target.TargetIndex]),
                lines);
        }).ToArray();

        if (PurchasePricingPolicy.RoundMoney(result.Sum(x => x.FreightTotal)) !=
            PurchasePricingPolicy.RoundMoney(input.HasFreight ? input.FreightTotal : 0m))
            throw new InvalidOperationException("Phân bổ phí vận chuyển không bảo toàn tổng phí.");
        if (usedD8Fallback && result.SelectMany(x => x.Lines).Any(x => x.FreightAllocation != 0m))
            throw new InvalidOperationException("D8 không được phân bổ phí vào dòng.");

        return new PurchaseReceiptSplitPlan(result, usedD8Fallback);
    }

    private static IReadOnlyDictionary<int, decimal> ResolveCanonicalLineFreight(
        PurchaseReceiptSplitPolicyInput input,
        IReadOnlyList<PurchaseReceiptSplitSourceLine> lines,
        out bool usedD8Fallback)
    {
        usedD8Fallback = false;
        if (input.CapitalizeFreightInInventoryCost &&
            PurchasePricingPolicy.RoundMoney(lines.Sum(x => x.FreightAllocation)) ==
            PurchasePricingPolicy.RoundMoney(input.FreightTotal) &&
            lines.All(x => x.FreightAllocation >= 0m))
            return lines.ToDictionary(x => x.LineId, x => PurchasePricingPolicy.RoundMoney(x.FreightAllocation));

        if (lines.Sum(x => x.LineAmountAfterVat) > 0m)
            return PurchasePricingPolicy.AllocateFreight(
                input.FreightTotal,
                lines.Select(x => (x.LineId, x.LineAmountAfterVat)).ToArray());

        if (!input.CapitalizeFreightInInventoryCost)
        {
            usedD8Fallback = true;
            return lines.ToDictionary(x => x.LineId, _ => 0m);
        }

        throw new InvalidOperationException("Không thể xác định cơ sở phân bổ phí vận chuyển.");
    }

    private static (bool Paid, string? Payee) ResolvePayment(
        PurchaseReceiptSplitPolicyInput input,
        PurchaseReceiptSplitTargetInput target)
    {
        if (target.SupplierId == input.SourceSupplierId)
        {
            return (
                target.IsMerchandisePaid ?? input.SourceIsMerchandisePaid,
                string.IsNullOrWhiteSpace(target.MerchandisePayeeName)
                    ? input.SourceMerchandisePayeeName
                    : target.MerchandisePayeeName.Trim());
        }

        if (!target.PaymentStateConfirmed || !target.IsMerchandisePaid.HasValue)
            throw new InvalidOperationException(
                $"Phiếu {target.TargetIndex} đổi nhà cung cấp và cần xác nhận thanh toán.");
        var payee = string.IsNullOrWhiteSpace(target.MerchandisePayeeName)
            ? null
            : target.MerchandisePayeeName.Trim();
        if (target.IsMerchandisePaid.Value && payee is null)
            throw new InvalidOperationException(
                $"Phiếu {target.TargetIndex} đã thanh toán và cần xác nhận người nhận tiền.");
        return (target.IsMerchandisePaid.Value, payee);
    }
}
