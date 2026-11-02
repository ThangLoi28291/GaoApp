namespace GaoApp.Domain.Storefront;

// WEB00 executable contracts. These records are trusted server projections,
// not persistence entities or HTTP request DTOs. No database, provider calls,
// clock, stock ledger, financial ledger or POS operation is performed here.
public enum StorefrontPaymentMethod { Cod, Qr }
public enum StorefrontOrderPhase { AwaitingPayment, AwaitingPreparation, Preparing, Shipping, Delivered, Completed, Cancelled }
public enum StorefrontActorKind { Guest, Staff, System }
public enum StorefrontCancellationEffect { None, RequestReview, AwaitPhysicalReturn, ClearRequest }
public enum StorefrontStockEffect { None, Dispatch, ReceiveReturn }
public enum StorefrontPaymentDisposition { Apply, Duplicate, Review }
public enum StorefrontShippingFeeTreatment { Unconfigured, StoreServiceFee, CarrierPassThrough }

[Flags]
public enum StorefrontCapability
{
    None = 0, Prepare = 1, Dispatch = 2, RecordDelivery = 4,
    ApproveCancellation = 8, ReceiveReturn = 16, ConfirmRefund = 32,
    PostSale = 64, ProcessBankEvents = 128
}

public enum StorefrontCommand
{
    StartPreparation, Dispatch, RecordDelivery, SelfCancel,
    RequestCancellation, ApproveCancellation, ReceiveReturn,
    ConfirmRefund, ExpireUnpaidQr, PostSale, RejectCancellation
}

public sealed class StorefrontRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public static class StorefrontValues
{
    public const decimal MoneyLimit = 10_000_000_000_000_000m;
    public const decimal QuantityLimit = 100_000_000_000_000m;
    public static void Require(bool condition, string code, string message)
    {
        if (!condition) throw new StorefrontRuleException(code, message);
    }
    public static void Money(decimal amount)
        => Require(amount >= 0 && amount < MoneyLimit && decimal.Truncate(amount) == amount,
            "MONEY_INVALID", "Số tiền phải là số đồng nguyên không âm trong giới hạn lưu trữ.");
    public static void Quantity(decimal amount, bool positive = false)
        => Require((positive ? amount > 0 : amount >= 0) && amount < QuantityLimit && decimal.Round(amount, 4) == amount,
            "QUANTITY_INVALID", "Số lượng phải hợp lệ, tối đa bốn số lẻ.");
}

public sealed record StorefrontActor(int StoreId, StorefrontActorKind Kind, int? UserId,
    StorefrontCapability Capabilities, long? AccessibleOrderId = null, bool GuestAccessVerified = false);

public sealed record StorefrontWorkflowFacts
{
    public required int StoreId { get; init; }
    public required long OrderId { get; init; }
    public required int Revision { get; init; }
    public required StorefrontPaymentMethod Method { get; init; }
    public bool PaymentConfirmed { get; init; }
    public bool HasReceivedFunds { get; init; }
    public bool PaymentNeedsReview { get; init; }
    public bool CancellationRequested { get; init; }
    public bool CancellationRejectionReasonRecorded { get; init; }
    public bool PhysicalDispatchRecorded { get; init; }
    public bool TrustedHandoverConfirmed { get; init; }
    public bool AllOrderedGoodsPicked { get; init; }
    public bool TrustedDeliveryResult { get; init; }
    public bool HasDeliveredGoods { get; init; }
    public bool ReturnAuthorised { get; init; }
    public bool TrustedReturnReceipt { get; init; }
    public bool AllDispatchedGoodsReturned { get; init; }
    public bool GoodsReconciled { get; init; }
    public bool MoneyReconciled { get; init; }
    public bool RefundPending { get; init; }
    public bool ActualRefundEvidenceVerified { get; init; }
    public bool PaymentWindowExpired { get; init; }
    public bool BankConfirmedNoReceipt { get; init; }
    public bool SaleAlreadyPosted { get; init; }
    public int? SettlingShiftId { get; init; }
    public int? SettlingShiftOwnerUserId { get; init; }
    public int? SettlingShiftStoreId { get; init; }
    public bool SettlingShiftOpen { get; init; }
}

public sealed record StorefrontTransition(StorefrontOrderPhase NextPhase,
    StorefrontCancellationEffect Cancellation = StorefrontCancellationEffect.None,
    bool ReleaseOwnHolds = false, bool CreateRefundReview = false,
    StorefrontStockEffect Stock = StorefrontStockEffect.None,
    bool ConfirmRefund = false, bool PostSale = false, bool RequiresReview = false);

// The bank account owner and authorised collection parties must be configured
// explicitly. Inventory ownership does not imply bank-account ownership.
public sealed record StorefrontPaymentExpectation(int StoreId, long OrderId, string IntentId,
    int BankAccountId, int ReceivingLegalEntityId, decimal AmountDue,
    IReadOnlySet<int> StockLegalEntityIds, IReadOnlySet<int> AuthorisedCollectionLegalEntityIds);

// Verified evidence must already be durably ingested before allocation. A
// rejected allocation never authorises deleting/forgetting the bank receipt.
public sealed record StorefrontVerifiedReceipt(int StoreId, long OrderId, string IntentId,
    int BankAccountId, int ReceivingLegalEntityId, string EvidenceKey,
    decimal Amount, bool Verified);

public sealed record StorefrontPaymentDecision(StorefrontPaymentDisposition Disposition,
    StorefrontOrderPhase NextPhase, decimal RetainedReceiptAmount,
    bool ConfirmCustomerPayment, bool CreateRefundReview, string? ReviewCode = null);

public sealed record StorefrontShippingLine(decimal Quantity, decimal? WeightKgPerSellingUnit);
public sealed record StorefrontShippingRule(int StoreId, int RuleId, int Revision,
    bool ByWeight, decimal BaseFee, decimal AdditionalFee,
    decimal FirstWeightKg = 1m, decimal AdditionalStepKg = 1m, decimal? FreeFromGoodsAmount = null);
public sealed record StorefrontShippingQuote(int RuleId, int RuleRevision,
    decimal GoodsAmount, decimal? TotalWeightKg, decimal AdditionalSteps,
    decimal ShippingCharge, decimal TotalDue);

public sealed record StorefrontFinancialSplit(decimal GoodsSaleAmount,
    decimal ShippingServiceAmount, decimal CarrierPassThroughAmount,
    decimal CustomerTotalDue, decimal SeparateDeliveryExpense,
    decimal NewReceiptAtSalePosting, decimal CarrierPayableSettlement,
    decimal CarrierPayableDifference, bool RequiresFeeReview);

public sealed record StorefrontCodPosition(decimal CustomerCollected,
    decimal StoreRemitted, decimal OutstandingWithCollector,
    bool CustomerPaymentSatisfied, bool ReadyForFinancialSettlement,
    decimal VerifiedCarrierDeduction = 0);
