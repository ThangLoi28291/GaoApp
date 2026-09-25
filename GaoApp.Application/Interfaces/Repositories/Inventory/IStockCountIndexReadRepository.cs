using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IStockCountIndexReadRepository
{
    Task<StockCountIndexPageDto> QueryAsync(
        int storeId,
        StockCountIndexQueryRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<StockCountIndexWarehouseOptionDto>> GetWarehouseOptionsAsync(
        int storeId,
        CancellationToken ct = default);

    Task<StockCountIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int documentId,
        CancellationToken ct = default);
}
