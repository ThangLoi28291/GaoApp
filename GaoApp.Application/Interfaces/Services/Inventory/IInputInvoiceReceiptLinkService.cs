using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceReceiptLinkService
{
    Task<bool> LinkWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        InputInvoiceHead invoice,
        string? note,
        CancellationToken ct = default);

    Task<bool> LinkWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        InputInvoiceHead invoice,
        string? note,
        bool refreshReconciliation,
        bool writeLinkAudit,
        CancellationToken ct = default);

    Task ValidateLinkWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        InputInvoiceHead invoice,
        CancellationToken ct = default);

    Task ValidateExistingLinksForConfirmWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        CancellationToken ct = default);

    Task ValidateExistingLinksForOwnerMutationWithinTransactionAsync(
        int storeId,
        StockDocument receipt,
        CancellationToken ct = default);
}
