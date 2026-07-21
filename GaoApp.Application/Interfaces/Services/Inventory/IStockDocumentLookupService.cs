using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IStockDocumentLookupService
{
    Task<List<StockDocumentVariantUnitDto>> GetVariantUnitsAsync(
        int variantId,
        CancellationToken ct = default);
}