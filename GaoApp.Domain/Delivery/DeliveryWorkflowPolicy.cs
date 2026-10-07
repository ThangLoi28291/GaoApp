namespace GaoApp.Domain.Delivery;

public static class DeliveryWorkflowPolicy
{
    public static DeliveryState Apply(DeliveryState state, DeliveryCommand command, DeliveryWorkflowFacts facts)
    {
        DeliveryValues.Require(Enum.IsDefined(state) && Enum.IsDefined(command), "STATE_INVALID", "Trạng thái/thao tác không hợp lệ.");
        var next = (state, command) switch
        {
            (DeliveryState.Created, DeliveryCommand.StartPicking) => DeliveryState.Picking,
            (DeliveryState.Picking, DeliveryCommand.SubmitPicking) => facts.HasPickingChanges
                ? DeliveryState.AwaitingApproval : DeliveryState.ReadyForHandover,
            (DeliveryState.AwaitingApproval, DeliveryCommand.ApprovePicking) => DeliveryState.ReadyForHandover,
            (DeliveryState.AwaitingApproval or DeliveryState.ReadyForHandover, DeliveryCommand.ReopenPicking) => DeliveryState.Picking,
            (DeliveryState.ReadyForHandover, DeliveryCommand.Handover) => DeliveryState.HandedOver,
            (DeliveryState.HandedOver, DeliveryCommand.Depart) => DeliveryState.Delivering,
            (DeliveryState.Delivering or DeliveryState.AwaitingReconciliation, DeliveryCommand.RecordOutcome) => DeliveryState.AwaitingReconciliation,
            (DeliveryState.AwaitingReconciliation, DeliveryCommand.Reconcile) => DeliveryState.ReadyToSettle,
            (DeliveryState.ReadyToSettle, DeliveryCommand.Settle) => DeliveryState.Settled,
            (DeliveryState.Created or DeliveryState.Picking or DeliveryState.AwaitingApproval or DeliveryState.ReadyForHandover,
                DeliveryCommand.CancelBeforeHandover) => DeliveryState.Cancelled,
            (DeliveryState.HandedOver or DeliveryState.Delivering or DeliveryState.AwaitingReconciliation,
                DeliveryCommand.RequestCancellation) => DeliveryState.CancellationPending,
            (DeliveryState.CancellationPending, DeliveryCommand.ConfirmCancellation) => DeliveryState.Cancelled,
            (DeliveryState.ReadyForHandover or DeliveryState.HandedOver, DeliveryCommand.ReassignCourier) => state,
            _ => throw new DeliveryRuleException("TRANSITION_DENIED", "Không được chuyển trạng thái bằng thao tác này.")
        };

        if (command is DeliveryCommand.SubmitPicking or DeliveryCommand.ApprovePicking)
            DeliveryValues.Require(facts.PickingComplete, "PICKING_INCOMPLETE", "Phải ghi đủ kết quả soạn từng dòng.");
        if (command == DeliveryCommand.Handover)
            DeliveryValues.Require(facts.PickingComplete && facts.HasPickedGoods && facts.CourierAssigned && facts.CustodyHandoverConfirmed,
                "HANDOVER_INCOMPLETE", "Phải có hàng đã soạn và người giao xác nhận nhận bàn giao.");
        if (command is DeliveryCommand.Depart or DeliveryCommand.RecordOutcome or DeliveryCommand.ReassignCourier)
            DeliveryValues.Require(facts.CourierAssigned && facts.CustodyHandoverConfirmed,
                "COURIER_REQUIRED", "Cần người giao và bàn giao trách nhiệm đã xác nhận.");
        if (command is DeliveryCommand.Reconcile or DeliveryCommand.Settle or DeliveryCommand.ConfirmCancellation)
            DeliveryValues.Require(facts.GoodsReconciled && facts.MoneyReconciled,
                "RECONCILIATION_REQUIRED", "Phải đối soát hàng và tiền; báo giao xong chưa đủ để chốt.");
        if (command is DeliveryCommand.RequestCancellation or DeliveryCommand.ConfirmCancellation)
            DeliveryValues.Require(!facts.HasDeliveredGoods, "DELIVERED_CANNOT_CANCEL", "Đã giao hàng: phải đối soát/chốt hoặc hoàn sau bán.");
        return next;
    }
}
