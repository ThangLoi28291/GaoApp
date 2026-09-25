using GaoApp.Application.DTOs.Inventory.InputInvoices;

using GaoApp.Application.Interfaces.Repositories.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceXmlService
{
    Task<InputInvoiceXmlUploadResultDto> UploadXmlAsync(
        int storeId,
        UploadInputInvoiceXmlRequest request,
        CancellationToken ct = default);

    Task<InputInvoicePickerSelectionResultDto> ImportAndLinkAsync(
        int storeId,
        int stockDocumentId,
        int receiptSupplierId,
        string receiptSupplierTaxCode,
        string originalFileName,
        byte[] fileBytes,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves/imports the immutable invoice identity inside the caller's transaction.
    /// The caller owns association locking, audit grouping, and the single deferred RECON refresh;
    /// this operation must not mutate receipt posting effects.
    /// </summary>
    Task<InputInvoiceResolution> ResolveInvoiceWithinTransactionAsync(
        int storeId,
        string receiptSupplierTaxCode,
        string originalFileName,
        byte[] fileBytes,
        CancellationToken ct = default);
    Task<List<InputInvoiceHeadDto>> GetInvoicesByStockDocumentAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default);
    Task<List<StockDocumentLineInputInvoiceMapDto>> GetLineMapsAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default);

    Task UpdateLineMapAsync(
        int storeId,
        int stockDocumentId,
        UpdateStockDocumentLineInputInvoiceMapRequest request,
        CancellationToken ct = default);
    Task BulkUpdateLineMapsAsync(
    int storeId,
    int stockDocumentId,
    bool useInputInvoice,
    string? exclusionReason,
    CancellationToken ct = default);

}
