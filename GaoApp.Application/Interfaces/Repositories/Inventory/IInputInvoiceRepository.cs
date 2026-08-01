using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IInputInvoiceRepository
{
    Task<InputInvoiceHead?> GetByXmlHashAsync(
        int storeId,
        string xmlHash,
        CancellationToken ct = default);

    Task AddInputInvoiceAsync(
        InputInvoiceHead entity,
        CancellationToken ct = default);

    Task<bool> ExistsStockDocumentInvoiceMapAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task AddStockDocumentInvoiceMapAsync(
        StockDocumentInputInvoiceMap entity,
        CancellationToken ct = default);

    Task<StockDocument?> GetStockDocumentWithLinesAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task AddMissingLineMapsAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
    Task<List<InputInvoiceHead>> GetByStockDocumentAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default);
    Task<List<StockDocumentLineInputInvoiceMap>> GetLineMapsByStockDocumentAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default);

    Task<StockDocumentLineInputInvoiceMap?> GetLineMapAsync(
        int storeId,
        int stockDocumentId,
        int stockDocumentLineId,
        CancellationToken ct = default);

    Task<InputInvoiceDetail?> GetInputInvoiceDetailAsync(
        int storeId,
        int stockDocumentId,
        int stockDocumentLineId,
        int inputInvoiceDetailId,
        CancellationToken ct = default);

    Task<StockDocumentLine?> GetStockDocumentLineAsync(
        int storeId,
        int stockDocumentLineId,
        CancellationToken ct = default);
}
