using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInventoryAdjustmentIndexReadService
{
    Task<InventoryAdjustmentIndexPageDto> GetPageAsync(
        InventoryAdjustmentIndexQueryRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<InventoryAdjustmentIndexWarehouseOptionDto>> GetWarehouseOptionsAsync(
        CancellationToken ct = default);

    Task<InventoryAdjustmentIndexQuickViewDto?> GetQuickViewAsync(
        int documentId,
        CancellationToken ct = default);
}
