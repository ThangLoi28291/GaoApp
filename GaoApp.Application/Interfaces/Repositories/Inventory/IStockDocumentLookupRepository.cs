using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IStockDocumentLookupRepository
{
    Task<bool> VariantExistsAsync(int variantId, CancellationToken ct = default);

    Task<List<StockDocumentVariantUnitDto>> GetVariantUnitsAsync(
        int variantId,
        CancellationToken ct = default);
}