namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceSupplierResolutionService
{
    Task BindCanonicalSupplierAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task BindCanonicalSupplierWithinTransactionAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task EnsureReceiptCanBeConfirmedAsync(
        int storeId,
        int stockDocumentId,
        int? receiptSupplierId,
        CancellationToken ct = default);
}
