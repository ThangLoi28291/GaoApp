namespace GaoApp.Domain.Delivery;

public static class DeliveryAccessPolicy
{
    private const DeliveryCapability KnownCapabilities = (DeliveryCapability)131071;

    public static void Ensure(DeliveryActor actor, DeliveryOrigin origin, DeliveryCapability required)
    {
        DeliveryValues.Origin(origin);
        DeliveryValues.Require(actor.UserId > 0 && actor.StoreId == origin.StoreId,
            "STORE_FORBIDDEN", "Không được thao tác đơn giao khác cửa hàng.");
        DeliveryValues.Require(required != DeliveryCapability.None && (required & ~KnownCapabilities) == 0 &&
            (actor.Capabilities & ~KnownCapabilities) == 0 && (actor.Capabilities & required) == required,
            "CAPABILITY_FORBIDDEN", "Bạn chưa có quyền cho thao tác giao hàng này.");
        DeliveryValues.Require(actor.WarehouseIds.Contains(origin.SourceWarehouseId) && actor.LegalEntityIds.Contains(origin.SourceLegalEntityId),
            "SOURCE_FORBIDDEN", "Không có phạm vi kho/chủ thể của nguồn xuất.");
    }

    public static DeliveryCapability For(DeliveryCommand command) => command switch
    {
        DeliveryCommand.StartPicking or DeliveryCommand.SubmitPicking => DeliveryCapability.Pick,
        DeliveryCommand.ApprovePicking or DeliveryCommand.ReopenPicking => DeliveryCapability.ApproveChanges,
        DeliveryCommand.Handover => DeliveryCapability.Handover,
        DeliveryCommand.Depart => DeliveryCapability.RecordDeparture,
        // RecordOutcome uses EnsureReport, which additionally validates the assigned courier.
        DeliveryCommand.RecordOutcome => DeliveryCapability.ReportOwnOutcome,
        DeliveryCommand.Reconcile => DeliveryCapability.ReceiveReturns | DeliveryCapability.ReconcileCash,
        DeliveryCommand.Settle => DeliveryCapability.Finalize,
        DeliveryCommand.CancelBeforeHandover or DeliveryCommand.RequestCancellation => DeliveryCapability.Cancel,
        DeliveryCommand.ConfirmCancellation => DeliveryCapability.Cancel | DeliveryCapability.ReceiveReturns | DeliveryCapability.ReconcileCash,
        DeliveryCommand.ReassignCourier => DeliveryCapability.ReassignCourier,
        _ => throw new DeliveryRuleException("COMMAND_INVALID", "Thao tác không hợp lệ.")
    };

    public static void EnsureReport(DeliveryActor actor, DeliveryOrigin origin, int assignedCourierId,
        int reportedCourierId, DeliveryReportSource source)
    {
        DeliveryValues.Require(assignedCourierId > 0 && assignedCourierId == reportedCourierId,
            "COURIER_MISMATCH", "Báo cáo phải thuộc người đang được bàn giao đơn.");
        DeliveryValues.Require(Enum.IsDefined(source), "REPORT_SOURCE_INVALID", "Nguồn báo cáo không hợp lệ.");
        var own = actor.UserId == assignedCourierId;
        Ensure(actor, origin, own ? DeliveryCapability.ReportOwnOutcome : DeliveryCapability.ReportOnBehalf);
        DeliveryValues.Require(own || source is DeliveryReportSource.Phone or DeliveryReportSource.AtStore,
            "PROXY_SOURCE_INVALID", "Nhập hộ phải ghi nguồn báo tại tiệm hoặc điện thoại.");
        DeliveryValues.Require(source != DeliveryReportSource.Phone || !own,
            "REPORT_SOURCE_INVALID", "Nguồn gọi điện dùng cho nhân viên tại tiệm ghi nhận hộ.");
    }
}
