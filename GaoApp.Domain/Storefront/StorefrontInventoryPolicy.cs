namespace GaoApp.Domain.Storefront;

public static class StorefrontInventoryPolicy
{
    public static decimal ReserveOwn(decimal onHand, decimal allHeld, decimal requested, bool allowNegative)
    {
        SignedQuantity(onHand);
        StorefrontValues.Quantity(allHeld);
        StorefrontValues.Quantity(requested, positive: true);
        StorefrontValues.Require(allowNegative || onHand - allHeld >= requested,
            "INSUFFICIENT_INVENTORY", "Không đủ tồn khả dụng theo chính sách hiện tại.");
        var result = allHeld + requested;
        StorefrontValues.Quantity(result);
        return result;
    }

    public static decimal ReleaseOwn(decimal allHeld, decimal ownHeld)
    {
        StorefrontValues.Quantity(allHeld);
        StorefrontValues.Quantity(ownHeld);
        StorefrontValues.Require(ownHeld <= allHeld, "HOLD_BUDGET_EXCEEDED", "Không được giải phóng lượng giữ của đơn khác.");
        return allHeld - ownHeld;
    }

    // Preview only: the typed inventory posting service executes the movement
    // under locks and its unique posting key, never a direct balance update.
    public static decimal DispatchAfter(decimal onHand, decimal otherHeld, decimal quantity, bool allowNegative)
    {
        SignedQuantity(onHand);
        StorefrontValues.Quantity(otherHeld);
        StorefrontValues.Quantity(quantity, positive: true);
        StorefrontValues.Require(allowNegative || onHand - otherHeld >= quantity,
            "INSUFFICIENT_INVENTORY", "Hàng đã giữ cho các đơn khác phải được bảo toàn.");
        var after = onHand - quantity;
        SignedQuantity(after);
        return after;
    }

    public static decimal ReceiveReturnBudget(decimal dispatched, decimal alreadyReturned, decimal acceptedReturn)
    {
        StorefrontValues.Quantity(dispatched);
        StorefrontValues.Quantity(alreadyReturned);
        StorefrontValues.Quantity(acceptedReturn, positive: true);
        StorefrontValues.Require(alreadyReturned <= dispatched && acceptedReturn <= dispatched - alreadyReturned,
            "RETURN_BUDGET_EXCEEDED", "Không được nhận trả vượt lượng đã xuất chưa nhận lại.");
        return alreadyReturned + acceptedReturn;
    }

    private static void SignedQuantity(decimal value)
        => StorefrontValues.Require(value > -StorefrontValues.QuantityLimit && value < StorefrontValues.QuantityLimit &&
            decimal.Round(value, 4) == value, "QUANTITY_INVALID", "Số lượng kho không hợp lệ.");
}
