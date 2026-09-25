using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IStockCountIndexReadService
{
    Task<StockCountIndexPageDto> GetPageAsync(
        StockCountIndexQueryRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<StockCountIndexWarehouseOptionDto>> GetWarehouseOptionsAsync(
        CancellationToken ct = default);

    Task<StockCountIndexQuickViewDto?> GetQuickViewAsync(
        int documentId,
        CancellationToken ct = default);
}
