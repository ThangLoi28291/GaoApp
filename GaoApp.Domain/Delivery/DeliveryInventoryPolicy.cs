namespace GaoApp.Domain.Delivery;

public static class DeliveryInventoryPolicy
{
    public static bool CanCancelBeforeHandover(DeliveryState state, bool hasStockEvidence)
        => !hasStockEvidence && state is DeliveryState.Created or DeliveryState.Picking or DeliveryState.AwaitingApproval or DeliveryState.ReadyForHandover;
    public static decimal HeldBaseQuantity(bool active, decimal? reported, decimal multiplier)
        => active && reported is > 0 ? DeliveryPickingPolicy.ToBase(reported.Value, multiplier) : 0;
    public static void RequireReceivedBudget(decimal dispatched, decimal received, decimal additional)
    {
        DeliveryValues.Quantity(dispatched, "dispatched"); DeliveryValues.Quantity(received, "received");
        DeliveryValues.Quantity(additional, "additional");
        DeliveryValues.Require(additional > 0 && received + additional <= dispatched,
            "RETURN_BUDGET_EXCEEDED", "Hàng nhận trả vượt lượng đã xuất chưa nhận lại.");
    }
}
