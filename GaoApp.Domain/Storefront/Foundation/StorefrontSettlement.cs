using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Storefront;

public sealed class StorefrontServiceRevenueSetting : BaseStoreEntity
{
    public int LegalEntityId { get; set; }
    public int TaxId { get; set; }
    public string Name { get; set; } = "Phí dịch vụ giao hàng";
    public InvoiceIssuanceRoute InvoiceRoute { get; set; } = InvoiceIssuanceRoute.Automatic;
}
public sealed class StorefrontServiceRevenue : BaseStoreEntity
{
    public bool CashOnlyFunding { get; set; }
    public int StorefrontOrderId { get; set; }
    public int LegalEntityId { get; set; }
    public int TaxId { get; set; }
    public string Name { get; set; } = "";
    public decimal GrossAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TaxRate { get; set; }
    public InvoiceIssuanceRoute InvoiceRoute { get; set; }
    public int SettlingShiftId { get; set; }
    public int ActorUserId { get; set; }
    public DateTime RecognizedAtUtc { get; set; }
    public string SourceSnapshotJson { get; set; } = "{}";
}
public sealed class StorefrontSaleBinding : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int SaleOrderId { get; set; }
    public int SettlingShiftId { get; set; }
    public int ActorUserId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string SourceSnapshotJson { get; set; } = "{}";
}
public sealed class StorefrontServiceFunding : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int ServiceRevenueId { get; set; }
    public int? GlobalBankReceiptId { get; set; }
    public int? CodReceiptId { get; set; }
    public int? CarrierWithholdingId { get; set; }
    public decimal Amount { get; set; }
}
public sealed class StorefrontInvoiceWork : BaseStoreEntity
{
    public DateTime NextAttemptAtUtc { get; set; }
    public int StorefrontOrderId { get; set; }
    public int SaleOrderId { get; set; }
    public int? ServiceRevenueId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastErrorCode { get; set; }
}
public sealed class StorefrontCodCollection : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int FulfillmentSourceId { get; set; }
    public int RecordedByUserId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CustomerPaidAtUtc { get; set; }
    public string EvidenceReference { get; set; } = "";
}
// Authorized append routing proof. The original receipt's Unknown channel and latches do not change.
public sealed class StorefrontCodBankRouting : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int GlobalBankReceiptId { get; set; }
    public int ReceivingLegalEntityId { get; set; }
    public int ActorUserId { get; set; }
    public string AuthoritySnapshotJson { get; set; } = "{}";
    public string EvidenceReference { get; set; } = "";
}
public sealed class StorefrontCodReceipt : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int? GlobalBankReceiptId { get; set; }
    public int? CodBankRoutingId { get; set; }
    public int? TreasuryEntryId { get; set; }
    public int? POSShiftCashTransactionId { get; set; }
    public decimal Amount { get; set; }
    public int ActorUserId { get; set; }
    public string SourceSnapshotJson { get; set; } = "{}";
}
public sealed class StorefrontCarrierWithholding : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int OperatingExpenseId { get; set; }
    public int LegalEntityId { get; set; }
    public int ActorUserId { get; set; }
    public decimal Amount { get; set; }
    public string CarrierEvidenceReference { get; set; } = "";
}
// Written only by actual HTTPS/device-token authenticated acceptance, alongside the new raw receipt.
public sealed class StorefrontNotificationAttestation : BaseStoreEntity
{
    public bool HadManualAmbiguity { get; set; }
    public int BankNotificationReceiptId { get; set; }
    public int DeviceId { get; set; }
    public int BankAccountId { get; set; }
    public string AccountIdentity { get; set; } = "";
    public string AccountVersion { get; set; } = "";
    public string SourcePackage { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string EvidenceKey { get; set; } = "";
    public string NativeTransactionId { get; set; } = "";
    public DateTime? BankOccurredAtUtc { get; set; }
    public DateTime AuthenticatedAtUtc { get; set; }
    public string AccountSnapshotJson { get; set; } = "{}";
    public string CollectionAuthoritySnapshotJson { get; set; } = "[]";
}
public sealed class StorefrontNotificationCertification : BaseStoreEntity
{
    public int NotificationAttestationId { get; set; }
    public int GlobalBankReceiptId { get; set; }
}
public sealed class StorefrontInventoryIssueSource : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int OrderInventoryIssueId { get; set; }
    public int OrderInventoryIssueLineId { get; set; }
    public int InventoryValuationEntryId { get; set; }
    public int InventoryCostLayerAllocationId { get; set; }
    public decimal BaselineResolvedQuantity { get; set; }
    public decimal UnresolvedQuantity { get; set; }
}
