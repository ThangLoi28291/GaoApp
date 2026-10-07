namespace GaoApp.Domain.Delivery;

public static class DeliveryQuantityPolicy
{
    public static DeliveryQuantityPlan Evaluate(DeliveryQuantityLine line)
    {
        DeliveryValues.Require(line.LineId > 0, "LINE_INVALID", "Thiếu mã dòng hàng.");
        foreach (var value in new[] { line.Ordered, line.Picked, line.Dispatched, line.Delivered,
            line.ReportedReturn, line.AcceptedReturn, line.Damaged, line.Lost })
            DeliveryValues.Quantity(value, "Lượng hàng/đơn vị");
        _ = DeliveryValues.CostAmount(0, line.BaseUnitCost);
        DeliveryValues.Require(line.Ordered > 0 && line.BaseMultiplier > 0 && line.BaseMultiplier < 1000000000000m &&
            decimal.Round(line.BaseMultiplier, 6) == line.BaseMultiplier,
            "UNIT_INVALID", "Lượng đặt phải > 0; hệ số đơn vị phải khớp precision (18,6).");
        DeliveryValues.Require(line.Picked <= line.Ordered && line.Dispatched <= line.Picked && line.AcceptedReturn <= line.ReportedReturn,
            "QUANTITY_EXCEEDS_SOURCE", "Lượng soạn/xuất/nhận về vượt nguồn.");
        var accounted = line.Delivered + line.ReportedReturn + line.Damaged + line.Lost;
        DeliveryValues.Require(accounted <= line.Dispatched, "OUTCOME_EXCEEDS_DISPATCH", "Tổng giao/mang về/hư/mất vượt lượng đã bàn giao.");

        // Validate converted values BEFORE allowing future SQL decimal(18,4) writes.
        _ = ToBase(line.Ordered, line.BaseMultiplier);
        _ = ToBase(line.Picked, line.BaseMultiplier);
        var dispatchedBase = ToBase(line.Dispatched, line.BaseMultiplier);
        var deliveredBase = ToBase(line.Delivered, line.BaseMultiplier);
        var returnedBase = ToBase(line.AcceptedReturn, line.BaseMultiplier);
        var exceptionBase = ToBase(line.Damaged + line.Lost, line.BaseMultiplier);
        var pending = line.Dispatched - accounted;
        var pendingReceipt = line.ReportedReturn - line.AcceptedReturn;
        var saleCost = DeliveryValues.CostAmount(deliveredBase, line.BaseUnitCost);
        var exceptionCost = DeliveryValues.CostAmount(exceptionBase, line.BaseUnitCost);
        return new(line.LineId, dispatchedBase, deliveredBase, returnedBase,
            -dispatchedBase + returnedBase, pending, pendingReceipt, line.Ordered - line.Delivered,
            saleCost, exceptionCost, pending == 0 && pendingReceipt == 0 &&
                (exceptionBase == 0 || line.ExceptionsApproved));
    }

    // Required for mixed FIFO layers: do not replace original layer costs with a current/average price.
    // Allocation IDs and outcome quantities must be loaded and locked by the server in D05/D08.
    public static DeliveryFragmentCostPlan EvaluateFragments(DeliveryOrigin origin, DeliveryQuantityLine line,
        IReadOnlyList<DeliveryCostFragment> fragments)
    {
        DeliveryValues.Origin(origin);
        var quantityPlan = Evaluate(line);
        DeliveryValues.Require(fragments.Count > 0 && fragments.All(x => x.AllocationId > 0) &&
            fragments.Select(x => x.AllocationId).Distinct().Count() == fragments.Count,
            "COST_FRAGMENTS_INVALID", "Thiếu hoặc trùng fragment giá vốn nguồn.");
        foreach (var fragment in fragments)
        {
            DeliveryValues.Require(fragment.WarehouseId == origin.SourceWarehouseId && fragment.LegalEntityId == origin.SourceLegalEntityId,
                "COST_SOURCE_MISMATCH", "Fragment vốn không thuộc kho/chủ thể xuất gốc.");
            foreach (var value in new[] { fragment.DispatchedBase, fragment.DeliveredBase, fragment.AcceptedReturnBase, fragment.DamagedBase, fragment.LostBase })
                DeliveryValues.Quantity(value, "Lượng fragment vốn");
            DeliveryValues.Require(fragment.DeliveredBase + fragment.AcceptedReturnBase + fragment.DamagedBase + fragment.LostBase <= fragment.DispatchedBase,
                "COST_FRAGMENT_OVERDRAWN", "Fragment giao/nhận về/hư/mất vượt nguồn xuất.");
            _ = DeliveryValues.CostAmount(fragment.DispatchedBase, fragment.UnitCost);
        }
        DeliveryValues.Require(fragments.Sum(x => x.DispatchedBase) == quantityPlan.DispatchedBase &&
            fragments.Sum(x => x.DeliveredBase) == quantityPlan.DeliveredBase &&
            fragments.Sum(x => x.AcceptedReturnBase) == quantityPlan.AcceptedReturnBase &&
            fragments.Sum(x => x.DamagedBase) == ToBase(line.Damaged, line.BaseMultiplier) &&
            fragments.Sum(x => x.LostBase) == ToBase(line.Lost, line.BaseMultiplier),
            "COST_QUANTITY_MISMATCH", "Lượng fragment vốn không khớp kết quả giao/nhận.");
        return new(fragments.Sum(x => DeliveryValues.CostAmount(x.DeliveredBase, x.UnitCost)),
            fragments.Sum(x => DeliveryValues.CostAmount(x.AcceptedReturnBase, x.UnitCost)),
            fragments.Sum(x => DeliveryValues.CostAmount(x.DamagedBase + x.LostBase, x.UnitCost)));
    }

    private static decimal ToBase(decimal quantity, decimal multiplier)
    {
        var result = quantity * multiplier;
        DeliveryValues.Quantity(result, "Lượng quy đổi");
        return result;
    }
}
