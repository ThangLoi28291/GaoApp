using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceReceiptOwnerGuard
{
    Task<InputInvoiceReceiptOwnerDecision> ValidateLinkWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        InputInvoiceHead invoice,
        CancellationToken ct = default);

    Task<InputInvoiceReceiptOwnerDecision> ValidateConfirmWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        CancellationToken ct = default);
}

public sealed record InputInvoiceReceiptOwnerDecision(
    int ReceiptOwnerLegalEntityId,
    int? InvoiceOwnerLegalEntityId,
    bool UsesConfirmedOwnerSnapshot);
