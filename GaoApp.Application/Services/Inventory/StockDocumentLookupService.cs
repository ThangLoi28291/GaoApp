using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Service đọc dữ liệu lookup cho màn chứng từ kho.
/// Service chỉ orchestration, không đụng EF trực tiếp.
/// </summary>
public sealed class StockDocumentLookupService : IStockDocumentLookupService
{
    private readonly IStockDocumentLookupRepository _lookupRepository;

    public StockDocumentLookupService(IStockDocumentLookupRepository lookupRepository)
    {
        _lookupRepository = lookupRepository;
    }

    public async Task<List<StockDocumentVariantUnitDto>> GetVariantUnitsAsync(
        int variantId,
        CancellationToken ct = default)
    {
        if (variantId <= 0)
            return new List<StockDocumentVariantUnitDto>();

        var exists = await _lookupRepository.VariantExistsAsync(variantId, ct);
        if (!exists)
            return new List<StockDocumentVariantUnitDto>();

        return await _lookupRepository.GetVariantUnitsAsync(variantId, ct);
    }
}