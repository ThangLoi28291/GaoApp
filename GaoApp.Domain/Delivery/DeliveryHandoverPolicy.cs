namespace GaoApp.Domain.Delivery;

public sealed record DeliveryHandoverQuantity(int LineId, decimal Dispatched, decimal Accepted,
    decimal Delivered, decimal ActualReturned);
public sealed record DeliveryHandoverCharge(decimal Gross, decimal Discount, decimal Net);

public static class DeliveryHandoverPolicy
{
    public static DeliveryState ReconciledState(DeliveryState state)
    {
        DeliveryValues.Require(state is DeliveryState.Delivering or DeliveryState.AwaitingReconciliation or DeliveryState.CancellationPending,
            "HANDOVER_LOCKED", "Đơn chưa xuất phát hoặc đã chốt.");
        return DeliveryState.ReadyToSettle;
    }
    public static void Quantities(IReadOnlyList<DeliveryHandoverQuantity> lines)
    {
        DeliveryValues.Require(lines.Count > 0 && lines.Select(x => x.LineId).Distinct().Count() == lines.Count,
            "LINE_INVALID", "Cần đúng toàn bộ dòng đã xuất.");
        foreach (var line in lines)
        {
            foreach (var value in new[] { line.Dispatched, line.Accepted, line.Delivered, line.ActualReturned })
                DeliveryValues.Quantity(value, "Bàn giao hàng");
            DeliveryValues.Require(line.LineId > 0 && line.Dispatched > 0 && line.Accepted <= line.Dispatched &&
                line.ActualReturned >= line.Accepted && line.Delivered <= line.Dispatched - line.Accepted &&
                line.Delivered + line.ActualReturned == line.Dispatched,
                "GOODS_UNRECONCILED", "Thực giao và tổng thực trả phải đủ hàng đã xuất, gồm cả hàng đã nhận trước.");
        }
    }

    public static DeliveryHandoverCharge Charge(decimal approvedBase, decimal deliveredBase,
        decimal approvedGross, decimal approvedNet)
    {
        DeliveryValues.Quantity(approvedBase, "Xuất nguồn"); DeliveryValues.Quantity(deliveredBase, "Thực giao");
        DeliveryValues.Money(approvedGross, "Giá gốc"); DeliveryValues.Money(approvedNet, "Giá duyệt");
        DeliveryValues.Require(approvedBase > 0 && deliveredBase <= approvedBase && approvedNet <= approvedGross,
            "PRICE_SNAPSHOT_INVALID", "Snapshot giá duyệt không khớp hàng nguồn.");
        var ratio = deliveredBase / approvedBase;
        var gross = DeliveryValues.RoundMoney(approvedGross * ratio);
        var net = DeliveryValues.RoundMoney(approvedNet * ratio);
        return new(gross, gross - net, net);
    }

    public static DeliverySettlementMethod Method(string value) => value switch
    {
        "Cash" => DeliverySettlementMethod.Cash,
        "BankTransfer" => DeliverySettlementMethod.ConfirmedBankTransfer,
        "CustomerCredit" => DeliverySettlementMethod.CustomerCredit,
        _ => throw new DeliveryRuleException("PAYMENT_METHOD_INVALID", "Phương thức chốt không hợp lệ.")
    };

    public static (decimal Quantity, decimal Multiplier, bool UsesBaseUnit) SaleQuantity(decimal baseQuantity, decimal multiplier)
    {
        DeliveryValues.Quantity(baseQuantity, "Thực giao");
        DeliveryValues.Require(multiplier > 0, "QUANTITY_INVALID", "Thiếu hệ số nguồn.");
        var selling = baseQuantity / multiplier;
        return decimal.Round(selling, 4) == selling && selling * multiplier == baseQuantity
            ? (selling, multiplier, false) : (baseQuantity, 1m, true);
    }
}
