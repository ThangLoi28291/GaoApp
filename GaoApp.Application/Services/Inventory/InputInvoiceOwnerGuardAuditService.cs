using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Globalization;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoiceOwnerGuardAuditService(IInputInvoiceRepository repository)
    : IInputInvoiceOwnerGuardAuditService
{
    public Task RecordBlockedLinkAsync(int storeId, int stockDocumentId,
        InputInvoiceOwnerGuardException exception,
        CancellationToken ct = default)
        => RecordAsync(storeId, stockDocumentId, exception.InputInvoiceHeadId,
            PurchaseReceiptAuditEventType.InputInvoiceOwnerLinkBlocked, exception, ct);

    public Task RecordBlockedConfirmAsync(int storeId, int stockDocumentId,
        InputInvoiceOwnerGuardException exception, CancellationToken ct = default)
        => RecordAsync(storeId, stockDocumentId, null,
            PurchaseReceiptAuditEventType.InputInvoiceOwnerConfirmBlocked, exception, ct);

    private async Task RecordAsync(int storeId, int receiptId, int? invoiceId,
        PurchaseReceiptAuditEventType type, InputInvoiceOwnerGuardException exception,
        CancellationToken ct)
    {
        await repository.ClearFailedTransactionStateAsync(ct);
        await repository.AddPurchaseReceiptAuditEventAsync(new PurchaseReceiptAuditEvent
        {
            StoreId = storeId,
            StockDocumentId = receiptId,
            EventType = type,
            IsSuccess = false,
            Note = exception.Message,
            ChangedFieldsJson = PurchaseReceiptAuditEvidence.SerializeChangedFields(
                [nameof(InputInvoiceHead.ResolvedBuyerLegalEntityId)]),
            NewValuesJson = PurchaseReceiptAuditEvidence.SerializeValues(new Dictionary<string, object?>
            {
                ["ReasonCode"] = exception.ReasonCode,
                ["ResolutionStatus"] = exception.ResolutionStatus.ToString(),
                ["InputInvoiceHeadId"] = invoiceId,
                ["BuyerTaxCode"] = exception.BuyerTaxCode,
                ["NormalizedBuyerTaxCode"] = exception.NormalizedBuyerTaxCode,
                ["NormalizedSellerTaxCode"] = exception.NormalizedSellerTaxCode,
                ["NormalizedInvoiceSeries"] = exception.NormalizedInvoiceSeries,
                ["NormalizedInvoiceNumber"] = exception.NormalizedInvoiceNumber,
                ["InvoiceIdentityDate"] = exception.InvoiceIdentityDate?
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["ReceiptOwnerLegalEntityId"] = exception.ReceiptOwnerLegalEntityId,
                ["InvoiceOwnerLegalEntityId"] = exception.InvoiceOwnerLegalEntityId
            })
        }, ct);
        await repository.SaveChangesAsync(ct);
    }
}
