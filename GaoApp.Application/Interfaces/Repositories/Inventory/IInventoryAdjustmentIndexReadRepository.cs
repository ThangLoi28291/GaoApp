using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IInventoryAdjustmentIndexReadRepository
{
    Task<InventoryAdjustmentIndexPageDto> QueryAsync(
        int storeId,
        InventoryAdjustmentIndexQueryRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<InventoryAdjustmentIndexWarehouseOptionDto>> GetWarehouseOptionsAsync(
        int storeId,
        CancellationToken ct = default);

    Task<InventoryAdjustmentIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int documentId,
        CancellationToken ct = default);
}
