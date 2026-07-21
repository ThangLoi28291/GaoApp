using GaoApp.Application.DTOs.Inventory.InputInvoices;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInputInvoiceXmlService
{
    Task<InputInvoiceXmlUploadResultDto> UploadXmlAsync(
        int storeId,
        UploadInputInvoiceXmlRequest request,
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
    CancellationToken ct = default);

}