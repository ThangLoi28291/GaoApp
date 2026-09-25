using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IStockTransferIndexReadService
{
    Task<StockTransferIndexPageDto> GetPageAsync(
        StockTransferIndexQueryRequest request,
        CancellationToken ct = default);

    Task<StockTransferIndexQuickViewDto?> GetQuickViewAsync(
        int documentId,
        CancellationToken ct = default);
}
