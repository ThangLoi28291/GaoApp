using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceInputStockReadRepository
{
    Task<IReadOnlyList<InvoiceInputStockMovement>> GetMovementsAsync(int storeId, CancellationToken ct = default);

    /// <summary>
    /// Reads only the product/warehouse slice needed by a ledger drill-down.
    /// Keeping this filter at the repository boundary prevents the web page
    /// from materialising the complete store history for every click.
    /// </summary>
    Task<IReadOnlyList<InvoiceInputStockMovement>> GetMovementsAsync(
        int storeId,
        IReadOnlyCollection<int>? productVariantIds,
        IReadOnlyCollection<int>? warehouseIds,
        CancellationToken ct = default);
}
