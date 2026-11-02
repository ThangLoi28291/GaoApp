namespace GaoApp.Domain.Storefront;

public static class StorefrontWorkflowPolicy
{
    private const StorefrontCapability AllCapabilities = StorefrontCapability.Prepare | StorefrontCapability.Dispatch |
        StorefrontCapability.RecordDelivery | StorefrontCapability.ApproveCancellation | StorefrontCapability.ReceiveReturn |
        StorefrontCapability.ConfirmRefund | StorefrontCapability.PostSale | StorefrontCapability.ProcessBankEvents;

    public static StorefrontOrderPhase InitialPhase(StorefrontPaymentMethod method)
    {
        StorefrontValues.Require(Enum.IsDefined(method), "PAYMENT_METHOD_INVALID", "Phương thức thanh toán không hợp lệ.");
        return method == StorefrontPaymentMethod.Cod
            ? StorefrontOrderPhase.AwaitingPreparation : StorefrontOrderPhase.AwaitingPayment;
    }

    public static StorefrontTransition Apply(StorefrontOrderPhase phase, StorefrontCommand command,
        StorefrontWorkflowFacts facts, StorefrontActor actor, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(actor);
        StorefrontValues.Require(Enum.IsDefined(phase) && Enum.IsDefined(command) && Enum.IsDefined(facts.Method),
            "STATE_INVALID", "Trạng thái hoặc thao tác không hợp lệ.");
        StorefrontValues.Require(facts.StoreId > 0 && facts.OrderId > 0 && facts.Revision > 0,
            "ORDER_SCOPE_INVALID", "Phạm vi đơn hàng không hợp lệ.");
        StorefrontValues.Require(actor.StoreId == facts.StoreId && Enum.IsDefined(actor.Kind) &&
            (actor.Capabilities & ~AllCapabilities) == 0,
            "ACTOR_FORBIDDEN", "Người thao tác không thuộc phạm vi đơn.");
        StorefrontValues.Require(expectedRevision == facts.Revision, "REVISION_CONFLICT", "Đơn đã thay đổi.");

        void Staff(StorefrontCapability required)
            => StorefrontValues.Require(actor.Kind == StorefrontActorKind.Staff && actor.UserId is > 0 &&
                (actor.Capabilities & required) == required,
                "CAPABILITY_REQUIRED", "Thao tác cần nhân viên có quyền tương ứng.");
        void Guest()
            => StorefrontValues.Require(actor.Kind == StorefrontActorKind.Guest && actor.GuestAccessVerified &&
                actor.AccessibleOrderId == facts.OrderId,
                "ORDER_ACCESS_REQUIRED", "Khách phải có quyền truy cập riêng của đơn này.");
        void PayReady()
            => StorefrontValues.Require(!facts.PaymentNeedsReview &&
                (facts.Method == StorefrontPaymentMethod.Cod || facts.PaymentConfirmed),
                "PAYMENT_NOT_READY", "Khoản thanh toán chưa đủ điều kiện chuẩn bị/giao.");
        void Require(bool condition, string code = "TRANSITION_DENIED")
            => StorefrontValues.Require(condition, code, "Không được thực hiện thao tác ở trạng thái hiện tại.");

        switch (command)
        {
            case StorefrontCommand.StartPreparation:
                Staff(StorefrontCapability.Prepare);
                Require(phase == StorefrontOrderPhase.AwaitingPreparation && !facts.CancellationRequested && !facts.PhysicalDispatchRecorded);
                PayReady();
                return new(StorefrontOrderPhase.Preparing);
            case StorefrontCommand.Dispatch:
                Staff(StorefrontCapability.Dispatch);
                Require(phase == StorefrontOrderPhase.Preparing && !facts.CancellationRequested && !facts.PhysicalDispatchRecorded);
                PayReady();
                Require(facts.TrustedHandoverConfirmed && facts.AllOrderedGoodsPicked, "HANDOVER_REQUIRED");
                return new(StorefrontOrderPhase.Shipping, Stock: StorefrontStockEffect.Dispatch);
            case StorefrontCommand.RecordDelivery:
                Staff(StorefrontCapability.RecordDelivery);
                Require(phase == StorefrontOrderPhase.Shipping && facts.PhysicalDispatchRecorded &&
                    facts.TrustedDeliveryResult && !facts.ReturnAuthorised, "DELIVERY_PROOF_REQUIRED");
                return new(StorefrontOrderPhase.Delivered,
                    facts.CancellationRequested ? StorefrontCancellationEffect.ClearRequest : StorefrontCancellationEffect.None,
                    RequiresReview: facts.CancellationRequested);
            case StorefrontCommand.SelfCancel:
                Guest();
                Require(phase is StorefrontOrderPhase.AwaitingPayment or StorefrontOrderPhase.AwaitingPreparation);
                Require(!facts.PhysicalDispatchRecorded);
                return new(StorefrontOrderPhase.Cancelled, StorefrontCancellationEffect.ClearRequest,
                    ReleaseOwnHolds: true, CreateRefundReview: facts.HasReceivedFunds);
            case StorefrontCommand.RequestCancellation:
                Guest();
                Require(phase is StorefrontOrderPhase.Preparing or StorefrontOrderPhase.Shipping);
                Require(!facts.HasDeliveredGoods && !facts.ReturnAuthorised);
                return new(phase, StorefrontCancellationEffect.RequestReview);
            case StorefrontCommand.ApproveCancellation:
                Staff(StorefrontCapability.ApproveCancellation);
                Require(facts.CancellationRequested && !facts.HasDeliveredGoods);
                if (phase == StorefrontOrderPhase.Preparing && !facts.PhysicalDispatchRecorded)
                    return new(StorefrontOrderPhase.Cancelled, StorefrontCancellationEffect.ClearRequest,
                        ReleaseOwnHolds: true, CreateRefundReview: facts.HasReceivedFunds);
                Require(phase == StorefrontOrderPhase.Shipping && facts.PhysicalDispatchRecorded);
                return new(phase, StorefrontCancellationEffect.AwaitPhysicalReturn);
            case StorefrontCommand.RejectCancellation:
                Staff(StorefrontCapability.ApproveCancellation);
                Require(phase is StorefrontOrderPhase.Preparing or StorefrontOrderPhase.Shipping);
                Require(facts.CancellationRequested && !facts.ReturnAuthorised && !facts.HasDeliveredGoods &&
                    facts.CancellationRejectionReasonRecorded, "CANCELLATION_REASON_REQUIRED");
                return new(phase, StorefrontCancellationEffect.ClearRequest);
            case StorefrontCommand.ReceiveReturn:
                Staff(StorefrontCapability.ReceiveReturn);
                Require(phase == StorefrontOrderPhase.Shipping && facts.ReturnAuthorised && facts.PhysicalDispatchRecorded &&
                    facts.TrustedReturnReceipt && facts.AllDispatchedGoodsReturned && !facts.HasDeliveredGoods,
                    "PHYSICAL_RETURN_REQUIRED");
                return new(StorefrontOrderPhase.Cancelled, StorefrontCancellationEffect.ClearRequest,
                    CreateRefundReview: facts.HasReceivedFunds, Stock: StorefrontStockEffect.ReceiveReturn);
            case StorefrontCommand.ConfirmRefund:
                Staff(StorefrontCapability.ConfirmRefund);
                Require(phase == StorefrontOrderPhase.Cancelled && facts.RefundPending && facts.HasReceivedFunds &&
                    facts.ActualRefundEvidenceVerified && facts.MoneyReconciled, "REFUND_PROOF_REQUIRED");
                return new(phase, ConfirmRefund: true);
            case StorefrontCommand.ExpireUnpaidQr:
                Require(actor.Kind == StorefrontActorKind.System &&
                    (actor.Capabilities & StorefrontCapability.ProcessBankEvents) != 0, "SYSTEM_BANK_WORK_REQUIRED");
                Require(phase == StorefrontOrderPhase.AwaitingPayment && facts.Method == StorefrontPaymentMethod.Qr &&
                    !facts.PaymentConfirmed && !facts.HasReceivedFunds && !facts.PhysicalDispatchRecorded);
                Require(facts.PaymentWindowExpired && facts.BankConfirmedNoReceipt && !facts.PaymentNeedsReview,
                    "BANK_OUTCOME_UNCLEAR");
                return new(StorefrontOrderPhase.Cancelled, ReleaseOwnHolds: true);
            case StorefrontCommand.PostSale:
                Staff(StorefrontCapability.PostSale);
                Require(phase == StorefrontOrderPhase.Delivered && facts.PhysicalDispatchRecorded && facts.HasDeliveredGoods &&
                    facts.GoodsReconciled && facts.MoneyReconciled && facts.PaymentConfirmed && !facts.PaymentNeedsReview &&
                    !facts.CancellationRequested && !facts.ReturnAuthorised && !facts.RefundPending && !facts.SaleAlreadyPosted,
                    "SETTLEMENT_NOT_READY");
                Require(facts.SettlingShiftId is > 0 && facts.SettlingShiftOpen &&
                    facts.SettlingShiftStoreId == facts.StoreId && facts.SettlingShiftOwnerUserId == actor.UserId,
                    "REAL_SETTLING_SHIFT_REQUIRED");
                return new(StorefrontOrderPhase.Completed, PostSale: true);
            default:
                throw new StorefrontRuleException("TRANSITION_DENIED", "Thao tác không được hỗ trợ.");
        }
    }
}
