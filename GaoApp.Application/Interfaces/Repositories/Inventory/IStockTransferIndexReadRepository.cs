using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IStockTransferIndexReadRepository
{
    Task<StockTransferIndexPageDto> QueryAsync(
        int storeId,
        StockTransferIndexQueryRequest request,
        CancellationToken ct = default);

    Task<StockTransferIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int documentId,
        CancellationToken ct = default);
}
