using GaoApp.Domain.Common;

namespace GaoApp.Domain.Storefront;

public sealed class StorefrontQuote : BaseStoreEntity
{
    public Guid Token { get; set; }
    public int Revision { get; set; } = 1;
    public int SettingsRevision { get; set; }
    public int ShippingRateId { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public decimal GoodsAmount { get; set; }
    public decimal ShippingCharge { get; set; }
    public decimal TotalDue { get; set; }
    public string SnapshotJson { get; set; } = "{}";
}

public sealed class StorefrontOrder : BaseStoreEntity
{
    public bool? UseMultiLegalEntitySnapshot { get; set; }
    public DateTime? LegalEntityModeCapturedAtUtc { get; set; }
    public string Code { get; set; } = "";
    public string GuestTokenHash { get; set; } = "";
    public int QuoteId { get; set; }
    public int Revision { get; set; } = 1;
    public StorefrontOrderPhase Phase { get; set; }
    public StorefrontPaymentMethod PaymentMethod { get; set; }
    public StorefrontPaymentState PaymentState { get; set; }
    public StorefrontDeliveryState DeliveryState { get; set; }
    public StorefrontRefundState RefundState { get; set; }
    public StorefrontDeliveryMethod DeliveryMethod { get; set; }
    public string RecipientName { get; set; } = "";
    public string RecipientPhone { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string? Note { get; set; }
    public string AddressSnapshotJson { get; set; } = "{}";
    public decimal GoodsAmount { get; set; }
    public decimal ShippingCharge { get; set; }
    public decimal TotalDue { get; set; }
    public StorefrontShippingFeeTreatment ShippingFeeTreatment { get; set; } = StorefrontShippingFeeTreatment.StoreServiceFee;
    public DateTime? PaymentExpiresAtUtc { get; set; }
}

public sealed class StorefrontOrderLine : BaseStoreEntity
{
    public bool? HasInputInvoiceSnapshot { get; set; }
    public int StorefrontOrderId { get; set; }
    public int ProductId { get; set; }
    public int VariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public int SellingUnitId { get; set; }
    public string ItemName { get; set; } = "";
    public string UnitName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal BaseMultiplier { get; set; } = 1;
    public decimal BaseQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal RoundingAdjustment { get; set; }
    public decimal NetAmount { get; set; }
    public decimal WeightKgPerSellingUnit { get; set; }
    public bool IsGift { get; set; }
    public string SnapshotJson { get; set; } = "{}";
}

public sealed class StorefrontPaymentIntent : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public bool IsActive { get; set; } = true;
    public string Provider { get; set; } = "ACB";
    public string ProviderReference { get; set; } = "";
    public int BankAccountId { get; set; }
    public int ReceivingLegalEntityId { get; set; }
    public decimal AmountDue { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string BankTerminalCode { get; set; } = "";
    public string BankUserCode { get; set; } = "";
    public string BindingSnapshotJson { get; set; } = "{}";
}

// Durable evidence remains separate from whether an order can accept its money.
public sealed class StorefrontBankReceipt : BaseStoreEntity
{
    public int? GlobalBankReceiptId { get; set; }
    public string Provider { get; set; } = "";
    public int BankAccountId { get; set; }
    public int ReceivingLegalEntityId { get; set; }
    public string ExternalTransactionId { get; set; } = "";
    public string EvidenceFingerprint { get; set; } = "";
    public string ProtectedEvidence { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public bool IsVerified { get; set; }
    public bool NeedsReview { get; set; }
}

public sealed class StorefrontPaymentAllocation : BaseStoreEntity
{
    public bool ReceiptVerified { get; set; } = true;
    public int BankReceiptId { get; set; }
    public int PaymentIntentId { get; set; }
    public int StorefrontOrderId { get; set; }
    public int BankAccountId { get; set; }
    public int ReceivingLegalEntityId { get; set; }
    public decimal Amount { get; set; }
}

// A future adapter links the legacy delivery only after it supports web origins.
// WEB01 never manufactures a POS cart or shift to populate that link.
public sealed class StorefrontFulfillmentSource : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int SourceWarehouseId { get; set; }
    public int SourceLegalEntityId { get; set; }
    public int? DeliveryOrderId { get; set; }
    public string DispatchPostingKey { get; set; } = "";
}

public sealed class StorefrontLineSource : BaseStoreEntity
{
    public int OrderLineId { get; set; }
    public int FulfillmentSourceId { get; set; }
    public int StorefrontOrderId { get; set; }
    public decimal BaseQuantity { get; set; }
    public int? InventoryTransactionId { get; set; }
    public decimal RecordedCost { get; set; }
    public bool IsProvisionalCost { get; set; }
}

public sealed class StorefrontReservationLink : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int OrderLineId { get; set; }
    public int InventoryReservationId { get; set; }
}

public sealed class StorefrontSettlementLink : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int FulfillmentSourceId { get; set; }
    public int SaleOrderId { get; set; }
    public int SettlingShiftId { get; set; }
    public int SettledByUserId { get; set; }
    public decimal GoodsAmount { get; set; }
    public decimal ShippingServiceAmount { get; set; }
    public decimal DeliveryExpense { get; set; }
    public string AllocationSnapshotJson { get; set; } = "{}";
}

public sealed class StorefrontRefund : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public StorefrontRefundState State { get; set; } = StorefrontRefundState.Requested;
    public string Reason { get; set; } = "";
    public decimal GoodsAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal UnallocatedReceiptAmount { get; set; }
    public decimal Amount { get; set; }
    public int? ApprovedByUserId { get; set; }
}

public sealed class StorefrontRefundEvidence : BaseStoreEntity
{
    public int RefundId { get; set; }
    public int StorefrontOrderId { get; set; }
    public int? BankReceiptId { get; set; }
    public int? FundingId { get; set; }
    public StorefrontRefundPayoutSourceKind? PayoutSourceKind { get; set; }
    public int? TreasuryEntryId { get; set; }
    public int? POSShiftCashTransactionId { get; set; }
    public int? PayerBankAccountId { get; set; }
    public int? PayerLegalEntityId { get; set; }
    public string? Fund { get; set; }
    public string? VerificationMethod { get; set; }
    public string? EvidenceReference { get; set; }
    public string? ProtectedPayoutEvidence { get; set; }
    public string? SourceSnapshotJson { get; set; }
    public string ExternalPayoutKey { get; set; } = "";
    public string EvidenceFingerprint { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime ExecutedAtUtc { get; set; }
    public int ConfirmedByUserId { get; set; }
}

public sealed class StorefrontCommandReceipt : BaseStoreEntity
{
    public Guid ClientRequestId { get; set; }
    public string Operation { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int? StorefrontOrderId { get; set; }
    public int? ActorUserId { get; set; }
    public string OutcomeJson { get; set; } = "{}";
}

public sealed class StorefrontEvent : BaseStoreEntity
{
    public Guid EventId { get; set; }
    public int StorefrontOrderId { get; set; }
    public int Revision { get; set; }
    public string Kind { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public Guid CausationId { get; set; }
    public int? ActorUserId { get; set; }
}

public sealed class StorefrontOutboxMessage : BaseStoreEntity
{
    public Guid EventId { get; set; }
    public int StorefrontOrderId { get; set; }
    public int Revision { get; set; }
    public string Kind { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public int Attempts { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
}
