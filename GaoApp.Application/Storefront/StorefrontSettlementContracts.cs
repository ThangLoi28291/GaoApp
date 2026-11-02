using GaoApp.Domain.Enums;

namespace GaoApp.Application.Storefront;

public sealed class StorefrontCodCollectionRequest : StorefrontOperationRequest
{
    public int FulfillmentSourceId { get; set; }
    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public decimal Amount { get; set; }
    public DateTimeOffset CustomerPaidAt { get; set; }
    public string EvidenceReference { get; set; } = "";
}
public sealed class StorefrontCodBankReceiptRequest : StorefrontOperationRequest
{
    public int GlobalBankReceiptId { get; set; }
    public int ReceivingLegalEntityId { get; set; }
    public string EvidenceReference { get; set; } = "";
}
public sealed class StorefrontCodCashReceiptRequest : StorefrontOperationRequest
{
    public int? TreasuryEntryId { get; set; }
    public int? POSShiftCashTransactionId { get; set; }
}
public sealed class StorefrontCarrierWithholdingRequest : StorefrontOperationRequest
{
    public int OperatingExpenseId { get; set; }
    public int LegalEntityId { get; set; }
    public string CarrierEvidenceReference { get; set; } = "";
}
public sealed class StorefrontServiceRevenueSettingRequest
{
    public string? Version { get; set; }
    public int LegalEntityId { get; set; }
    public int TaxId { get; set; }
    public string Name { get; set; } = "Phí dịch vụ giao hàng";
    public InvoiceIssuanceRoute InvoiceRoute { get; set; } = InvoiceIssuanceRoute.Automatic;
}
public sealed record StorefrontServiceRevenueSettingDto(string Version, int LegalEntityId, int TaxId, string Name, InvoiceIssuanceRoute InvoiceRoute);
public sealed record StorefrontSettlementSummary(decimal CustomerPaid, decimal ActualStoreReceived, decimal WithheldExpense,
    decimal CollectorOutstanding, bool CanPostSale, string? BlockReason, int? SaleOrderId)
{
    public string CustomerPaidText => StorefrontCheckoutText.Money(CustomerPaid);
    public string ActualStoreReceivedText => StorefrontCheckoutText.Money(ActualStoreReceived);
    public string WithheldExpenseText => StorefrontCheckoutText.Money(WithheldExpense);
    public string CollectorOutstandingText => StorefrontCheckoutText.Money(CollectorOutstanding);
}
public interface IStorefrontSettlementService
{
    Task<StorefrontSettlementSummary> GetAsync(int orderId, CancellationToken ct = default);
    Task<StorefrontOperationAck> RecordCustomerCollectionAsync(int id, StorefrontCodCollectionRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> ReceiveBankRemittanceAsync(int id, StorefrontCodBankReceiptRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> ReceiveCashRemittanceAsync(int id, StorefrontCodCashReceiptRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> RecordCarrierWithholdingAsync(int id, StorefrontCarrierWithholdingRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> PostSaleAsync(int id, StorefrontOperationRequest request, CancellationToken ct = default);
    Task<StorefrontServiceRevenueSettingDto?> GetServiceSettingAsync(CancellationToken ct = default);
    Task<StorefrontServiceRevenueSettingDto> SaveServiceSettingAsync(StorefrontServiceRevenueSettingRequest request, CancellationToken ct = default);
}
public sealed record StorefrontCodBankCandidate(int GlobalReceiptId, string ExternalId, string AmountText, string BankTimeText);
public interface IStorefrontCodBankSourceService
{
    Task<IReadOnlyList<StorefrontCodBankCandidate>> ListAsync(CancellationToken ct = default);
    // ID only: the complete raw notification and authenticated acceptance are revalidated on the server.
    Task<int> ImportAuthenticatedNotificationAsync(int notificationReceiptId, CancellationToken ct = default);
}
// Identity-only server bridge; every quantity, owner and monetary value is re-read from persistence.
public interface IStorefrontSaleBridge
{
    Task<int> PostAsync(int storefrontOrderId, Guid requestId, CancellationToken ct = default);
}
public interface IStorefrontInvoiceProcessor
{
    Task ProcessAsync(int storefrontOrderId, CancellationToken ct = default);
}
